using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// Ubicación simulada del iPhone, hecha desde el PC (el agente usa pymobiledevice3 por cable USB). Viene APAGADA; encenderla y cada cambio piden confirmar que eres tú,
// cada cambio te llega por Telegram, todo tiene hora de fin (como mucho 12 h, luego vuelve la real) y PARAR siempre se puede hacer, esté como esté el interruptor.
public record LugarGuardado(string Id, string Nombre, double Lat, double Lon);
// Lo que la API sabe que está en marcha (se escribe cuando el agente confirma la orden). «Modo»: fijo | andar | bici | coche
public record SimulacionActual(double Lat, double Lon, DateTimeOffset Desde, DateTimeOffset Hasta, string Modo, double? DestinoLat, double? DestinoLon, int Segundos);
public record PedirUbicacion(double Lat, double Lon, int Minutos, string? Modo);
public record NuevoLugar(string? Nombre, double Lat, double Lon);
public record EntradaUbicacion(bool? Activo);

public static class UbicacionIphone
{
    public const string ClaveActivo = "ubicacion-activo", ClaveLugares = "ubicacion-lugares", ClaveActual = "ubicacion-actual";
    public const int MinutosMaximos = 720, MaxLugares = 50, MaxNombre = 40;
    public static readonly Dictionary<string, double> Velocidades = new() { ["andar"] = 5, ["bici"] = 15, ["coche"] = 50 };
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool CoordenadasValidas(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    public static string Formato(double v) => Math.Round(v, 5).ToString("0.#####", CultureInfo.InvariantCulture);

    // la misma gramática que valida el agente
    public static string Parametro(string accion, double lat, double lon, int minutos, string? modo = null) =>
        accion == "ir" ? $"ir:{Formato(lat)},{Formato(lon)}:{minutos}:{modo}" : $"fijar:{Formato(lat)},{Formato(lon)}:{minutos}";

    public static Dictionary<string, string[]>? Errores(double lat, double lon, int minutos, string? modo, bool conModo)
    {
        var mal = new Dictionary<string, string[]>();
        if (!CoordenadasValidas(lat, lon)) mal["lat"] = ["Coordenadas no válidas (latitud −90 a 90, longitud −180 a 180)."];
        if (minutos is < 1 or > MinutosMaximos) mal["minutos"] = [$"El tiempo va de 1 a {MinutosMaximos} minutos."];
        if (conModo && !Velocidades.ContainsKey(modo ?? "")) mal["modo"] = [$"Elige {string.Join(", ", Velocidades.Keys)}."];
        return mal.Count == 0 ? null : mal;
    }

    public static double Distancia(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double g) => g * Math.PI / 180;
        var dLat = Rad(lat2 - lat1); var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * 6_371_000 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public static int Segundos(double metros, string modo) => Math.Max(1, (int)Math.Ceiling(metros / (Velocidades[modo] / 3.6)));

    // Dónde debería estar ahora (si va de camino, por el tiempo que lleva); null si ya pasó su hora de fin
    public static (double Lat, double Lon)? Posicion(SimulacionActual s, DateTimeOffset ahora)
    {
        if (ahora >= s.Hasta) return null;
        if (s.DestinoLat is not { } dl || s.DestinoLon is not { } dn || s.Segundos <= 0) return (s.Lat, s.Lon);
        var f = Math.Clamp((ahora - s.Desde).TotalSeconds / s.Segundos, 0, 1);
        var dLon = dn - s.Lon;
        if (dLon > 180) dLon -= 360; else if (dLon < -180) dLon += 360;
        var lon = s.Lon + dLon * f;
        if (lon > 180) lon -= 360; else if (lon < -180) lon += 360;
        return (s.Lat + (dl - s.Lat) * f, lon);
    }

    public static async Task<bool> Activo(AppDbContext db, CancellationToken ct = default) =>
        (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveActivo, ct))?.Valor == "si";

