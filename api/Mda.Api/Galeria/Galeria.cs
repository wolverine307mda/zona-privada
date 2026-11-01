using System.Security.Cryptography;
using System.Text;
using Mda.Api.Archivos;
using SkiaSharp;

namespace Mda.Api.Galeria;

// Una foto de la carpeta «Imágenes» que se enseña en el portfolio. Solo existe lo que tú publicas: el resto de la carpeta nunca sale.
// El id es aleatorio (no se deduce de la ruta ni del nombre del archivo) y la ruta real nunca viaja a la web pública.
public class FotoPublica
{
    public string Id { get; set; } = "";
    public string Raiz { get; set; } = "imagenes";
    public string Ruta { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Alt { get; set; } = "";
    public string Categoria { get; set; } = "";
    public int Orden { get; set; }
    public int Ancho { get; set; }
    public int Alto { get; set; }
    // color medio de la foto: se pinta mientras carga la imagen
    public string Tono { get; set; } = "#808080";
    // cambia si cambia el archivo: va en la URL y permite cachearla «para siempre»
    public string Hash { get; set; } = "";
    public bool Publicada { get; set; } = true;
    // Globo del portfolio: solo si lo marcas. La posición que se guarda es la que tú pones (o la del archivo, que solo ves tú);
    // al público siempre le llega redondeada a ~10 km, y nunca si cae dentro de la zona excluida alrededor de tu casa.
    public bool EnGlobo { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public string Lugar { get; set; } = "";
    public DateTimeOffset Creada { get; set; } = DateTimeOffset.UtcNow;
}

// Las imágenes que se sirven al público: siempre un derivado recomprimido en WebP (nunca el original), y sin EXIF:
// el codificador de Skia no copia metadatos, así que el GPS y la cámara se pierden.
public sealed class Derivados(IConfiguration config, Carpetas carpetas, ILogger<Derivados> log)
{
    public static readonly Dictionary<string, int> Tamanos = new() { ["mini"] = 360, ["media"] = 720, ["grande"] = 1600 };
    private static readonly Dictionary<string, int> Calidad = new() { ["mini"] = 70, ["media"] = 75, ["grande"] = 80 };
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _lqip = new();
    // lo que Skia sabe leer en todas las plataformas (HEIC/AVIF no: hay que pasarlos a JPG o WebP)
    public static readonly string[] Extensiones = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"];

    private readonly SemaphoreSlim _cupo = new(2); // decodificar una foto de 24 Mpx ocupa ~100 MB: no más de dos a la vez
    private string Directorio => config["Galeria:Derivados"] is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "mda-derivados");

    public static bool Soportada(string nombre) => Extensiones.Contains(Path.GetExtension(nombre), StringComparer.OrdinalIgnoreCase);

    public static string HashDe(FileInfo f) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{f.Length}:{f.LastWriteTimeUtc.Ticks}")))[..12].ToLowerInvariant();

    public string? RutaReal(FotoPublica f) => carpetas.Resolver(f.Raiz, f.Ruta) is { } r && File.Exists(r) ? r : null;

    // (ancho, alto, tono) de la foto ya orientada; null si no se puede leer
    public (int Ancho, int Alto, string Tono)? Medir(string ruta)
    {
        try
        {
            using var codec = SKCodec.Create(ruta);
            if (codec is null) return null;
            var i = codec.Info;
            var gira = (int)codec.EncodedOrigin >= (int)SKEncodedOrigin.LeftTop;
            using var mini = Reducir(ruta, 32);
            return (gira ? i.Height : i.Width, gira ? i.Width : i.Height, mini is null ? "#808080" : Medio(mini));
        }
        catch (Exception e) { log.LogWarning(e, "No se pudo leer {Ruta}", Path.GetFileName(ruta)); return null; }
    }

    // El derivado en disco (se genera la primera vez y se guarda); null si no se puede
    public async Task<string?> Obtener(FotoPublica f, string tam, CancellationToken ct)
    {
        if (!Tamanos.TryGetValue(tam, out var lado) || RutaReal(f) is not { } origen) return null;
        var destino = Path.Combine(Directorio, $"{f.Id}-{f.Hash}-{tam}.webp");
        if (File.Exists(destino)) return destino;
        await _cupo.WaitAsync(ct);
        try
        {
            if (File.Exists(destino)) return destino;
            Directory.CreateDirectory(Directorio);
            using var reducida = Reducir(origen, lado);
            if (reducida is null) return null;
            using var imagen = SKImage.FromBitmap(reducida);
            using var datos = imagen.Encode(SKEncodedImageFormat.Webp, Calidad[tam]);
            var temporal = destino + $".{Guid.NewGuid():N}.tmp";
            await using (var s = File.Create(temporal)) datos.SaveTo(s);
            File.Move(temporal, destino, true);
            return destino;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "No se pudo generar el derivado {Tam} de {Id}", tam, f.Id);
            return null;
        }
        finally { _cupo.Release(); }
    }

