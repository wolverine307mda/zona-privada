namespace Mda.Api.Seguridad;

public static class Limites
{
    public const string Acceso = "acceso";
}

public static class IpCliente
{
    // Detrás de Cloudflare la IP real del visitante llega en CF-Connecting-IP
    public static string De(HttpContext ctx) =>
        ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
        ?? ctx.Connection.RemoteIpAddress?.ToString()
        ?? "desconocida";
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
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://*.scdn.co https://*.spotifycdn.com; " +
            "font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        await next();
    });
}
