using Mda.Api.Archivos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Galeria;

// Una foto de «Imágenes» con lo que su archivo cuenta de dónde y cuándo se hizo. Se apunta aunque no lleve GPS (así no se vuelve a leer).
// Solo la ves tú, con el PIN: ni la posición ni la fecha salen nunca hacia la web pública.
public class FotoGeo
{
    public int Id { get; set; }
    public string Ruta { get; set; } = "";
    public long Tamano { get; set; }
    public long ModificadoTicks { get; set; }
    public string Hash { get; set; } = "";
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public DateTime? Captura { get; set; }
}

public record ResultadoIndice(int Nuevas, int Quitadas, int Pendientes, int Total);

// Recorre la carpeta «Imágenes» y apunta el GPS de cada foto (solo las nuevas o cambiadas, con tope por pasada para no bloquear el PC la primera vez).
public static class IndiceFotos
{
    public const int PorPasada = 300;

    public static (double? Lat, double? Lon, DateTime? Captura) LeerMetadatos(string ruta)
    {
        try
        {
            var meta = MetadataExtractor.ImageMetadataReader.ReadMetadata(ruta);
            var gps = meta.OfType<MetadataExtractor.Formats.Exif.GpsDirectory>().FirstOrDefault()?.GetGeoLocation();
            // «2026:07:14 19:42:10»: así guarda el EXIF la hora local de la cámara
            var texto = meta.OfType<MetadataExtractor.Formats.Exif.ExifSubIfdDirectory>().FirstOrDefault()?.GetObject(MetadataExtractor.Formats.Exif.ExifDirectoryBase.TagDateTimeOriginal)?.ToString();
            DateTime? fecha = DateTime.TryParseExact(texto, "yyyy:MM:dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var f) ? f : null;
            return gps is { IsZero: false } g ? (Math.Round(g.Latitude, 5), Math.Round(g.Longitude, 5), fecha) : (null, null, fecha);
        }
        catch (Exception e) when (e is MetadataExtractor.ImageProcessingException or IOException or UnauthorizedAccessException) { return (null, null, null); }
    }

    // Las imágenes soportadas de la carpeta y sus subcarpetas (sin ocultas ni papelera), con la ruta relativa de siempre (barras normales)
    public static IEnumerable<(string Ruta, FileInfo Archivo)> Imagenes(string raiz)
    {
        var pendientes = new Stack<DirectoryInfo>();
        pendientes.Push(new DirectoryInfo(raiz));
        while (pendientes.Count > 0)
        {
            var d = pendientes.Pop();
            IEnumerable<FileSystemInfo> hijos;
            try { hijos = d.EnumerateFileSystemInfos().ToList(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var h in hijos)
            {
                if (Carpetas.Oculto(h)) continue;
                if (h is DirectoryInfo sub) pendientes.Push(sub);
                else if (h is FileInfo f && Derivados.Soportada(f.Name)) yield return (Path.GetRelativePath(raiz, f.FullName).Replace('\\', '/'), f);
            }
        }
    }

    public static async Task<ResultadoIndice> Pasada(AppDbContext db, string raiz, int maximo = PorPasada, CancellationToken ct = default)
    {
        var enDisco = Imagenes(raiz).ToList();
        var guardadas = (await db.FotosGeo.ToListAsync(ct)).ToDictionary(x => x.Ruta, StringComparer.Ordinal);
        int nuevas = 0, pendientes = 0;
        foreach (var (ruta, f) in enDisco)
        {
            if (guardadas.TryGetValue(ruta, out var g) && g.Tamano == f.Length && g.ModificadoTicks == f.LastWriteTimeUtc.Ticks) continue;
            if (nuevas >= maximo) { pendientes++; continue; }
            var (lat, lon, captura) = LeerMetadatos(f.FullName);
            if (g is null) db.FotosGeo.Add(g = new FotoGeo { Ruta = ruta });
            (g.Tamano, g.ModificadoTicks, g.Hash, g.Lat, g.Lon, g.Captura) = (f.Length, f.LastWriteTimeUtc.Ticks, Derivados.HashDe(f), lat, lon, captura);
            nuevas++;
        }
        // las que ya no están en la carpeta (borradas o movidas), fuera; solo si la carpeta se pudo recorrer entera
        var presentes = enDisco.Select(x => x.Ruta).ToHashSet(StringComparer.Ordinal);
        var quitar = guardadas.Values.Where(x => !presentes.Contains(x.Ruta)).ToList();
        db.FotosGeo.RemoveRange(quitar);
        await db.SaveChangesAsync(ct);
        return new ResultadoIndice(nuevas, quitar.Count, pendientes, enDisco.Count);
    }
}

public sealed class IndicadorFotos(IServiceScopeFactory ambitos, Carpetas carpetas, ILogger<IndicadorFotos> log) : BackgroundService
{
    public ResultadoIndice? Ultimo { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(90), ct); } catch (OperationCanceledException) { return; }
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(30));
        do
        {
            try
            {
                var raiz = carpetas.RutaRaiz("imagenes");
                if (Directory.Exists(raiz))
                {
                    using var scope = ambitos.CreateScope();
                    // con trabajo pendiente (primera vez, miles de fotos) se sigue sin esperar la media hora
                    do { Ultimo = await IndiceFotos.Pasada(scope.ServiceProvider.GetRequiredService<AppDbContext>(), raiz, IndiceFotos.PorPasada, ct); await Task.Delay(TimeSpan.FromSeconds(2), ct); }
                    while (Ultimo.Pendientes > 0);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "No se pudo indexar el GPS de las fotos"); }
        } while (await t.WaitForNextTickAsync(ct));
    }
}

public static class MapaFotosEndpoints
{
    public static void MapMapaFotos(this IEndpointRouteBuilder app)
    {
        // Todas las fotos con posición, para pintarlas en el mapa (se agrupan en el navegador). Con el PIN, como el resto de «Imágenes».
        app.MapGet("/api/galeria/mapa", async (AppDbContext db, IndicadorFotos indice, CancellationToken ct) =>
        {
            var total = await db.FotosGeo.CountAsync(ct);
            var puntos = await db.FotosGeo.AsNoTracking().Where(x => x.Lat != null && x.Lon != null).OrderBy(x => x.Captura)
                .Select(x => new { r = x.Ruta, v = x.Hash, lat = x.Lat, lon = x.Lon, f = x.Captura }).ToListAsync(ct);
            return Results.Ok(new { total, conGps = puntos.Count, pendientes = indice.Ultimo?.Pendientes ?? 0, indexado = indice.Ultimo is not null, puntos });
        }).RequireAuthorization().AddEndpointFilter<PinFiltro>();
    }
}
