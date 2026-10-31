using System.Security.Claims;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

// Una sesión abierta (una cookie): en qué equipo, desde dónde y cuándo se usó por última vez.
// Cerrarla aquí deja la cookie sin valor en la siguiente petición.
public class SesionAbierta
{
    public string Id { get; set; } = "";
    public string UsuarioId { get; set; } = "";
    public DateTimeOffset Creada { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UltimaVez { get; set; } = DateTimeOffset.UtcNow;
    public string Ip { get; set; } = "";
    public string Agente { get; set; } = "";
    public bool App { get; set; }
    public DateTimeOffset? Cerrada { get; set; }
}

// Cada intento de entrar, bien o mal
public class Acceso
{
    public int Id { get; set; }
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.UtcNow;
    public string Ip { get; set; } = "";
    public string Agente { get; set; } = "";
    public bool Exito { get; set; }
    public string Motivo { get; set; } = "";
}

// Ajustes sueltos de la app (clave -> valor)
public class Ajuste
{
    public string Clave { get; set; } = "";
    public string Valor { get; set; } = "";
}

public static class Sesiones
{
    public const string Claim = "mda:sid";
    public const string AvisarInicios = "avisar-inicios";
    private static readonly TimeSpan Refresco = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan EnLinea = TimeSpan.FromMinutes(2);

    private static string Agente(HttpContext ctx) => ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } a ? a[..Math.Min(a.Length, 400)] : "desconocido";

    // Al entrar: la cookie lleva el id de su sesión, que se apunta en la base
    public static async Task AlEntrar(CookieSigningInContext ctx)
    {
        if (ctx.Principal?.Identity is not ClaimsIdentity id || id.FindFirst(Claim) is not null) return;
        var s = await Nueva(ctx.HttpContext, ctx.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
        id.AddClaim(new Claim(Claim, s.Id));

        var sp = ctx.HttpContext.RequestServices;
        var db = sp.GetRequiredService<AppDbContext>();
        if (await db.Ajustes.AnyAsync(a => a.Clave == AvisarInicios && a.Valor == "si"))
        {
            var avisos = sp.GetRequiredService<IAvisos>();
            var hora = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, sp.GetRequiredService<Agenda.Zona>().Info);
            var texto = $"Se ha iniciado sesión en tu zona privada.\n\nEquipo: {Dispositivo.Nombre(s.Agente)}\nIP: {s.Ip}\nHora: {hora:dd/MM/yyyy HH:mm:ss}\n\nSi no has sido tú, ciérrala desde Seguridad y cambia la contraseña.";
            _ = Task.Run(async () => { await avisos.PorCorreo("🔐 Nuevo inicio de sesión", texto, "/seguridad"); await avisos.PorTelegram($"🔐 Nuevo inicio de sesión\n{texto}"); });
        }
    }

    private static async Task<SesionAbierta> Nueva(HttpContext ctx, string usuario)
    {
        var db = ctx.RequestServices.GetRequiredService<AppDbContext>();
        var s = new SesionAbierta { Id = Guid.NewGuid().ToString("N"), UsuarioId = usuario, Ip = IpCliente.De(ctx), Agente = Agente(ctx) };
        db.Sesiones.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    // En cada petición: la sesión tiene que seguir abierta. Las cookies de antes de esto se adoptan con una sesión nueva.
    public static async Task AlValidar(CookieValidatePrincipalContext ctx)
    {
        if (ctx.Principal?.Identity is not ClaimsIdentity { IsAuthenticated: true } id) return;
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var sid = id.FindFirst(Claim)?.Value;
        if (sid is null)
        {
            var s = await Nueva(ctx.HttpContext, ctx.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
            id.AddClaim(new Claim(Claim, s.Id));
            ctx.ReplacePrincipal(ctx.Principal);
            ctx.ShouldRenew = true;
            return;
        }
        var sesion = await db.Sesiones.FindAsync(sid);
        if (sesion is null || sesion.Cerrada is not null)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        var ahora = DateTimeOffset.UtcNow;
        if (ahora - sesion.UltimaVez > Refresco)
        {
            sesion.UltimaVez = ahora;
            sesion.Ip = IpCliente.De(ctx.HttpContext);
            sesion.Agente = Agente(ctx.HttpContext);
            await db.SaveChangesAsync();
        }
    }

    public static string? Actual(ClaimsPrincipal user) => user.FindFirstValue(Claim);

    // Registra los intentos de POST /api/auth/login con su resultado
    public static IApplicationBuilder UseRegistroAccesos(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        await next();
        if (!HttpMethods.IsPost(ctx.Request.Method) || !ctx.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)) return;
        var motivo = ctx.Response.StatusCode switch
        {
            200 => "ok", 401 => "contraseña o código incorrectos", 429 => "demasiados intentos", _ => $"error {ctx.Response.StatusCode}",
        };
        try
        {
            using var scope = ctx.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Accesos.Add(new Acceso { Ip = IpCliente.De(ctx), Agente = Agente(ctx), Exito = ctx.Response.StatusCode == 200, Motivo = motivo });
            await db.SaveChangesAsync();
            // solo se guardan los 300 últimos
            var viejos = await db.Accesos.OrderByDescending(a => a.Id).Skip(300).ToListAsync();
            if (viejos.Count > 0) { db.Accesos.RemoveRange(viejos); await db.SaveChangesAsync(); }
        }
        catch (Exception e) when (e is DbUpdateException or InvalidOperationException)
        {
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Accesos").LogWarning(e, "No se pudo registrar el acceso");
        }
    });
}

// «iPhone · Safari», «Windows · Chrome»… a partir del User-Agent
public static class Dispositivo
{
    public static string Sistema(string ua) =>
        ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Android") ? (ua.Contains("Mobile") ? "Android" : "Tablet Android")
        : ua.Contains("Windows") ? "Windows" : ua.Contains("Mac OS X") || ua.Contains("Macintosh") ? "Mac" : ua.Contains("CrOS") ? "Chromebook"
        : ua.Contains("Linux") ? "Linux" : "Otro";

    public static string Navegador(string ua) =>
        ua.Contains("Edg/") || ua.Contains("EdgiOS") ? "Edge" : ua.Contains("OPR/") ? "Opera" : ua.Contains("SamsungBrowser") ? "Samsung Internet"
        : ua.Contains("Firefox/") || ua.Contains("FxiOS") ? "Firefox" : ua.Contains("CriOS") || ua.Contains("Chrome/") ? "Chrome"
        : ua.Contains("Safari/") || ua.Contains("AppleWebKit") ? "Safari" : ua.Contains("curl") ? "curl" : "Navegador";

    public static string Tipo(string ua) => Sistema(ua) switch
    {
        "iPhone" or "Android" => "movil", "iPad" or "Tablet Android" => "tablet", "Otro" => "otro", _ => "ordenador",
    };

    public static string Nombre(string ua) => $"{Sistema(ua)} · {Navegador(ua)}";
}
