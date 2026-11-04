using System.Collections.Concurrent;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Galeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Seguridad;

// El botón de pánico: si crees que alguien tiene acceso, un toque (en la app o con /panico en Telegram) lo corta todo:
// cierra todas las sesiones, revoca los tokens de los atajos, apaga lo público y no deja iniciar sesión durante una hora.
// Se levanta antes con /desbloquear en Telegram (el chat vinculado es el canal de confianza).
public static class Panico
{
    public const string Clave = "panico-hasta";
    public static readonly TimeSpan Bloqueo = TimeSpan.FromHours(1);
    private static readonly string[] Publicos = [GaleriaEndpoints.Activa, "publico-estado", "publico-cpu"];

    public record Resultado(int Sesiones, int Tokens, DateTimeOffset Hasta);

    public static async Task<Resultado> Ejecutar(AppDbContext db, TimeProvider reloj, IMemoryCache cache, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow();
        var abiertas = await db.Sesiones.Where(s => s.Cerrada == null).ToListAsync(ct);
        foreach (var s in abiertas) s.Cerrada = ahora;
        var tokens = await db.TokensAtajo.Where(t => !t.Revocado).ToListAsync(ct);
        foreach (var t in tokens) t.Revocado = true;
        foreach (var clave in Publicos)
        {
            var a = await db.Ajustes.FindAsync([clave], ct);
            if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = clave });
            a.Valor = "no";
        }
        var hasta = ahora + Bloqueo;
        var b = await db.Ajustes.FindAsync([Clave], ct);
        if (b is null) db.Ajustes.Add(b = new Ajuste { Clave = Clave });
        b.Valor = hasta.ToString("O");
        await db.SaveChangesAsync(ct);
        cache.Remove(Clave);
        return new Resultado(abiertas.Count, tokens.Count, hasta);
    }

    public static async Task Levantar(AppDbContext db, IMemoryCache cache, CancellationToken ct = default)
    {
        if (await db.Ajustes.FindAsync([Clave], ct) is { } a) { db.Ajustes.Remove(a); await db.SaveChangesAsync(ct); }
        cache.Remove(Clave);
    }

    public static async Task<DateTimeOffset?> BloqueadoHasta(AppDbContext db, IMemoryCache cache, TimeProvider reloj, CancellationToken ct = default)
    {
        // se mira como mucho cada 5 s: este chequeo va delante de cada intento de entrar
        var hasta = await cache.GetOrCreateAsync(Clave, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);
            var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor;
            return DateTimeOffset.TryParse(v, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : (DateTimeOffset?)null;
        });
        return hasta is { } h && h > reloj.GetUtcNow() ? h : null;
    }

    public static string Resumen(Resultado r) => $"{r.Sesiones} sesión(es) cerradas, {r.Tokens} token(s) revocados, galería y estado públicos apagados. Nadie puede iniciar sesión hasta las {r.Hasta.ToLocalTime():HH:mm}.";

    public static void MapPanico(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/seguridad/panico", async (AppDbContext db, TimeProvider reloj, IMemoryCache cache, IAvisos avisos, HttpContext ctx, CancellationToken ct) =>
        {
            var r = await Ejecutar(db, reloj, cache, ct);
            await avisos.PorTelegram($"🚨 Botón de pánico pulsado desde {Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString())} ({IpCliente.De(ctx)}).\n{Resumen(r)}\nPara volver a entrar antes, escribe /desbloquear.", ct);
            return Results.Ok(new { r.Sesiones, r.Tokens, hasta = r.Hasta });
        }).RequireAuthorization().RequireRateLimiting(Limites.Sensible);
    }

    // El login y el passkey no contestan mientras dure el bloqueo (429 con cuánto falta)
    public static IApplicationBuilder UsePanico(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var p = ctx.Request.Path;
        if (HttpMethods.IsPost(ctx.Request.Method) && (p.StartsWithSegments("/api/auth/login") || p.StartsWithSegments("/api/auth/passkey/entrada") || p.StartsWithSegments("/api/auth/refresh")))
        {
            using var scope = ctx.RequestServices.CreateScope();
            if (await BloqueadoHasta(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ctx.RequestServices.GetRequiredService<IMemoryCache>(), ctx.RequestServices.GetRequiredService<TimeProvider>()) is { } h)
            {
                ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ctx.Response.WriteAsJsonAsync(new { title = "Acceso bloqueado temporalmente", detail = $"Se pulsó el botón de pánico. Podrás entrar a las {h.ToLocalTime():HH:mm}." });
                return;
            }
        }
        await next();
    });
}

