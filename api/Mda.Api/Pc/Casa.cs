using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Pc;

public record CasaEntrada(string Estado);
public record ConcentracionEntrada(bool Activo);
public record MultimediaEntrada(string Accion);
public record EventoAgente(string Tipo, string? Texto);
// qué quieres que te avise el PC (todo activado por defecto)
public record AvisosPc(bool Descargas = true, bool Windows = true, bool Copias = true, bool Bateria = true);

public static class AvisosPcAjustes
{
    public const string Clave = "notif-pc";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<AvisosPc> Leer(AppDbContext db, CancellationToken ct = default)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor;
        try { return v is null ? new AvisosPc() : JsonSerializer.Deserialize<AvisosPc>(v, Json) ?? new AvisosPc(); } catch (JsonException) { return new AvisosPc(); }
    }

    public static async Task Guardar(AppDbContext db, AvisosPc a, CancellationToken ct = default)
    {
        var x = await db.Ajustes.FindAsync([Clave], ct);
        if (x is null) db.Ajustes.Add(x = new Ajuste { Clave = Clave });
        x.Valor = JsonSerializer.Serialize(a, Json);
        await db.SaveChangesAsync(ct);
    }
}

// «Llegué a casa», «Me voy» y el modo concentración: varias órdenes seguidas al agente. No piden Face ID (no apagan nada):
// el que las lanza ya tiene sesión o un token de Atajos con el permiso «casa».
public static class Casa
{
    public const string ClaveConcentracion = "concentracion";
    private const string ClavePrevio = "concentracion-previo";

    private static async Task Guardar(AppDbContext db, string clave, string valor, CancellationToken ct)
    {
        var a = await db.Ajustes.FindAsync([clave], ct);
        if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = clave });
        a.Valor = valor;
        await db.SaveChangesAsync(ct);
    }

    public static async Task<bool> ConcentracionActiva(AppDbContext db, CancellationToken ct = default) =>
        await db.Ajustes.AsNoTracking().AnyAsync(a => a.Clave == ClaveConcentracion && a.Valor == "si", ct);

    public static async Task<(bool Ok, string Mensaje)> Aplicar(string estado, AppDbContext db, EstadoAgente agente, IAvisos avisos, TimeProvider reloj, string desde, CancellationToken ct = default)
    {
        if (estado is not ("llegada" or "salida")) return (false, "Estado no válido: «llegada» o «salida».");
        if (!agente.Conectado(OrdenesPc.AgenteConectado)) return (false, "El agente de Windows no está en marcha.");
        string mensaje;
        if (estado == "llegada")
        {
            await OrdenesPc.Encolar(db, reloj, "brillo", "70", desde, ct);
            await OrdenesPc.Encolar(db, reloj, "volumen", "30", desde, ct);
            await OrdenesPc.Encolar(db, reloj, "despierto", "off", desde, ct);
            mensaje = "Bienvenido a casa: brillo al 70 %, volumen al 30 % y el PC ya puede suspenderse.";
        }
        else
        {
            await OrdenesPc.Encolar(db, reloj, "despierto", "off", desde, ct);
            await OrdenesPc.Encolar(db, reloj, "bloquear", "", desde, ct);
            mensaje = "Hasta luego: PC bloqueado y con permiso para suspenderse.";
        }
        _ = avisos.PorTelegram($"🏠 {(estado == "llegada" ? "Llegaste a casa" : "Te vas de casa")}. {mensaje}", ct);
        return (true, mensaje);
    }

    // activar: se guarda cómo estaban el volumen y el brillo, se silencia, se baja la pantalla y se bloquea; desactivar: se devuelven
    public static async Task<(bool Ok, string Mensaje)> Concentracion(bool activo, AppDbContext db, EstadoAgente agente, IAvisos avisos, TimeProvider reloj, string desde, CancellationToken ct = default)
    {
        if (!agente.Conectado(OrdenesPc.AgenteConectado)) return (false, "El agente de Windows no está en marcha.");
        string mensaje;
        if (activo)
        {
            var m = agente.Reciente();
            await Guardar(db, ClavePrevio, $"{m?.Volumen ?? 40}|{m?.Brillo ?? 60}", ct);
            await OrdenesPc.Encolar(db, reloj, "volumen", "0", desde, ct);
            await OrdenesPc.Encolar(db, reloj, "brillo", "25", desde, ct);
            await OrdenesPc.Encolar(db, reloj, "bloquear", "", desde, ct);
            await Guardar(db, ClaveConcentracion, "si", ct);
            mensaje = "Modo concentración: PC silenciado, pantalla tenue y bloqueado.";
        }
        else
        {
            var previo = ((await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClavePrevio, ct))?.Valor ?? "40|60").Split('|');
            var vol = int.TryParse(previo.ElementAtOrDefault(0), out var v) ? v : 40;
            var bri = int.TryParse(previo.ElementAtOrDefault(1), out var b) ? b : 60;
            await OrdenesPc.Encolar(db, reloj, "volumen", vol.ToString(), desde, ct);
            await OrdenesPc.Encolar(db, reloj, "brillo", bri.ToString(), desde, ct);
            await Guardar(db, ClaveConcentracion, "no", ct);
            mensaje = $"Modo concentración apagado: volumen al {vol} % y brillo al {bri} %.";
        }
        _ = avisos.PorTelegram($"🎯 {mensaje}", ct);
        return (true, mensaje);
    }
}

// Cuándo avisar de la batería: baja (sin enchufar) y «lleva horas al 100 %». Pura: se prueba sin reloj ni red.
public sealed class ReglasBateria(int umbral = 15)
{
    private bool _bajaAvisada, _llenaAvisada;
    private DateTimeOffset? _llenaDesde;
    public static readonly TimeSpan TiempoLlena = TimeSpan.FromHours(2);