    public static async Task<List<LugarGuardado>> Lugares(AppDbContext db, CancellationToken ct = default)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveLugares, ct))?.Valor;
        if (string.IsNullOrEmpty(v)) return [];
        try { return JsonSerializer.Deserialize<List<LugarGuardado>>(v, Json) ?? []; } catch (JsonException) { return []; }
    }

    public static async Task<SimulacionActual?> Actual(AppDbContext db, TimeProvider reloj, CancellationToken ct = default)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveActual, ct))?.Valor;
        if (string.IsNullOrEmpty(v)) return null;
        try { return JsonSerializer.Deserialize<SimulacionActual>(v, Json) is { } s && s.Hasta > reloj.GetUtcNow() ? s : null; } catch (JsonException) { return null; }
    }

    static async Task Poner(AppDbContext db, string clave, string? valor, CancellationToken ct)
    {
        var f = await db.Ajustes.FindAsync([clave], ct);
        if (valor is null) { if (f is not null) db.Ajustes.Remove(f); return; }
        if (f is null) db.Ajustes.Add(new Ajuste { Clave = clave, Valor = valor }); else f.Valor = valor;
    }

    public static string Texto(OrdenPc o)
    {
        var p = o.Parametro.Split(':');
        return p[0] switch
        {
            "parar" => "restaurar la ubicación real del iPhone",
            "estado" => "mirar la ubicación del iPhone",
            "fijar" when p.Length == 3 => $"poner el iPhone en {p[1].Replace(",", ", ")} durante {p[2]} min",
            "ir" when p.Length == 4 => $"llevar el iPhone ({p[3]}) a {p[1].Replace(",", ", ")}",
            _ => "ubicación del iPhone",
        };
    }

    // Cuando el agente confirma una orden, la API apunta lo que de verdad quedó en marcha
    public static async Task Aplicar(AppDbContext db, OrdenPc o, bool ok, TimeProvider reloj, CancellationToken ct = default)
    {
        if (!ok) return;
        var ahora = reloj.GetUtcNow();
        var p = o.Parametro.Split(':');
        if (p[0] == "parar" || (p[0] == "estado" && o.Resultado.Contains("ubicación real", StringComparison.OrdinalIgnoreCase)))
        {
            await Poner(db, ClaveActual, null, ct);
        }
        else if (p[0] is "fijar" or "ir" && p.Length >= 3 && int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out var min))
        {
            var c = p[1].Split(',');
            if (c.Length != 2 || !double.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) || !double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return;
            SimulacionActual s;
            if (p[0] == "fijar") s = new(lat, lon, ahora, ahora.AddMinutes(min), "fijo", null, null, 0);
            else
            {
                // sale de donde estuviera la simulación anterior
                var antes = await Actual(db, reloj, ct);
                var (la, lo) = antes is null ? (lat, lon) : Posicion(antes, ahora) ?? (lat, lon);
                var modo = p.Length == 4 && Velocidades.ContainsKey(p[3]) ? p[3] : "andar";
                s = new(la, lo, ahora, ahora.AddMinutes(min), modo, lat, lon, Segundos(Distancia(la, lo, lat, lon), modo));
            }
            await Poner(db, ClaveActual, JsonSerializer.Serialize(s, Json), ct);
        }
        await db.SaveChangesAsync(ct);
    }

    // Al activar el pánico: si hay una ubicación simulada, vuelve la real
    public static async Task EnPanico(AppDbContext db, TimeProvider reloj, CancellationToken ct = default)
    {
        if (await Actual(db, reloj, ct) is null) return;
        await OrdenesPc.Encolar(db, reloj, "ubicacion", "parar", "pánico", ct);
    }

    static readonly Regex Control = new(@"[\p{C}]", RegexOptions.Compiled);
    public static string LimpiarNombre(string? n)
    {
        var t = Control.Replace((n ?? "").Trim(), "").Trim();
        return t.Length > MaxNombre ? t[..MaxNombre].TrimEnd() : t;
    }
}

public static class UbicacionIphoneEndpoints
{
    public static void MapUbicacionIphone(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pc/ubicacion").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, EstadoAgente agente, TimeProvider reloj, CancellationToken ct) =>
        {
            var ahora = reloj.GetUtcNow();
            var actual = await UbicacionIphone.Actual(db, reloj, ct);
            var recientes = (await db.OrdenesPc.AsNoTracking().Where(o => o.Tipo == "ubicacion" && o.Parametro != "estado").OrderByDescending(o => o.Id).Take(8).ToListAsync(ct))
                .Select(o => new { o.Id, o.Parametro, o.Estado, o.Creada, o.Resultado, o.Desde, texto = UbicacionIphone.Texto(o) });
            return Results.Ok(new
            {
                activo = await UbicacionIphone.Activo(db, ct),
                agenteConectado = agente.Conectado(OrdenesPc.AgenteConectado),
                actual,
                ahora = actual is null ? null : UbicacionIphone.Posicion(actual, ahora) is { } p ? new { lat = p.Lat, lon = p.Lon } : null,
                lugares = await UbicacionIphone.Lugares(db, ct),
                recientes,
            });
        });

        g.MapPut("/ajustes", async (EntradaUbicacion e, AppDbContext db, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            if (e.Activo is { } a)
            {
                var f = await db.Ajustes.FindAsync([UbicacionIphone.ClaveActivo], ct);
                if (f is null) db.Ajustes.Add(new Ajuste { Clave = UbicacionIphone.ClaveActivo, Valor = a ? "si" : "no" }); else f.Valor = a ? "si" : "no";
                await db.SaveChangesAsync(ct);
                // al apagarlo, si había una simulación en marcha vuelve la real
                if (!a && agente.Conectado(OrdenesPc.AgenteConectado) && await UbicacionIphone.Actual(db, reloj, ct) is not null)
                    await OrdenesPc.Encolar(db, reloj, "ubicacion", "parar", "ubicación apagada", ct);
                _ = avisos.PorTelegram($"📍 Ubicación del iPhone {(a ? "ENCENDIDA" : "apagada")} desde {Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString())} · {IpCliente.De(ctx)}.");
            }
            return Results.Ok(new { activo = await UbicacionIphone.Activo(db, ct) });
        }).RequireRateLimiting(Limites.Sensible);