// Sondas: rutas que solo pide quien busca un hueco (/.env, /wp-login.php, /admin…). Contestan 404 como cualquier ruta inexistente,
// se apuntan y esa IP queda bloqueada una hora. Nunca se bloquea una IP local o desconocida (detrás del proxy valdría para todos).
public sealed class Sondas(TimeProvider reloj)
{
    private static readonly HashSet<string> Rutas = new(StringComparer.OrdinalIgnoreCase)
    {
        "/.env", "/.git/config", "/.git/HEAD", "/wp-login.php", "/wp-admin", "/wp-admin/", "/xmlrpc.php", "/phpmyadmin", "/phpmyadmin/", "/pma", "/admin", "/admin/",
        "/administrator", "/administrator/", "/server-status", "/actuator/env", "/actuator/health", "/.aws/credentials", "/config.json", "/backup.sql", "/db.sql",
        "/vendor/phpunit/phpunit/src/Util/PHP/eval-stdin.php", "/cgi-bin/luci", "/boaform/admin/formLogin", "/solr/admin/info/system", "/.DS_Store", "/id_rsa",
    };
    public static readonly TimeSpan Castigo = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _bloqueadas = new();
    private readonly ConcurrentQueue<(DateTimeOffset Cuando, string Ip, string Ruta)> _pendientes = new();

    public static bool EsSonda(PathString ruta) => Rutas.Contains(ruta.Value ?? "");

    public static bool Bloqueable(string ip) =>
        System.Net.IPAddress.TryParse(ip, out var a) && !System.Net.IPAddress.IsLoopback(a) && !EsPrivada(a);

    private static bool EsPrivada(System.Net.IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) return a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || (a.GetAddressBytes()[0] & 0xFE) == 0xFC;
        var b = a.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    public bool EstaBloqueada(string ip) => _bloqueadas.TryGetValue(ip, out var hasta) && (hasta > reloj.GetUtcNow() || !_bloqueadas.TryRemove(ip, out _));

    public void Anotar(string ip, string ruta)
    {
        _pendientes.Enqueue((reloj.GetUtcNow(), ip, ruta));
        if (Bloqueable(ip)) _bloqueadas[ip] = reloj.GetUtcNow() + Castigo;
    }

    // lo que ha pasado desde el último resumen (y lo vacía)
    public List<(DateTimeOffset Cuando, string Ip, string Ruta)> Vaciar()
    {
        var l = new List<(DateTimeOffset, string, string)>();
        while (_pendientes.TryDequeue(out var s)) l.Add(s);
        return l;
    }
}

public static class SondasMiddleware
{
    public static IApplicationBuilder UseSondas(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var sondas = ctx.RequestServices.GetRequiredService<Sondas>();
        var ip = IpCliente.De(ctx);
        if (sondas.EstaBloqueada(ip)) { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
        if (Sondas.EsSonda(ctx.Request.Path))
        {
            sondas.Anotar(ip, ctx.Request.Path.Value!);
            // a una IP de fuera se le hace perder el tiempo (tarpit); a una de casa, o con el tope lleno, el 404 de siempre
            if (Sondas.Bloqueable(ip) && await ctx.RequestServices.GetRequiredService<Tarpit>().Gotear(ctx)) return;
            ctx.Response.StatusCode = StatusCodes.Status404NotFound; // idéntico a lo que da una ruta que no existe
            return;
        }
        await next();
    });
}

// Un resumen de las sondas cada hora (solo si hubo alguna): cuántas y desde dónde
public class ResumenSondas(Sondas sondas, IAvisos avisos, TimeProvider reloj, ILogger<ResumenSondas> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var t = new PeriodicTimer(TimeSpan.FromHours(1), reloj);
        try { while (await t.WaitForNextTickAsync(ct)) await Resumir(ct); }
        catch (OperationCanceledException) { }
    }

    public async Task Resumir(CancellationToken ct)
    {
        var l = sondas.Vaciar();
        if (l.Count == 0) return;
        var ips = l.GroupBy(x => x.Ip).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key} ({g.Count()})");
        var rutas = l.GroupBy(x => x.Ruta).OrderByDescending(g => g.Count()).Take(4).Select(g => g.Key);
        log.LogWarning("Sondas: {Cantidad} en la última hora", l.Count);
        await avisos.PorTelegram($"🕵️ {l.Count} sonda(s) en la última hora, bloqueadas 1 h.\nDesde: {string.Join(", ", ips)}\nBuscaban: {string.Join(", ", rutas)}", ct);
    }
}

// Latido para Healthchecks.io (gratis): la API avisa cada 5 minutos de que sigue viva. Si el PC se apaga, se corta internet
// o Docker se cae, el latido para y Healthchecks te manda un correo o un Telegram. HEALTHCHECK_URL en el .env; sin ella, no hace nada.
public class LatidoExterno(IConfiguration config, IHttpClientFactory http, TimeProvider reloj, ILogger<LatidoExterno> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (config["Healthcheck:Url"] is not { Length: > 0 } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(5), reloj);
        try
        {
            do { await Latir(uri, ct); } while (await t.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    public async Task<bool> Latir(Uri uri, CancellationToken ct)
    {
        try { using var r = await http.CreateClient("avisos").GetAsync(uri, ct); return r.IsSuccessStatusCode; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("No se pudo enviar el latido ({Motivo})", e.GetType().Name); // la URL lleva un identificador secreto: no se escribe
            return false;
        }
    }
}