    public string? Evaluar(int? bateria, bool? cargando, DateTimeOffset ahora)
    {
        if (bateria is not { } b) return null;
        var enchufado = cargando == true;
        if (enchufado || b >= umbral + 5) _bajaAvisada = false;
        if (!enchufado && b <= umbral && !_bajaAvisada)
        {
            _bajaAvisada = true;
            return $"🔋 La batería del portátil está al {b} %. Enchúfalo.";
        }
        if (enchufado && b >= 100)
        {
            _llenaDesde ??= ahora;
            if (!_llenaAvisada && ahora - _llenaDesde >= TiempoLlena)
            {
                _llenaAvisada = true;
                return "🔌 El portátil lleva más de 2 horas enchufado al 100 %. Desenchufarlo de vez en cuando cuida la batería.";
            }
        }
        else { _llenaDesde = null; _llenaAvisada = false; }
        return null;
    }
}

public class AvisosBateria(IServiceScopeFactory scopes, EstadoAgente agente, IAvisos avisos, TimeProvider reloj, ILogger<AvisosBateria> log) : BackgroundService
{
    private readonly ReglasBateria _reglas = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(1), reloj);
        try { while (await t.WaitForNextTickAsync(ct)) await Revisar(ct); }
        catch (OperationCanceledException) { }
    }

    public async Task Revisar(CancellationToken ct)
    {
        try
        {
            var m = agente.Reciente();
            if (m is null || _reglas.Evaluar(m.Bateria, m.Cargando, reloj.GetUtcNow()) is not { } texto) return;
            using var scope = scopes.CreateScope();
            if (!(await AvisosPcAjustes.Leer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct)).Bateria) return;
            await avisos.PorPush("🔋 Batería del portátil", texto, "/pc", ct);
            await avisos.PorTelegram(texto, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Fallo en el aviso de batería"); }
    }
}

public static class CasaEndpoints
{
    private static readonly string[] AccionesMultimedia = ["playpause", "siguiente", "anterior", "silencio"];
    private static readonly string[] TiposEvento = ["descarga", "reinicio-pendiente"];

    private static string Desde(HttpContext ctx) => Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()) + " · " + IpCliente.De(ctx);

    public static void MapCasa(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pc").RequireAuthorization();

        g.MapPost("/casa", async (CasaEntrada e, AppDbContext db, EstadoAgente agente, IAvisos avisos, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            var (ok, mensaje) = await Casa.Aplicar((e.Estado ?? "").ToLowerInvariant(), db, agente, avisos, reloj, Desde(ctx), ct);
            return ok ? Results.Ok(new { mensaje }) : Results.Conflict(new { detalle = mensaje });
        }).RequireRateLimiting(Limites.Sensible);

        g.MapPost("/concentracion", async (ConcentracionEntrada e, AppDbContext db, EstadoAgente agente, IAvisos avisos, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            var (ok, mensaje) = await Casa.Concentracion(e.Activo, db, agente, avisos, reloj, Desde(ctx), ct);
            return ok ? Results.Ok(new { mensaje, activo = e.Activo }) : Results.Conflict(new { detalle = mensaje });
        }).RequireRateLimiting(Limites.Sensible);

        // música del PC (cualquier reproductor: Spotify de escritorio, el navegador…) con las teclas multimedia
        g.MapPost("/multimedia", async (MultimediaEntrada e, AppDbContext db, EstadoAgente agente, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            var accion = (e.Accion ?? "").ToLowerInvariant();
            if (!AccionesMultimedia.Contains(accion)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["accion"] = ["Acción no permitida."] });
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha." });
            await OrdenesPc.Encolar(db, reloj, "multimedia", accion, Desde(ctx), ct);
            return Results.NoContent();
        });

        g.MapGet("/avisos", async (AppDbContext db, CancellationToken ct) => Results.Ok(await AvisosPcAjustes.Leer(db, ct)));
        g.MapPut("/avisos", async (AvisosPc a, AppDbContext db, CancellationToken ct) => { await AvisosPcAjustes.Guardar(db, a, ct); return Results.NoContent(); });

        // el agente cuenta cosas que pasan en el PC (una descarga terminada, Windows pidiendo reiniciar) y la API las avisa si lo tienes activado
        app.MapPost("/api/sistema/agente/eventos", async (HttpRequest req, EventoAgente e, IConfiguration config, EstadoAgente agente, IServiceScopeFactory scopes, IAvisos avisos, IMemoryCache cache) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            agente.Visto();
            var tipo = (e.Tipo ?? "").ToLowerInvariant();
            if (!TiposEvento.Contains(tipo)) return Results.BadRequest();
            var texto = (e.Texto ?? "").Trim();
            if (texto.Length > 200) texto = texto[..200];
            // como mucho un aviso de cada tipo cada 20 s (una carpeta con 50 descargas no manda 50 notificaciones)
            if (cache.TryGetValue($"evento:{tipo}", out _)) return Results.NoContent();
            cache.Set($"evento:{tipo}", true, TimeSpan.FromSeconds(20));
            using var scope = scopes.CreateScope();
            var a = await AvisosPcAjustes.Leer(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            if (tipo == "descarga" && a.Descargas)
            {
                await avisos.PorPush("⬇️ Descarga terminada", texto, "/pc");
                await avisos.PorTelegram($"⬇️ Descarga terminada en el PC: {texto}");
            }
            else if (tipo == "reinicio-pendiente" && a.Windows)
            {
                await avisos.PorPush("🔄 Windows quiere reiniciar", "Hay actualizaciones esperando un reinicio.", "/pc");
                await avisos.PorTelegram("🔄 Windows ha terminado de preparar actualizaciones y pide reiniciar el PC.");
            }
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting(Limites.Agente);
    }
}
