namespace Mda.Api.Data;

// Archivos pequeños guardados en la base: la foto de un ticket («ticket:15»), la foto de perfil («avatar»)…
// Van en su propia tabla para que listar apuntes no cargue nunca los bytes.
public class Adjunto
{
    public string Clave { get; set; } = "";
    public string Tipo { get; set; } = "image/jpeg";
    public byte[] Datos { get; set; } = [];
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;

    public const int Maximo = 6 * 1024 * 1024;
    private static readonly string[] Tipos = ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif"];

    // lee el cuerpo de la petición como imagen (el front ya la manda reducida)
    public static async Task<(byte[]? Datos, string Tipo, IResult? Error)> Leer(HttpRequest req, CancellationToken ct)
    {
        var tipo = (req.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (!Tipos.Contains(tipo)) return (null, tipo, Results.Problem("Tiene que ser una imagen (JPG, PNG, WebP o HEIC).", statusCode: 415));
        if (req.ContentLength > Maximo) return (null, tipo, Results.Problem("La imagen pasa de 6 MB.", statusCode: 413));
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await req.Body.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > Maximo) return (null, tipo, Results.Problem("La imagen pasa de 6 MB.", statusCode: 413));
        }
        if (ms.Length == 0) return (null, tipo, Results.BadRequest());
        var datos = ms.ToArray();
        if (!EsImagen(datos)) return (null, tipo, Results.Problem("El archivo no es una imagen válida.", statusCode: 415));
        return (datos, tipo, null);
    }

    // JPEG (FF D8 FF), PNG, WebP (RIFF…WEBP) o HEIC/HEIF («ftyp» en la posición 4)
    internal static bool EsImagen(byte[] b) =>
        (b.Length >= 4 && ((b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) || (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47))) ||
        (b.Length >= 12 && (
            (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') ||
            (b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p')));
}
