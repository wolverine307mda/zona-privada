using System.Text.Json;

namespace Mda.Api.Archivos;

public record Raiz(string Id, string Nombre);

// Las carpetas del equipo que se ven en «Documentos». En Docker cada una se monta en <Archivos:Base>/<id>
// (compose.yaml); una carpeta que no esté montada no aparece. Nada fuera de ellas es accesible.
public sealed class Carpetas
{
    public static readonly Raiz[] Todas =
    [
        new("documentos", "Documentos"),
        new("escritorio", "Escritorio"),
        new("descargas", "Descargas"),
        new("imagenes", "Imágenes"),
        new("musica", "Música"),
        new("videos", "Vídeos"),
    ];

    // lo borrado no se pierde: va a esta carpeta oculta dentro de cada raíz y se puede recuperar
    public const string Papelera = ".papelera-zona-privada";
    private const string Origen = ".origen";

    private static readonly char[] Prohibidos = ['<', '>', ':', '"', '|', '?', '*', '/', '\\'];

    public string Base { get; }

    public Carpetas(IConfiguration config) =>
        Base = Path.GetFullPath(config["Archivos:Base"] ?? "/carpetas");

    public IEnumerable<Raiz> Disponibles() => Todas.Where(r => Directory.Exists(RutaRaiz(r.Id)));

    public string RutaRaiz(string id) => Path.Combine(Base, id);

    // Convierte (raíz, ruta relativa) en una ruta real, sin salir nunca de la raíz ni entrar en la papelera
    // ni seguir enlaces. Devuelve null si la ruta no es válida.
    public string? Resolver(string? raiz, string? ruta)
    {
        if (raiz is null || !Todas.Any(r => r.Id == raiz)) return null;
        var baseRaiz = Path.GetFullPath(RutaRaiz(raiz));
        if (!Directory.Exists(baseRaiz)) return null;
        var partes = (ruta ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Any(p => p is "." or ".." || p.Equals(Papelera, StringComparison.OrdinalIgnoreCase))) return null;
        var completa = Path.GetFullPath(Path.Combine([baseRaiz, .. partes]));
        if (completa != baseRaiz && !completa.StartsWith(baseRaiz + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        // ningún tramo puede ser un enlace simbólico (podría apuntar fuera)
        var actual = baseRaiz;
        foreach (var p in partes)
        {
            actual = Path.Combine(actual, p);
            var info = new FileInfo(actual);
            if (info.Exists ? info.LinkTarget is not null : new DirectoryInfo(actual).LinkTarget is not null) return null;
        }
        return completa;
    }

    // Nombre de archivo o carpeta válido también en Windows (las carpetas son del equipo)
    public static bool NombreValido(string? n) =>
        !string.IsNullOrWhiteSpace(n) && n.Length <= 200 && n.Trim() == n && n is not ("." or "..")
        && n.IndexOfAny(Prohibidos) < 0 && !n.Any(char.IsControl) && !n.EndsWith('.')
        && !n.Equals(Papelera, StringComparison.OrdinalIgnoreCase);

    // «foto.jpg» -> «foto (2).jpg» si ya existe
    public static string Libre(string carpeta, string nombre)
    {
        var destino = Path.Combine(carpeta, nombre);
        var sinExt = Path.GetFileNameWithoutExtension(nombre);
        var ext = Path.GetExtension(nombre);
        for (var i = 2; File.Exists(destino) || Directory.Exists(destino); i++)
            destino = Path.Combine(carpeta, $"{sinExt} ({i}){ext}");
        return destino;
    }

    public static bool Oculto(FileSystemInfo f) =>
        f.Name.StartsWith('.') || f.LinkTarget is not null
        || f.Attributes.HasFlag(FileAttributes.Hidden) || f.Attributes.HasFlag(FileAttributes.System)
        || f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);

    // ---- Papelera ----

    public record EnPapelera(string Raiz, string Id, string Nombre, string Origen, bool Carpeta, long Tamano, DateTimeOffset Borrado);

    public void ALaPapelera(string raiz, string relativa, string real)
    {
        var id = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..26];
        var dir = Path.Combine(RutaRaiz(raiz), Papelera, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Origen), JsonSerializer.Serialize(new { ruta = relativa.Trim('/'), borrado = DateTimeOffset.UtcNow }));
        var destino = Path.Combine(dir, Path.GetFileName(real));
        if (Directory.Exists(real)) Directory.Move(real, destino); else File.Move(real, destino);
    }

    public IEnumerable<EnPapelera> Papeleras()
    {
        foreach (var r in Disponibles())
        {
            var p = Path.Combine(RutaRaiz(r.Id), Papelera);
            if (!Directory.Exists(p)) continue;
            foreach (var dir in new DirectoryInfo(p).EnumerateDirectories())
            {
                var item = dir.EnumerateFileSystemInfos().FirstOrDefault(f => f.Name != Origen);
                if (item is null) continue;
                var (ruta, borrado) = LeerOrigen(dir.FullName, item.Name, dir.CreationTimeUtc);
                yield return new EnPapelera(r.Id, dir.Name, item.Name, ruta, item is DirectoryInfo,
                    item is FileInfo fi ? fi.Length : 0, borrado);
            }
        }
    }

    private static (string, DateTimeOffset) LeerOrigen(string dir, string nombre, DateTime creado)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, Origen)));
            return (doc.RootElement.GetProperty("ruta").GetString() ?? nombre, doc.RootElement.GetProperty("borrado").GetDateTimeOffset());
        }
        catch { return (nombre, new DateTimeOffset(creado, TimeSpan.Zero)); }
    }

    public string? DirPapelera(string raiz, string id) =>
        Todas.Any(r => r.Id == raiz) && id.Length == 26 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            && Directory.Exists(Path.Combine(RutaRaiz(raiz), Papelera, id))
            ? Path.Combine(RutaRaiz(raiz), Papelera, id) : null;

    // vuelve a su sitio; si la carpeta de origen ya no existe, se crea; si el nombre está ocupado, «(2)»
    public string Restaurar(string raiz, string dir)
    {
        var item = new DirectoryInfo(dir).EnumerateFileSystemInfos().First(f => f.Name != Origen);
        var (ruta, _) = LeerOrigen(dir, item.Name, DateTime.UtcNow);
        var padreRel = Path.GetDirectoryName(ruta.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        var padre = Resolver(raiz, padreRel) ?? RutaRaiz(raiz);
        Directory.CreateDirectory(padre);
        var destino = Libre(padre, item.Name);
        if (item is DirectoryInfo) Directory.Move(item.FullName, destino); else File.Move(item.FullName, destino);
        Directory.Delete(dir, true);
        return Path.GetRelativePath(RutaRaiz(raiz), destino).Replace('\\', '/');
    }
}
