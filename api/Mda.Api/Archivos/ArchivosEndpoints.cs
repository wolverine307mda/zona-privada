using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Net.Http.Headers;

namespace Mda.Api.Archivos;

public record Elemento(string Nombre, string Ruta, bool Carpeta, long Tamano, DateTimeOffset Modificado, string Tipo);
public record NuevaCarpeta(string Raiz, string? Ruta, string Nombre);
public record Renombrar(string Raiz, string Ruta, string Nombre);
public record Mover(string Raiz, string Ruta, string Destino);
public record Recuperar(string Raiz, string Id);

// API de «Documentos»: navegar por las carpetas del equipo, descargar, subir, crear carpetas, renombrar,
// mover y borrar (a una papelera recuperable). Todo con sesión iniciada.
public static class ArchivosEndpoints
{
    private static readonly FileExtensionContentTypeProvider Tipos = new();

    // lo que se puede abrir en el navegador sin descargar; el resto siempre se descarga
    private static readonly HashSet<string> SeVen = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp",
        ".mp3", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".wav", ".flac",
        ".mp4", ".m4v", ".webm", ".mov",
        ".pdf", ".txt", ".md", ".csv", ".log", ".json",
    };

    public const long MaxSubida = 4L * 1024 * 1024 * 1024; // 4 GB por subida

    public static string TipoDe(string nombre) => Path.GetExtension(nombre).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".avif" or ".bmp" or ".heic" or ".svg" or ".raw" or ".cr2" or ".nef" or ".arw" or ".dng" => "imagen",
        ".mp3" or ".m4a" or ".aac" or ".ogg" or ".oga" or ".opus" or ".wav" or ".flac" or ".wma" => "audio",
        ".mp4" or ".m4v" or ".webm" or ".mov" or ".mkv" or ".avi" or ".wmv" => "video",
        ".pdf" => "pdf",
        ".doc" or ".docx" or ".odt" or ".rtf" or ".txt" or ".md" or ".pages" => "texto",
        ".xls" or ".xlsx" or ".ods" or ".csv" or ".numbers" => "hoja",
        ".ppt" or ".pptx" or ".odp" or ".key" => "presentacion",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "comprimido",
        ".cs" or ".ts" or ".js" or ".py" or ".java" or ".html" or ".css" or ".json" or ".xml" or ".sql" or ".sh" or ".yml" or ".yaml" => "codigo",
        ".exe" or ".msi" or ".dmg" or ".apk" => "programa",
        _ => "otro",
    };

    private static Elemento ElementoDe(FileSystemInfo f, string raizReal) => new(
        f.Name, Path.GetRelativePath(raizReal, f.FullName).Replace('\\', '/'), f is DirectoryInfo,
        f is FileInfo fi ? fi.Length : 0, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero),
        f is DirectoryInfo ? "carpeta" : TipoDe(f.Name));

    private static IResult NoValida() => Results.Problem("Esa ruta no existe o no es válida.", statusCode: 404);
    private static IResult NombreMal() => Results.ValidationProblem(new Dictionary<string, string[]>
        { ["nombre"] = ["El nombre no es válido: sin / \\ : * ? \" < > | y sin acabar en punto."] });
    private static IResult Ejecutable() => Results.ValidationProblem(new Dictionary<string, string[]>
        { ["nombre"] = ["Por seguridad no se pueden subir ni crear programas o scripts (.exe, .bat, .ps1, .lnk…)."] });

    // dejar siempre 2 GB libres en el disco: una subida de 4 GB no debe llenar el C: del equipo
    private const long Reserva = 2L * 1024 * 1024 * 1024;

    public static void MapArchivos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/archivos").RequireAuthorization().AddEndpointFilter<Seguridad.PinFiltro>(); // pide el PIN de Documentos

        // contar elementos es recorrer cada carpeta (en Windows, por un montaje de Docker, es lento): se recuerda un minuto
        g.MapGet("/raices", (Carpetas c, IMemoryCache cache) => cache.GetOrCreate("archivos-raices", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return c.Disponibles().Select(r =>
            {
                var dir = new DirectoryInfo(c.RutaRaiz(r.Id));
                var n = dir.EnumerateFileSystemInfos().Count(f => !Carpetas.Oculto(f));
                return new { r.Id, r.Nombre, elementos = n };
            }).ToList();
        }));

        // ---- Contenido de una carpeta ----
        g.MapGet("/lista", (string raiz, string? ruta, Carpetas c) =>
        {
            var real = c.Resolver(raiz, ruta);
            if (real is null || !Directory.Exists(real)) return NoValida();
            var raizReal = Path.GetFullPath(c.RutaRaiz(raiz));
            var elementos = new DirectoryInfo(real).EnumerateFileSystemInfos()
                .Where(f => !Carpetas.Oculto(f))
                .Select(f => ElementoDe(f, raizReal))
                .OrderByDescending(e => e.Carpeta).ThenBy(e => e.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var rel = Path.GetRelativePath(raizReal, real).Replace('\\', '/');
            return Results.Ok(new { raiz, ruta = rel == "." ? "" : rel, elementos });
        });

        // ---- Buscar por nombre dentro de una raíz (hasta 200 resultados) ----
        g.MapGet("/buscar", (string raiz, string q, Carpetas c, CancellationToken ct) =>
        {
            var real = c.Resolver(raiz, "");
            if (real is null) return NoValida();
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2) return Results.Ok(Array.Empty<Elemento>());
            var opciones = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 12, MatchCasing = MatchCasing.CaseInsensitive, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
            var res = new DirectoryInfo(real).EnumerateFileSystemInfos($"*{q.Trim()}*", opciones)
                .Where(f => !f.FullName.Contains(Carpetas.Papelera, StringComparison.Ordinal) && !Carpetas.Oculto(f))
                .TakeWhile(_ => !ct.IsCancellationRequested)
                .Take(200)
                .Select(f => ElementoDe(f, real))
                .ToList();
            return Results.Ok(res);
        });

        // ---- Descargar o ver (con rangos: la música y los vídeos se pueden adelantar) ----
        g.MapGet("/archivo", async (string raiz, string ruta, bool? ver, Carpetas c, HttpContext ctx, Data.AppDbContext db, Telegram.Bot bot, Avisos.IAvisos avisos) =>
        {
            var real = c.Resolver(raiz, ruta);
            if (real is null || !File.Exists(real)) return NoValida();
            await Seguridad.Senuelos.Comprobar(ctx, db, bot, avisos, c, raiz, real);
            var nombre = Path.GetFileName(real);
            var ext = Path.GetExtension(nombre).ToLowerInvariant();
            var enLinea = ver == true && SeVen.Contains(ext);
            var tipo = "application/octet-stream";
            if (enLinea)
            {
                // el texto se muestra como texto, nunca como HTML; y lo abierto en línea no puede ejecutar nada
                tipo = ext is ".txt" or ".md" or ".csv" or ".log" or ".json" ? "text/plain; charset=utf-8"
                    : Tipos.TryGetContentType(nombre, out var t) ? t : tipo;
                if (tipo != "application/pdf") ctx.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'; media-src 'self'";
            }
            ctx.Response.Headers.CacheControl = "private, max-age=300";
            return Results.File(real, tipo, enLinea ? null : nombre,
                new DateTimeOffset(File.GetLastWriteTimeUtc(real), TimeSpan.Zero), enableRangeProcessing: true);
        });

        // ---- Subir uno o varios archivos (multipart, se escriben a disco sin pasar por memoria) ----
        g.MapPost("/subir", async (string raiz, string? ruta, Carpetas c, HttpRequest req, ILogger<Carpetas> log, CancellationToken ct) =>
        {
            var real = c.Resolver(raiz, ruta);
            if (real is null || !Directory.Exists(real)) return NoValida();
            if (req.ContentLength is { } tam && Carpetas.EspacioLibre(real) is { } libre && tam > libre - Reserva)
                return Results.Problem("No hay espacio suficiente en el disco del equipo.", statusCode: StatusCodes.Status507InsufficientStorage);
            var limite = req.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limite is { IsReadOnly: false }) limite.MaxRequestBodySize = MaxSubida;
            var frontera = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(req.ContentType ?? "").Boundary).Value;
            if (string.IsNullOrEmpty(frontera)) return Results.BadRequest();

            var lector = new MultipartReader(frontera, req.Body) { BodyLengthLimit = MaxSubida };
            var subidos = new List<Elemento>();
            var raizReal = Path.GetFullPath(c.RutaRaiz(raiz));
            while (await lector.ReadNextSectionAsync(ct) is { } seccion)
            {
                if (!ContentDispositionHeaderValue.TryParse(seccion.ContentDisposition, out var cd) || !cd.IsFileDisposition()) continue;
                var nombre = Path.GetFileName((cd.FileNameStar.Value ?? cd.FileName.Value ?? "").Trim('"'));
                if (!Carpetas.NombreValido(nombre)) return NombreMal();
                if (c.ExtensionBloqueada(nombre)) return Ejecutable();
                // se escribe con un nombre oculto y solo al terminar toma el suyo: nunca queda un archivo a medias
                var temporal = Path.Combine(real, $".{Guid.NewGuid():N}.subiendo");
                string destino;
                try
                {
                    await using (var f = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, true))
                        await seccion.Body.CopyToAsync(f, ct);
                    destino = Carpetas.Libre(real, nombre);
                    File.Move(temporal, destino);
                }
                catch
                {
                    File.Delete(temporal);
                    throw;
                }
                subidos.Add(ElementoDe(new FileInfo(destino), raizReal));
                log.LogInformation("Subido {Archivo} a {Raiz}", subidos[^1].Ruta, raiz);
            }
            return Results.Ok(subidos);
        }).WithMetadata(new RequestSizeLimitAttribute(MaxSubida));

        // ---- Carpeta nueva ----
        g.MapPost("/carpeta", (NuevaCarpeta e, Carpetas c) =>
        {
            var padre = c.Resolver(e.Raiz, e.Ruta);
            if (padre is null || !Directory.Exists(padre)) return NoValida();
            if (!Carpetas.NombreValido(e.Nombre)) return NombreMal();
            var destino = Path.Combine(padre, e.Nombre);
            if (Directory.Exists(destino) || File.Exists(destino))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Ya hay algo con ese nombre aquí."] });
            Directory.CreateDirectory(destino);
            return Results.Ok(ElementoDe(new DirectoryInfo(destino), Path.GetFullPath(c.RutaRaiz(e.Raiz))));
        });

        // ---- Renombrar ----
        g.MapPost("/renombrar", (Renombrar e, Carpetas c) =>
        {
            var real = c.Resolver(e.Raiz, e.Ruta);
            if (real is null || real == Path.GetFullPath(c.RutaRaiz(e.Raiz)) || (!File.Exists(real) && !Directory.Exists(real))) return NoValida();
            if (!Carpetas.NombreValido(e.Nombre)) return NombreMal();
            if (File.Exists(real) && c.ExtensionBloqueada(e.Nombre, Path.GetFileName(real))) return Ejecutable();
            var destino = Path.Combine(Path.GetDirectoryName(real)!, e.Nombre);
            if (destino == real) return Results.Ok();
            if (File.Exists(destino) || Directory.Exists(destino))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Ya hay algo con ese nombre aquí."] });
            if (Directory.Exists(real)) Directory.Move(real, destino); else File.Move(real, destino);
            return Results.Ok();
        });

        // ---- Mover a otra carpeta de la misma raíz ----
        g.MapPost("/mover", (Mover e, Carpetas c) =>
        {
            var real = c.Resolver(e.Raiz, e.Ruta);
            var destinoDir = c.Resolver(e.Raiz, e.Destino);
            if (real is null || destinoDir is null || real == Path.GetFullPath(c.RutaRaiz(e.Raiz)) || !Directory.Exists(destinoDir)) return NoValida();
            if (!File.Exists(real) && !Directory.Exists(real)) return NoValida();
            // una carpeta no puede ir dentro de sí misma
            if (Directory.Exists(real) && (destinoDir == real || destinoDir.StartsWith(real + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["destino"] = ["No se puede mover una carpeta dentro de sí misma."] });
            var destino = Carpetas.Libre(destinoDir, Path.GetFileName(real));
            if (Directory.Exists(real)) Directory.Move(real, destino); else File.Move(real, destino);
            return Results.Ok();
        });

        // ---- Borrar: a la papelera ----
        g.MapDelete("/", (string raiz, string ruta, Carpetas c, ILogger<Carpetas> log) =>
        {
            var real = c.Resolver(raiz, ruta);
            if (real is null || real == Path.GetFullPath(c.RutaRaiz(raiz)) || (!File.Exists(real) && !Directory.Exists(real))) return NoValida();
            c.ALaPapelera(raiz, ruta, real);
            log.LogInformation("A la papelera: {Ruta} de {Raiz}", ruta, raiz);
            return Results.NoContent();
        });

        // ---- Papelera ----
        g.MapGet("/papelera", (Carpetas c) => c.Papeleras().OrderByDescending(p => p.Borrado));

        g.MapPost("/papelera/recuperar", (Recuperar e, Carpetas c) =>
        {
            var dir = c.DirPapelera(e.Raiz, e.Id);
            return dir is null ? NoValida() : Results.Ok(new { ruta = c.Restaurar(e.Raiz, dir) });
        });

        // borrar para siempre: uno (raiz + id) o toda la papelera
        g.MapDelete("/papelera", (string? raiz, string? id, Carpetas c, ILogger<Carpetas> log) =>
        {
            if (raiz is not null && id is not null)
            {
                var dir = c.DirPapelera(raiz, id);
                if (dir is null) return NoValida();
                Directory.Delete(dir, true);
            }
            else
            {
                foreach (var r in c.Disponibles())
                {
                    var p = Path.Combine(c.RutaRaiz(r.Id), Carpetas.Papelera);
                    if (Directory.Exists(p)) Directory.Delete(p, true);
                }
            }
            log.LogInformation("Papelera vaciada ({Raiz} {Id})", raiz ?? "todas", id ?? "todo");
            return Results.NoContent();
        });
    }
}
