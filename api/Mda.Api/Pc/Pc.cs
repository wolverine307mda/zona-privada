using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Mda.Api.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// ---------------- salud del PC (lo manda el agente de Windows) ----------------

public record DiscoSalud(string Nombre, string Modelo, string Salud, string Estado, double? TemperaturaC, int? DesgastePct, long? ErroresLectura);
public record VolumenSalud(string Nombre, long LibreBytes, long TotalBytes);
public record SaludPc(DateTimeOffset Hora, IReadOnlyList<DiscoSalud> Discos, IReadOnlyList<VolumenSalud> Volumenes, bool ReinicioPendiente);

// Una foto de la salud por día (los últimos 90): sirve para ver a dónde va el espacio y el desgaste de los discos
public class SaludDia
{
    public DateOnly Fecha { get; set; }
    public string Datos { get; set; } = "[]";   // {"v":[[nombre,libre,total]],"d":[[nombre,desgaste,temperatura]]}
}

public static class HistorialSalud
{
    public const int Dias = 90;

    public static async Task Guardar(AppDbContext db, SaludPc s, DateOnly hoy, CancellationToken ct = default)
    {
        var datos = JsonSerializer.Serialize(new
        {
            v = s.Volumenes.Select(x => new object[] { x.Nombre, x.LibreBytes, x.TotalBytes }),
            d = s.Discos.Select(x => new object?[] { x.Nombre, x.DesgastePct, x.TemperaturaC }),
        });
        var fila = await db.SaludDias.FindAsync([hoy], ct);
        if (fila is null) db.SaludDias.Add(fila = new SaludDia { Fecha = hoy });
        fila.Datos = datos;
        db.SaludDias.RemoveRange(await db.SaludDias.Where(d => d.Fecha < hoy.AddDays(-Dias)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    public static async Task<object> Leer(AppDbContext db, CancellationToken ct = default)
    {
        var filas = await db.SaludDias.AsNoTracking().OrderBy(d => d.Fecha).ToListAsync(ct);
        return filas.Select(f =>
        {
            var j = JsonDocument.Parse(f.Datos).RootElement;
            return new
            {
                fecha = f.Fecha.ToString("yyyy-MM-dd"),
                volumenes = j.GetProperty("v").EnumerateArray().Select(x => new { nombre = x[0].GetString(), libreBytes = x[1].GetInt64(), totalBytes = x[2].GetInt64() }),
                discos = j.GetProperty("d").EnumerateArray().Select(x => new { nombre = x[0].GetString(), desgastePct = x[1].ValueKind == JsonValueKind.Number ? x[1].GetInt32() : (int?)null, temperaturaC = x[2].ValueKind == JsonValueKind.Number ? x[2].GetDouble() : (double?)null }),
            };
        }).ToList();
    }
}

// Lo último que mandó el agente; se guarda también en la base para no perderlo al reiniciar
public class EstadoSalud(TimeProvider reloj)
{
    public const string Clave = "salud-pc";
    private SaludPc? _ultima;
    private DateTimeOffset _recibida;
    public SaludPc? Ultima => _ultima;
    public DateTimeOffset Recibida => _recibida;
    public bool Reciente => _ultima is not null && reloj.GetUtcNow() - _recibida < TimeSpan.FromMinutes(30);
    public void Recibir(SaludPc s) { _ultima = s; _recibida = reloj.GetUtcNow(); }
    public void Restaurar(SaludPc s, DateTimeOffset cuando) { _ultima = s; _recibida = cuando; }
}

// Las reglas: qué está mal ahora mismo. Cada una tiene un id estable para avisar una vez al día y decir «resuelto» al arreglarse.
public static class ReglasSalud
{
    public static readonly TimeSpan ReinicioMaximo = TimeSpan.FromDays(7);

    public static Dictionary<string, string> Evaluar(SaludPc s, DateTimeOffset? reinicioDesde, DateTimeOffset ahora, double? encendidoDias)
    {
        var activas = new Dictionary<string, string>();
        foreach (var v in s.Volumenes.Where(v => v.TotalBytes > 0))
        {
            var libre = 100.0 * v.LibreBytes / v.TotalBytes;
            var gb = v.LibreBytes / 1_073_741_824.0;
            if (libre < 8) activas[$"disco-libre:{v.Nombre}"] = $"🔴 Disco {v.Nombre} casi lleno: quedan {gb:0.#} GB ({libre:0} %)";
            else if (libre < 15) activas[$"disco-libre:{v.Nombre}"] = $"🟠 Disco {v.Nombre} al {100 - libre:0} %: quedan {gb:0.#} GB";
        }
        foreach (var d in s.Discos)
        {
            if (!string.Equals(d.Salud, "Healthy", StringComparison.OrdinalIgnoreCase) && d.Salud.Length > 0)
                activas[$"disco-salud:{d.Nombre}"] = $"🔴 El disco {d.Modelo} ({d.Nombre}) no está sano: {d.Salud}. Haz copia de lo importante ya";
            if (d.TemperaturaC is >= 70) activas[$"disco-temp:{d.Nombre}"] = $"🟠 El disco {d.Modelo} está a {d.TemperaturaC:0} °C";
            if (d.DesgastePct is >= 90) activas[$"disco-desgaste:{d.Nombre}"] = $"🟠 El disco {d.Modelo} lleva un {d.DesgastePct} % de desgaste";
        }
        if (s.ReinicioPendiente && reinicioDesde is { } desde && ahora - desde > ReinicioMaximo)
            activas["reinicio"] = $"🟠 Windows lleva {(int)(ahora - desde).TotalDays} días pidiendo reiniciar para terminar actualizaciones";
        if (encendidoDias is > 45) activas["encendido"] = $"🟡 El PC lleva {encendidoDias:0} días encendido sin reiniciar";
        return activas;
    }

    // el texto de «ya está bien» a partir del id de la regla
    public static string Resuelto(string id) => id.Split(':')[0] switch
    {
        "disco-libre" => $"✅ Ya hay espacio en el disco {id[(id.IndexOf(':') + 1)..]}",
        "disco-salud" => $"✅ El disco {id[(id.IndexOf(':') + 1)..]} vuelve a estar sano",
        "disco-temp" => $"✅ El disco {id[(id.IndexOf(':') + 1)..]} ha bajado de temperatura",
        "disco-desgaste" => $"✅ El desgaste del disco {id[(id.IndexOf(':') + 1)..]} ya no es preocupante",
        "reinicio" => "✅ El PC ya se ha reiniciado",
        "encendido" => "✅ El PC se ha reiniciado",
        _ => "✅ Resuelto",
    };
}

// Cada 10 minutos mira la última salud y avisa por Telegram y notificación: una vez al día por problema, y cuando se arregla
public class AvisosSalud(IServiceScopeFactory scopes, EstadoSalud salud, EstadoSistema sistema, IAvisos avisos, Agenda.Zona zona, TimeProvider reloj, ILogger<AvisosSalud> log) : BackgroundService
{
    public const string ClaveAvisadas = "salud-avisadas";
    public const string ClaveReinicio = "salud-reinicio-desde";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // lo último que mandó el agente antes de reiniciar la API (para enseñarlo en «Mi PC» mientras el agente vuelve)
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == EstadoSalud.Clave, ct) is { } a && JsonSerializer.Deserialize<SaludPc>(a.Valor, JsonSerializerOptions.Web) is { } s)
                salud.Restaurar(s, s.Hora);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or System.Data.Common.DbException) { log.LogWarning("No se pudo restaurar la última salud del PC"); }
        try { await Task.Delay(TimeSpan.FromMinutes(1), reloj, ct); } catch (OperationCanceledException) { return; }
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(10), reloj);
        do
        {
            try { await Revisar(ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Fallo revisando la salud del PC"); }
        } while (await t.WaitForNextTickAsync(ct));
    }

    public async Task Revisar(CancellationToken ct)
    {
        if (!salud.Reciente || salud.Ultima is not { } s) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ahora = reloj.GetUtcNow();

        // desde cuándo pide reiniciar Windows
        var desdeAj = await db.Ajustes.FindAsync([ClaveReinicio], ct);
        DateTimeOffset? desde = DateTimeOffset.TryParse(desdeAj?.Valor, out var d) ? d : null;
        if (s.ReinicioPendiente && desde is null)
        {
            desde = ahora;
            db.Ajustes.Add(new Ajuste { Clave = ClaveReinicio, Valor = ahora.ToString("O") });
        }
        else if (!s.ReinicioPendiente && desdeAj is not null) { db.Ajustes.Remove(desdeAj); desde = null; }

        var encendidoDias = sistema.Historial().LastOrDefault()?.EncendidoSegundos / 86400;
        var activas = ReglasSalud.Evaluar(s, desde, ahora, encendidoDias);

        var reg = await db.Ajustes.FindAsync([ClaveAvisadas], ct);
        var avisadas = new Dictionary<string, string>();
        try { if (reg?.Valor is { Length: > 0 } v) avisadas = JsonSerializer.Deserialize<Dictionary<string, string>>(v) ?? []; } catch (JsonException) { /* se reconstruye */ }
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, zona.Info).DateTime).ToString("O");

        foreach (var (id, texto) in activas)
        {
            if (avisadas.TryGetValue(id, out var ultimo) && ultimo == hoy) continue;
            await avisos.PorPush("💻 Salud del PC", texto, "/pc", ct);
            await avisos.PorTelegram($"💻 {texto}", ct);
            avisadas[id] = hoy;
        }
        foreach (var id in avisadas.Keys.Where(k => !activas.ContainsKey(k)).ToList())
        {
            var texto = ReglasSalud.Resuelto(id);
            await avisos.PorPush("💻 Salud del PC", texto, "/pc", ct);
            await avisos.PorTelegram($"💻 {texto}", ct);
            avisadas.Remove(id);
        }
        if (reg is null) db.Ajustes.Add(reg = new Ajuste { Clave = ClaveAvisadas });
        reg.Valor = JsonSerializer.Serialize(avisadas);
        await db.SaveChangesAsync(ct);
    }
}