        // Todas las órdenes al iPhone pasan por aquí: encendido, agente en marcha, confirmar que eres tú (salvo parar) y aviso por Telegram
        static async Task<IResult> Orden(string accion, double lat, double lon, int minutos, string? modo, AppDbContext db, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct)
        {
            var cambia = accion is "fijar" or "ir";
            if (cambia)
            {
                if (UbicacionIphone.Errores(lat, lon, minutos, modo, accion == "ir") is { } mal) return Results.ValidationProblem(mal);
                if (!await UbicacionIphone.Activo(db, ct)) return Results.Conflict(new { detalle = "La ubicación del iPhone está apagada: enciéndela primero." });
                if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            }
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha (agente\\arrancar.cmd)." });
            if (accion == "ir")
            {
                var actual = await UbicacionIphone.Actual(db, reloj, ct);
                if (actual is null) return Results.Conflict(new { detalle = "Primero fija un punto de partida: caminar sale desde donde esté el iPhone ahora." });
                var (la, lo) = UbicacionIphone.Posicion(actual, reloj.GetUtcNow()) ?? (actual.Lat, actual.Lon);
                var seg = UbicacionIphone.Segundos(UbicacionIphone.Distancia(la, lo, lat, lon), modo!);
                if (seg > minutos * 60) return Results.ValidationProblem(new Dictionary<string, string[]> { ["minutos"] = [$"Ese trayecto ({modo}) tarda {(seg >= 3600 ? $"{seg / 3600} h {seg % 3600 / 60} min" : $"{Math.Max(1, seg / 60)} min")}: sube el tiempo (hasta 12 h) o cambia de modo."] });
            }
            var parametro = accion switch { "parar" or "estado" => accion, _ => UbicacionIphone.Parametro(accion, lat, lon, minutos, modo) };
            var desde = Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()) + " · " + IpCliente.De(ctx);
            var o = await OrdenesPc.Encolar(db, reloj, "ubicacion", parametro, desde, ct);
            if (accion != "estado") _ = avisos.PorTelegram($"📍 Orden al PC: {UbicacionIphone.Texto(o)}.\nDesde {desde}. Si no has sido tú, cierra sesiones en Seguridad.");
            return Results.Ok(new { orden = o.Id });
        }

        g.MapPost("/fijar", (PedirUbicacion e, AppDbContext db, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
            Orden("fijar", e.Lat, e.Lon, e.Minutos, null, db, agente, avisos, ctx, reloj, ct)).RequireRateLimiting(Limites.Sensible);
        g.MapPost("/ir", (PedirUbicacion e, AppDbContext db, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
            Orden("ir", e.Lat, e.Lon, e.Minutos, (e.Modo ?? "").ToLowerInvariant(), db, agente, avisos, ctx, reloj, ct)).RequireRateLimiting(Limites.Sensible);
        g.MapPost("/parar", (AppDbContext db, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
            Orden("parar", 0, 0, 0, null, db, agente, avisos, ctx, reloj, ct)).RequireRateLimiting(Limites.Sensible);
        g.MapPost("/comprobar", (AppDbContext db, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
            Orden("estado", 0, 0, 0, null, db, agente, avisos, ctx, reloj, ct)).RequireRateLimiting(Limites.Sensible);

        // sitios guardados (solo viven en tu base de datos)
        g.MapPost("/lugares", async (NuevoLugar e, AppDbContext db, CancellationToken ct) =>
        {
            var nombre = UbicacionIphone.LimpiarNombre(e.Nombre);
            if (nombre.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Ponle un nombre al sitio."] });
            if (!UbicacionIphone.CoordenadasValidas(e.Lat, e.Lon)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["lat"] = ["Coordenadas no válidas."] });
            var lista = await UbicacionIphone.Lugares(db, ct);
            if (lista.Count >= UbicacionIphone.MaxLugares) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Máximo {UbicacionIphone.MaxLugares} sitios: borra alguno."] });
            var nuevo = new LugarGuardado(Guid.NewGuid().ToString("N")[..8], nombre, Math.Round(e.Lat, 5), Math.Round(e.Lon, 5));
            lista.Add(nuevo);
            await Guardar(db, lista, ct);
            return Results.Ok(lista);
        });

        g.MapDelete("/lugares/{id}", async (string id, AppDbContext db, CancellationToken ct) =>
        {
            var lista = await UbicacionIphone.Lugares(db, ct);
            if (lista.RemoveAll(l => l.Id == id) == 0) return Results.NotFound();
            await Guardar(db, lista, ct);
            return Results.Ok(lista);
        });
    }

    static async Task Guardar(AppDbContext db, List<LugarGuardado> lista, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(lista, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var f = await db.Ajustes.FindAsync([UbicacionIphone.ClaveLugares], ct);
        if (f is null) db.Ajustes.Add(new Ajuste { Clave = UbicacionIphone.ClaveLugares, Valor = json }); else f.Valor = json;
        await db.SaveChangesAsync(ct);
    }
}
