using System.Net;

namespace Mda.Api.Seguridad;

public static class Limites
{
    public const string Acceso = "acceso";
    // acciones que mandan avisos o tocan la contraseña: pocas por IP
    public const string Sensible = "sensible";
    // el agente de Windows manda una lectura cada 2 s
    public const string Agente = "agente";
    // lo que lee la web pública (la galería): muchas imágenes por visita, pero con tope por IP
    public const string Publico = "publico";
}

public static class IpCliente
{
    // La IP viene de la conexión, que UseForwardedHeaders ya ha reescrito con X-Forwarded-For del proxy (Tailscale Funnel).
    // CF-Connecting-IP solo se cree con Proxy:Cloudflare=true: sin Cloudflare delante, cualquiera puede inventarse esa cabecera.
    public static string De(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ctx.RequestServices.GetService<IConfiguration>()?.GetValue<bool>("Proxy:Cloudflare") == true
            && IPAddress.TryParse(ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault(), out var cf))
            ip = cf;
        if (ip is null) return "desconocida";
        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }
}

public static class CabecerasSeguras
{
    // Cabeceras de seguridad para la API y el front: sin iframes, sin scripts de terceros, sin adivinar tipos
    public static IApplicationBuilder UseCabecerasSeguras(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        // la geolocalización solo para la propia app («Mi ubicación» del tiempo)
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        // nadie más puede incrustar nuestros recursos, salvo lo que la web pública consulta a propósito
        var ruta = ctx.Request.Path;
        if (!ruta.StartsWithSegments("/api/salud") && !ruta.StartsWithSegments("/api/publico"))
            h["Cross-Origin-Resource-Policy"] = "same-origin";
        // HTTPS lo pone el proxy: solo se manda por conexiones seguras (en http://localhost no tiene sentido)
        if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=15552000";
        // nada de la API se guarda en cachés compartidas ni del navegador (los archivos fijan la suya después)
        if (ruta.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
        h["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://*.scdn.co https://*.spotifycdn.com; " +
            "font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        await next();
    });
}