    // ¿ya está generado el derivado? (no lo genera)
    public bool Existe(FotoPublica f, string tam) => File.Exists(Path.Combine(Directorio, $"{f.Id}-{f.Hash}-{tam}.webp"));

    // Miniatura borrosa de 16 px en data-URI (~300 B): la web pinta la rejilla al instante. Sale de la «mini» ya generada; null si aún no existe.
    public string? Lqip(FotoPublica f)
    {
        var clave = $"{f.Id}-{f.Hash}";
        if (_lqip.TryGetValue(clave, out var hecho)) return hecho;
        var mini = Path.Combine(Directorio, $"{f.Id}-{f.Hash}-mini.webp");
        if (!File.Exists(mini)) return null;
        try
        {
            using var origen = SKBitmap.Decode(mini);
            if (origen is null) return null;
            var w = 16; var h = Math.Max(1, (int)Math.Round(16.0 * origen.Height / origen.Width));
            using var pequena = origen.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            using var imagen = SKImage.FromBitmap(pequena);
            using var datos = imagen.Encode(SKEncodedImageFormat.Webp, 40);
            return _lqip[clave] = "data:image/webp;base64," + Convert.ToBase64String(datos.ToArray());
        }
        catch (Exception e) { log.LogWarning(e, "No se pudo generar el marcador de {Id}", f.Id); return null; }
    }

    // borra los derivados de una foto (al despublicarla o si cambia el archivo)
    public void Limpiar(string id)
    {
        try { foreach (var a in Directory.EnumerateFiles(Directorio, $"{id}-*")) File.Delete(a); }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException or UnauthorizedAccessException) { /* ya no estaban */ }
    }

    // El GPS del archivo original, si lo tiene. SOLO se enseña a ti en la privada; nunca sale hacia la web.
    public static (double Lat, double Lon)? LeerGps(string ruta)
    {
        try
        {
            var g = MetadataExtractor.ImageMetadataReader.ReadMetadata(ruta).OfType<MetadataExtractor.Formats.Exif.GpsDirectory>().FirstOrDefault();
            return g?.GetGeoLocation() is { } l && !l.IsZero ? (l.Latitude, l.Longitude) : null;
        }
        catch (Exception e) when (e is MetadataExtractor.ImageProcessingException or IOException) { return null; }
    }

    // Decodifica, aplica la orientación y reduce para que el lado largo no pase de «lado» (sin agrandar)
    private static SKBitmap? Reducir(string ruta, int lado)
    {
        using var codec = SKCodec.Create(ruta);
        if (codec is null) return null;
        using var original = SKBitmap.Decode(codec);
        if (original is null) return null;
        using var orientada = Orientar(original, codec.EncodedOrigin);
        var mayor = Math.Max(orientada.Width, orientada.Height);
        if (mayor <= lado) return orientada.Copy();
        var k = (double)lado / mayor;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(orientada.Width * k)), Math.Max(1, (int)Math.Round(orientada.Height * k)));
        return orientada.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    private static string Medio(SKBitmap b)
    {
        long r = 0, g = 0, a = 0;
        var n = 0;
        for (var y = 0; y < b.Height; y++)
            for (var x = 0; x < b.Width; x++) { var p = b.GetPixel(x, y); r += p.Red; g += p.Green; a += p.Blue; n++; }
        return n == 0 ? "#808080" : $"#{r / n:x2}{g / n:x2}{a / n:x2}";
    }

    // EXIF dice cómo hay que girar el dibujo para verlo derecho: sin esto, las fotos de móvil salen tumbadas
    private static SKBitmap Orientar(SKBitmap origen, SKEncodedOrigin o)
    {
        if (o is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return origen.Copy();
        float w = origen.Width, h = origen.Height;
        var traspone = (int)o >= (int)SKEncodedOrigin.LeftTop;
        var m = o switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };
        var destino = new SKBitmap(traspone ? origen.Height : origen.Width, traspone ? origen.Width : origen.Height);
        using var c = new SKCanvas(destino);
        c.SetMatrix(m);
        using var imagen = SKImage.FromBitmap(origen);
        c.DrawImage(imagen, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return destino;
    }
}
