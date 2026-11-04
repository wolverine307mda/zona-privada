using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace Mda.Api.Seguridad;

// Caché de los archivos del front: los que llevan hash en el nombre (main-TQ6T2ERZ.js) no cambian nunca, así que se guardan un año;
// el index.html y el service worker se revisan siempre (si no, un despliegue nuevo no llegaría); el resto, un día.
public static partial class CacheFront
{
    [GeneratedRegex(@"-[A-Z0-9]{8}\.[a-z0-9]+$")]
    private static partial Regex ConHash();

    public static void Poner(StaticFileResponseContext ctx)
    {
        var nombre = ctx.File.Name;
        ctx.Context.Response.Headers.CacheControl = nombre is "index.html" or "sw.js" or "ngsw.json" or "manifest.webmanifest"
            ? "no-cache"
            : ConHash().IsMatch(nombre) ? "public, max-age=31536000, immutable" : "public, max-age=86400";
    }
}
