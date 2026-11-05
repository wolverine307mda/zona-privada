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
    // hasta cuándo vale la «contraseña otra vez» para acciones delicadas (órdenes al PC)
    public DateTimeOffset? ReautenticadaHasta { get; set; }
    // «pedir Face ID al volver»: segundos fuera de la app a partir de los cuales la sesión se bloquea (null = nunca)
    public int? BloquearTrasSegundos { get; set; }
    public DateTimeOffset? SegundoPlano { get; set; }
    public bool Bloqueada { get; set; }
    // PIN de Seguridad: hasta cuándo vale en esta sesión, fallos seguidos y bloqueo tras cinco
    public DateTimeOffset? PinHasta { get; set; }
    public int PinFallos { get; set; }
    public DateTimeOffset? PinBloqueadoHasta { get; set; }
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
    // un fallo que ya has mirado: deja de contar como incidencia (semáforo de Inicio, contador de Seguridad)
    public bool Revisado { get; set; }
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
    // «no» = avisar de todos los inicios; sin valor o «si» = solo de equipos o países nuevos
    public const string AvisarSoloNuevos = "avisar-solo-nuevos";
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
        // se apunta el equipo siempre (así aprende cuáles son los tuyos); un fallo aquí nunca impide entrar
        EntradaEquipo? entrada = null; Geo? geo = null;
        try
        {
            geo = sp.GetService<GeoIp>()?.Buscar(s.Ip);
            entrada = await Equipos.Registrar(db, s.Agente, s.Ip, geo, DateTimeOffset.UtcNow);
        }
        catch (Exception e) when (e is DbUpdateException or InvalidOperationException)
        {
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("Equipos").LogWarning(e, "No se pudo apuntar el equipo");
        }
        if (await db.Ajustes.AnyAsync(a => a.Clave == AvisarInicios && a.Valor == "si"))
        {
            var soloNuevos = !await db.Ajustes.AnyAsync(a => a.Clave == AvisarSoloNuevos && a.Valor == "no");
            if (soloNuevos && entrada is { Nuevo: false }) return;
            var avisos = sp.GetRequiredService<IAvisos>();
            var hora = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, sp.GetRequiredService<Agenda.Zona>().Info);
            var lugar = geo is { } g ? $"\nLugar: {(g.Ciudad.Length > 0 ? g.Ciudad + ", " : "")}{g.Pais}" : "";
            var novedad = entrada is { PaisNuevo: true } ? $"\n\n⚠️ Es la primera vez que entras desde {geo?.Pais}." : entrada is { Nuevo: true } ? "\n\nEs la primera vez que entras desde este equipo." : "";
            var texto = $"Se ha iniciado sesión en tu zona privada.\n\nEquipo: {Dispositivo.Nombre(s.Agente)}\nIP: {s.Ip}{lugar}\nHora: {hora:dd/MM/yyyy HH:mm:ss}{novedad}\n\nSi no has sido tú, ciérrala desde Seguridad.";
            var asunto = entrada is { PaisNuevo: true } ? "🔐 Inicio de sesión desde un país nuevo" : entrada is { Nuevo: true } ? "🔐 Inicio de sesión desde un equipo nuevo" : "🔐 Nuevo inicio de sesión";
            _ = Task.Run(async () => { await avisos.PorCorreo(asunto, texto, "/seguridad"); await avisos.PorTelegram($"{asunto}\n{texto}"); await avisos.PorPush(asunto, texto, "/seguridad"); });
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

    // Una sesión vale como mucho 30 días aunque se use a diario: pasado ese tiempo hay que volver a entrar
    public static readonly TimeSpan Maxima = TimeSpan.FromDays(30);
    public const string ClaveBloqueada = "mda:bloqueada";

    // En cada petición: la sesión tiene que seguir abierta. Una cookie sin id de sesión no se acepta: la firma sola
    // no basta (si se filtraran las claves de firma, se podrían fabricar cookies válidas para siempre).
    public static async Task AlValidar(CookieValidatePrincipalContext ctx)
    {
        if (ctx.Principal?.Identity is not ClaimsIdentity { IsAuthenticated: true } id) return;
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var sid = id.FindFirst(Claim)?.Value;
        var ahora = DateTimeOffset.UtcNow;
        var sesion = sid is null ? null : await db.Sesiones.FindAsync(sid);
        if (sesion is null || sesion.Cerrada is not null || ahora - sesion.Creada > Maxima)
        {
            if (sesion is { Cerrada: null }) { sesion.Cerrada = ahora; await db.SaveChangesAsync(); }
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        // bloqueo al volver a la app: si estuvo fuera más del umbral (o no se oyó de ella en un buen rato), pide desbloquear
        if (sesion.BloquearTrasSegundos is { } umbral)
        {
            if (!sesion.Bloqueada)
            {
                if (sesion.SegundoPlano is { } sp)
                {
                    if (ahora - sp > TimeSpan.FromSeconds(umbral)) sesion.Bloqueada = true; else sesion.SegundoPlano = null;
                }
                // red de seguridad: si el aviso de «salgo de la app» no llegó (iOS la cerró de golpe), el silencio largo también bloquea
                else if (ahora - sesion.UltimaVez > TimeSpan.FromSeconds(Math.Max(umbral, 180))) sesion.Bloqueada = true;
                if (sesion.Bloqueada || sesion.SegundoPlano is null) await db.SaveChangesAsync();
            }
            if (sesion.Bloqueada) ctx.HttpContext.Items[ClaveBloqueada] = true;
        }
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
            // varios intentos fallidos seguidos: si lo tienes activado, una foto de la cámara del PC
            if (ctx.Response.StatusCode != 200) await Mda.Api.Pc.Capturas.TrasFallo(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
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
