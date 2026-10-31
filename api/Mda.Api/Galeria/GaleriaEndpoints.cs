using System.Security.Cryptography;
using Mda.Api.Archivos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Galeria;

public record FotoEntrada(string Ruta, string? Titulo, string? Descripcion, string? Alt, string? Categoria);
public record FotoCambio(string? Titulo, string? Descripcion, string? Alt, string? Categoria, bool Publicada,
    bool EnGlobo = false, double? Lat = null, double? Lon = null, string? Lugar = null);
public record ExclusionEntrada(double? Lat, double? Lon, double RadioKm);
public record OrdenEntrada(string[] Ids);
public record EstadoEntrada(bool Activa);

// Galería del portfolio: lo gestionas con sesión en /api/galeria y la web pública solo lee /api/publico/galeria.
// Nada es público hasta que activas el interruptor general y marcas cada foto.
public static class GaleriaEndpoints
{
    public const string Activa = "galeria-activa";
    public const string WebPublica = "web-publica";
    private const int MaxFotos = 200;

    public const string ClaveExclusion = "globo-excluir";
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    // distancia en km entre dos puntos
    internal static double Km(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371, Rad = Math.PI / 180;
        var dLat = (lat2 - lat1) * Rad; var dLon = (lon2 - lon1) * Rad;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Rad) * Math.Cos(lat2 * Rad) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }

    // lo que sabe el público de dónde está una foto: redondeado a 0,1° (~10 km), y nada si cae en la zona de tu casa
    internal static object? LugarPublico(FotoPublica f, ExclusionEntrada? x)
    {
        if (!f.EnGlobo || f.Lat is not { } lat || f.Lon is not { } lon) return null;
        if (x is { Lat: { } xl, Lon: { } xo, RadioKm: > 0 } && Km(lat, lon, xl, xo) <= x.RadioKm) return null;
        return new { lat = Math.Round(lat, 1), lon = Math.Round(lon, 1), nombre = f.Lugar };
    }

    private static async Task<ExclusionEntrada?> Exclusion(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveExclusion, ct))?.Valor;
        try { return v is null ? null : System.Text.Json.JsonSerializer.Deserialize<ExclusionEntrada>(v, Json); } catch (System.Text.Json.JsonException) { return null; }
    }

    private static IResult Invalido(string campo, string msg) => Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [msg] });

    public static async Task<bool> EstaActiva(AppDbContext db, CancellationToken ct) =>
        await db.Ajustes.AsNoTracking().AnyAsync(a => a.Clave == Activa && a.Valor == "si", ct);

    private static string Recortar(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();

    public static void MapGaleria(this IEndpointRouteBuilder app)
    {
        // ---- gestión (con sesión) ----
        var g = app.MapGroup("/api/galeria").RequireAuthorization();

        g.MapGet("/estado", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(new { activa = await EstaActiva(db, ct), publicadas = await db.FotosPublicas.CountAsync(f => f.Publicada, ct), total = await db.FotosPublicas.CountAsync(ct) }));

        g.MapPut("/estado", async (EstadoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            var a = await db.Ajustes.FindAsync([Activa], ct);
            if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = Activa });
            a.Valor = e.Activa ? "si" : "no";
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // las fotos de una carpeta de «Imágenes» con su estado de publicación
        g.MapGet("/carpeta", async (string? ruta, Carpetas c, AppDbContext db, CancellationToken ct) =>
        {
            var real = c.Resolver("imagenes", ruta);
            if (real is null || !Directory.Exists(real)) return Results.NotFound();
            var raizReal = Path.GetFullPath(c.RutaRaiz("imagenes"));
            var publicadas = await db.FotosPublicas.AsNoTracking().ToListAsync(ct);
            var rel = Path.GetRelativePath(raizReal, real).Replace('\\', '/');
            var elementos = new DirectoryInfo(real).EnumerateFileSystemInfos()
                .Where(f => !Carpetas.Oculto(f) && (f is DirectoryInfo || Derivados.Soportada(f.Name) || ArchivosEndpoints.TipoDe(f.Name) == "imagen"))
                .Select(f =>
                {
                    var r = Path.GetRelativePath(raizReal, f.FullName).Replace('\\', '/');
                    var foto = f is FileInfo ? publicadas.FirstOrDefault(p => p.Ruta == r) : null;
                    return new
                    {
                        nombre = f.Name, ruta = r, carpeta = f is DirectoryInfo, tamano = f is FileInfo fi ? fi.Length : 0L,
                        soportada = f is FileInfo && Derivados.Soportada(f.Name),
                        foto = foto is null ? null : new { foto.Id, foto.Publicada },
                    };
                })
                .OrderByDescending(x => x.carpeta).ThenBy(x => x.nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
            return Results.Ok(new { ruta = rel == "." ? "" : rel, elementos });
        });

        g.MapGet("/fotos", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.FotosPublicas.AsNoTracking().OrderBy(f => f.Orden).ThenBy(f => f.Creada)
                .Select(f => new { f.Id, f.Ruta, f.Titulo, f.Descripcion, f.Alt, f.Categoria, f.Orden, f.Ancho, f.Alto, f.Tono, f.Hash, f.Publicada, f.EnGlobo, f.Lat, f.Lon, f.Lugar }).ToListAsync(ct)));

        // añade una foto a la galería (o la devuelve si ya estaba)
        g.MapPost("/fotos", async (FotoEntrada e, Carpetas c, Derivados d, AppDbContext db, CancellationToken ct) =>
        {
            var real = c.Resolver("imagenes", e.Ruta);
            if (real is null || !File.Exists(real)) return Results.NotFound();
            var ruta = Path.GetRelativePath(Path.GetFullPath(c.RutaRaiz("imagenes")), real).Replace('\\', '/');
            if (!Derivados.Soportada(real)) return Invalido("ruta", "Ese formato no se puede publicar (usa JPG, PNG o WebP; los HEIC hay que convertirlos).");
            if (await db.FotosPublicas.CountAsync(ct) >= MaxFotos) return Invalido("ruta", $"Máximo {MaxFotos} fotos en la galería.");
            var f = await db.FotosPublicas.FirstOrDefaultAsync(x => x.Ruta == ruta, ct);
            var info = new FileInfo(real);
            if (f is null)
            {
                var medidas = d.Medir(real);
                if (medidas is null) return Invalido("ruta", "No se pudo leer la imagen.");
                f = new FotoPublica
                {
                    Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(), Ruta = ruta,
                    Ancho = medidas.Value.Ancho, Alto = medidas.Value.Alto, Tono = medidas.Value.Tono, Hash = Derivados.HashDe(info),
                    Orden = (await db.FotosPublicas.MaxAsync(x => (int?)x.Orden, ct) ?? 0) + 1,
                };
                db.FotosPublicas.Add(f);
            }
            f.Titulo = Recortar(e.Titulo is { Length: > 0 } ? e.Titulo : Path.GetFileNameWithoutExtension(real), 80);
            f.Descripcion = Recortar(e.Descripcion ?? f.Descripcion, 400);
            f.Alt = Recortar(e.Alt ?? f.Alt, 200);
            f.Categoria = Recortar(e.Categoria ?? f.Categoria, 30).ToLowerInvariant();
            f.Publicada = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok(f);
        });

        g.MapPut("/fotos/{id}", async (string id, FotoCambio e, Carpetas c, Derivados d, AppDbContext db, CancellationToken ct) =>
        {
            var f = await db.FotosPublicas.FindAsync([id], ct);
            if (f is null) return Results.NotFound();
            f.Titulo = Recortar(e.Titulo, 80);
            f.Descripcion = Recortar(e.Descripcion, 400);
            f.Alt = Recortar(e.Alt, 200);
            f.Categoria = Recortar(e.Categoria, 30).ToLowerInvariant();
            f.Publicada = e.Publicada;
            if (e.Lat is { } la && (la is < -90 or > 90) || e.Lon is { } lo && (lo is < -180 or > 180)) return Invalido("lat", "Esa posición no es válida.");
            f.EnGlobo = e.EnGlobo && e.Lat is not null && e.Lon is not null;
            f.Lat = e.Lat; f.Lon = e.Lon;
            f.Lugar = Recortar(e.Lugar, 80);
            // si el archivo cambió en disco, se vuelve a medir y las URLs nuevas llevan otro hash
            if (d.RutaReal(f) is { } real && Derivados.HashDe(new FileInfo(real)) is var h && h != f.Hash && d.Medir(real) is { } m)
            {
                d.Limpiar(f.Id);
                (f.Ancho, f.Alto, f.Tono, f.Hash) = (m.Ancho, m.Alto, m.Tono, h);
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(f);
        });

        // el GPS que lleva el archivo original (solo lo ves tú, para elegir si lo usas)
        g.MapGet("/fotos/{id}/gps", async (string id, Derivados d, AppDbContext db, CancellationToken ct) =>
        {
            var f = await db.FotosPublicas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null || d.RutaReal(f) is not { } real) return Results.NotFound();
            return Derivados.LeerGps(real) is { } p ? Results.Ok(new { lat = p.Lat, lon = p.Lon }) : Results.NoContent();
        });

        // la zona de tu casa: las fotos que caigan dentro nunca salen en el globo
        g.MapGet("/globo", async (AppDbContext db, CancellationToken ct) => Results.Ok(await Exclusion(db, ct) ?? new ExclusionEntrada(null, null, 0)));
        g.MapPut("/globo", async (ExclusionEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            var a = await db.Ajustes.FindAsync([ClaveExclusion], ct);
            if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = ClaveExclusion });
            a.Valor = System.Text.Json.JsonSerializer.Serialize(e with { RadioKm = Math.Clamp(e.RadioKm, 0, 200) }, Json);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("/fotos/{id}", async (string id, Derivados d, AppDbContext db, CancellationToken ct) =>
        {
            var f = await db.FotosPublicas.FindAsync([id], ct);
            if (f is null) return Results.NotFound();
            db.FotosPublicas.Remove(f);
            await db.SaveChangesAsync(ct);
            d.Limpiar(id);
            return Results.NoContent();
        });

        g.MapPut("/orden", async (OrdenEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            var todas = await db.FotosPublicas.ToListAsync(ct);
            for (var i = 0; i < e.Ids.Length; i++) if (todas.FirstOrDefault(f => f.Id == e.Ids[i]) is { } f) f.Orden = i + 1;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // la vista previa dentro de la privada (con sesión), también de las no publicadas
        g.MapGet("/fotos/{id}/{tam}", async (string id, string tam, Derivados d, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var f = await db.FotosPublicas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null || await d.Obtener(f, tam, ct) is not { } ruta) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.File(ruta, "image/webp");
        });

        // ---- lo que ve la web pública: solo lectura, sin sesión, solo si está activa y solo lo publicado ----
        var p = app.MapGroup("/api/publico/galeria").AllowAnonymous().RequireCors(WebPublica).RequireRateLimiting(Limites.Publico);

        p.MapGet("/", async (AppDbContext db, Derivados d, HttpContext ctx, CancellationToken ct) =>
        {
            if (!await EstaActiva(db, ct)) return Results.NotFound();
            var lista = await db.FotosPublicas.AsNoTracking().Where(f => f.Publicada).OrderBy(f => f.Orden).ThenBy(f => f.Creada).ToListAsync(ct);
            var exclusion = await Exclusion(db, ct);
            // si el archivo ya no está en el equipo, no se anuncia
            var fotos = lista.Where(f => d.RutaReal(f) is not null).Select(f => new
            {
                id = f.Id, titulo = f.Titulo, descripcion = f.Descripcion, alt = f.Alt.Length > 0 ? f.Alt : f.Titulo, categoria = f.Categoria,
                ancho = f.Ancho, alto = f.Alto, tono = f.Tono, lugar = LugarPublico(f, exclusion),
                mini = $"/api/publico/galeria/{f.Id}/mini?v={f.Hash}", grande = $"/api/publico/galeria/{f.Id}/grande?v={f.Hash}",
            }).ToList();
            ctx.Response.Headers.CacheControl = "public, max-age=60";
            return Results.Ok(new { fotos });
        });

        p.MapGet("/{id}/{tam}", async (string id, string tam, AppDbContext db, Derivados d, HttpContext ctx, CancellationToken ct) =>
        {
            if (!await EstaActiva(db, ct)) return Results.NotFound();
            var f = await db.FotosPublicas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Publicada, ct);
            if (f is null || await d.Obtener(f, tam, ct) is not { } ruta) return Results.NotFound();
            // la URL lleva el hash del archivo: cambia si cambia la foto, así que se puede guardar «para siempre»
            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            ctx.Response.Headers.ETag = $"\"{f.Hash}-{tam}\"";
            return Results.File(ruta, "image/webp");
        });
    }
}