// ---------------- órdenes al PC (las ejecuta el agente) ----------------

public class OrdenPc
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "";
    public string Parametro { get; set; } = "";
    // pendiente → enviada (la cogió el agente) → hecha | error; o caducada si nadie la cogió a tiempo
    public string Estado { get; set; } = "pendiente";
    public DateTimeOffset Creada { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Caduca { get; set; }
    public string Resultado { get; set; } = "";
    public string Desde { get; set; } = "";
}

public record OrdenEntrada(string Tipo, string? Parametro);
public record ResultadoOrden(bool Ok, string? Resultado);

public static class OrdenesPc
{
    // lista cerrada: el agente rechaza cualquier otra cosa aunque llegara
    public static readonly string[] Tipos = ["bloquear", "despierto", "reiniciar", "suspender", "apagar", "cancelar", "volumen", "brillo"];
    // órdenes inocuas (no apagan ni exponen nada): no piden volver a confirmar con Face ID cada vez
    public static readonly string[] SinConfirmar = ["volumen", "brillo"];
    public static readonly TimeSpan Validez = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan AgenteConectado = TimeSpan.FromSeconds(15);

    // la firma que el agente comprueba con el secreto compartido: sin ella, una orden de la base de datos o del camino no vale
    public static string Firma(string secreto, OrdenPc o) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), Encoding.UTF8.GetBytes($"{o.Id}|{o.Tipo}|{o.Parametro}|{o.Caduca.ToUnixTimeSeconds()}"))).ToLowerInvariant();

    public static string Texto(OrdenPc o) => o.Tipo switch
    {
        "bloquear" => "bloquear la pantalla",
        "despierto" => o.Parametro == "off" ? "dejar que el PC se suspenda" : "mantener el PC despierto",
        "reiniciar" => "reiniciar (en 30 s)",
        "suspender" => "suspender",
        "apagar" => "apagar (en 30 s)",
        "cancelar" => "cancelar el apagado o reinicio",
        "volumen" => $"volumen al {o.Parametro} %",
        "brillo" => $"brillo de la pantalla al {o.Parametro} %",
        _ => o.Tipo,
    };

    // la ruta del agente: directo a la API (nunca por el proxy) y con el token compartido
    internal static IResult? ComprobarAgente(HttpRequest req, IConfiguration config)
    {
        var esperado = config["Agente:Token"];
        var porProxy = req.Headers.ContainsKey("X-Forwarded-For") || req.Headers.ContainsKey("X-Original-For");
        if (esperado is not { Length: >= SistemaEndpoints.TokenMinimo } || porProxy) return Results.NotFound();
        var recibido = req.Headers["X-Agente-Token"].ToString();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recibido), Encoding.UTF8.GetBytes(esperado)) ? null : Results.Unauthorized();
    }

    public static void MapPc(this IEndpointRouteBuilder app)
    {
        // ---- la pantalla «Mi PC» (con sesión) ----
        var g = app.MapGroup("/api/pc").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, EstadoSalud salud, EstadoAgente agente, HttpContext ctx, TimeProvider reloj, CancellationToken ct) => Results.Ok(new
        {
            agenteConectado = agente.Conectado(AgenteConectado),
            salud = salud.Ultima,
            saludHace = salud.Ultima is null ? (double?)null : (reloj.GetUtcNow() - salud.Recibida).TotalSeconds,
            reautenticado = await Reautenticar.Vigente(ctx, db, reloj),
            ordenes = await db.OrdenesPc.AsNoTracking().OrderByDescending(o => o.Id).Take(8).ToListAsync(ct),
        }));

        // la foto diaria de los últimos 90 días (espacio libre y desgaste de cada disco)
        g.MapGet("/historial", async (AppDbContext db, CancellationToken ct) => Results.Ok(await HistorialSalud.Leer(db, ct)));

        g.MapPost("/ordenes", async (OrdenEntrada e, AppDbContext db, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            var tipo = (e.Tipo ?? "").ToLowerInvariant();
            if (!Tipos.Contains(tipo)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["tipo"] = ["Orden no permitida."] });
            var parametro = tipo == "despierto" ? (e.Parametro == "off" ? "off" : "on") : "";
            if (tipo is "volumen" or "brillo")
            {
                if (!int.TryParse(e.Parametro, out var nivel) || nivel is < 0 or > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["parametro"] = ["Pon un número de 0 a 100."] });
                parametro = nivel.ToString();
            }
            if (!agente.Conectado(AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha (agente\\arrancar.cmd)." });
            if (!SinConfirmar.Contains(tipo) && !await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            var ahora = reloj.GetUtcNow();
            var o = new OrdenPc { Tipo = tipo, Parametro = parametro, Creada = ahora, Caduca = ahora.Add(Validez), Desde = Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()) + " · " + IpCliente.De(ctx) };
            db.OrdenesPc.Add(o);
            await db.SaveChangesAsync(ct);
            // cada orden deja rastro: te llega un aviso aunque la hayas dado tú
            _ = avisos.PorTelegram($"💻 Orden al PC: {Texto(o)}\nDesde {o.Desde}. Si no has sido tú, cierra sesiones en Seguridad.");
            return Results.Ok(o);
        }).RequireRateLimiting(Limites.Sensible);

        g.MapGet("/ordenes/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
            await db.OrdenesPc.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct) is { } o ? Results.Ok(o) : Results.NotFound());

        // ---- lo que usa el agente (token compartido, sin pasar por el proxy) ----
        var a = app.MapGroup("/api/sistema/agente").AllowAnonymous().RequireRateLimiting(Limites.Agente);

        a.MapPost("/salud", async (HttpRequest req, EstadoSalud salud, EstadoAgente agente, IConfiguration config, IServiceScopeFactory scopes) =>
        {
            if (ComprobarAgente(req, config) is { } no) return no;
            agente.Visto();
            const int Max = 32 * 1024;
            if (req.ContentLength > Max) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            SaludPc? s;
            try { s = await req.ReadFromJsonAsync<SaludPc>(); } catch (Exception e) when (e is JsonException or BadHttpRequestException) { return Results.BadRequest(); }
            if (s is null || s.Discos.Count > 32 || s.Volumenes.Count > 32) return Results.BadRequest();
            salud.Recibir(s with { Hora = DateTimeOffset.UtcNow });
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var x = await db.Ajustes.FindAsync([EstadoSalud.Clave]);
            if (x is null) db.Ajustes.Add(x = new Ajuste { Clave = EstadoSalud.Clave });
            x.Valor = JsonSerializer.Serialize(salud.Ultima, JsonSerializerOptions.Web);
            await db.SaveChangesAsync();
            await HistorialSalud.Guardar(db, salud.Ultima!, DateOnly.FromDateTime(DateTime.UtcNow));
            return Results.NoContent();
        });

        // el agente pregunta (espera hasta 20 s) si hay órdenes y las recibe firmadas
        a.MapGet("/ordenes", async (HttpRequest req, AppDbContext db, EstadoAgente agente, IConfiguration config, TimeProvider reloj, CancellationToken ct) =>
        {
            if (ComprobarAgente(req, config) is { } no) return no;
            var secreto = config["Agente:Token"]!;
            var limite = reloj.GetUtcNow().AddSeconds(config.GetValue("Agente:EsperaSegundos", 20));
            do
            {
                agente.Visto();
                var ahora = reloj.GetUtcNow();
                foreach (var v in await db.OrdenesPc.Where(o => o.Estado == "pendiente" && o.Caduca <= ahora).ToListAsync(ct)) v.Estado = "caducada";
                var pendientes = await db.OrdenesPc.Where(o => o.Estado == "pendiente" && o.Caduca > ahora).OrderBy(o => o.Id).ToListAsync(ct);
                foreach (var o in pendientes) o.Estado = "enviada";
                await db.SaveChangesAsync(ct);
                if (pendientes.Count > 0)
                    return Results.Ok(pendientes.Select(o => new { o.Id, o.Tipo, o.Parametro, caduca = o.Caduca.ToUnixTimeSeconds(), firma = Firma(secreto, o) }));
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            } while (reloj.GetUtcNow() < limite && !ct.IsCancellationRequested);
            return Results.Ok(Array.Empty<object>());
        });

        a.MapPost("/ordenes/{id:int}", async (int id, ResultadoOrden r, HttpRequest req, AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            if (ComprobarAgente(req, config) is { } no) return no;
            var o = await db.OrdenesPc.FindAsync([id], ct);
            if (o is null) return Results.NotFound();
            o.Estado = r.Ok ? "hecha" : "error";
            o.Resultado = (r.Resultado ?? "").Length > 200 ? r.Resultado![..200] : r.Resultado ?? "";
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
