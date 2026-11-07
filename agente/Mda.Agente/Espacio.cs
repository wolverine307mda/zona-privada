using System.Diagnostics;

// «Qué ocupa»: las carpetas más grandes de una raíz que tú eliges (solo lectura). Se queda siempre en las unidades locales del equipo,
// no sigue enlaces ni uniones (para no salir de donde le dices ni contar dos veces) y se corta a los 60 segundos marcando el resultado como parcial.
record CarpetaEspacio(string Nombre, long Bytes);
record ResultadoEspacio(string Raiz, DateTimeOffset Hora, List<CarpetaEspacio> Carpetas, long TotalBytes, bool Parcial, int Segundos);

static class Espacio
{
    public const int Cuantas = 15;
    public static readonly TimeSpan Limite = TimeSpan.FromSeconds(60);

    // Pura: una ruta de unidad local («C:\», «D:\Juegos»). Nada de rutas de red, relativas, con «..» ni con caracteres raros. Devuelve la ruta limpia o null.
    public static string? RaizValida(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return null;
        var r = ruta.Trim().Replace('/', '\\');
        if (r.Length is < 2 or > 240 || !char.IsAsciiLetter(r[0]) || r[1] != ':') return null;
        if (r.Length > 2 && r[2] != '\\') return null;
        if (r.Contains("..") || r.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || r.IndexOfAny(['*', '?', '<', '>', '|', '"']) >= 0 || r[2..].Contains(':')) return null;
        r = char.ToUpperInvariant(r[0]) + r[1..];
        return r.Length == 2 ? r + "\\" : r.TrimEnd('\\') is { Length: 2 } sola ? sola + "\\" : r.TrimEnd('\\');
    }

    // Pura: las N más grandes, de mayor a menor (a igualdad, por nombre)
    public static List<CarpetaEspacio> Mayores(IEnumerable<CarpetaEspacio> todas, int cuantas = Cuantas) =>
        todas.Where(c => c.Bytes > 0).OrderByDescending(c => c.Bytes).ThenBy(c => c.Nombre, StringComparer.OrdinalIgnoreCase).Take(cuantas).ToList();

    // ¿Es un enlace o una unión? No se entra: puede llevar a otra unidad o dar vueltas
    static bool EsEnlace(FileSystemInfo f) => f.Attributes.HasFlag(FileAttributes.ReparsePoint);

    // Tamaño de una carpeta (todo lo que hay dentro). `corte` corta la cuenta cuando acaba el tiempo.
    static long Tamano(DirectoryInfo d, Stopwatch reloj, TimeSpan limite, ref bool parcial)
    {
        long total = 0;
        var pila = new Stack<DirectoryInfo>();
        pila.Push(d);
        while (pila.Count > 0)
        {
            if (reloj.Elapsed > limite) { parcial = true; break; }
            var actual = pila.Pop();
            try
            {
                foreach (var f in actual.EnumerateFiles())
                {
                    try { if (!EsEnlace(f)) total += f.Length; } catch (IOException) { /* un archivo que desaparece no importa */ }
                }
                foreach (var s in actual.EnumerateDirectories()) if (!EsEnlace(s)) pila.Push(s);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { /* sin permiso: se salta */ }
        }
        return total;
    }

    public static ResultadoEspacio Escanear(string raiz, TimeSpan? limite = null)
    {
        var tope = limite ?? Limite;
        var reloj = Stopwatch.StartNew();
        var parcial = false;
        var carpetas = new List<CarpetaEspacio>();
        long sueltos = 0;
        var dir = new DirectoryInfo(raiz);
        try
        {
            foreach (var f in dir.EnumerateFiles()) { try { if (!EsEnlace(f)) sueltos += f.Length; } catch (IOException) { } }
            foreach (var s in dir.EnumerateDirectories())
            {
                if (EsEnlace(s)) continue;
                if (reloj.Elapsed > tope) { parcial = true; break; }
                carpetas.Add(new CarpetaEspacio(s.Name, Tamano(s, reloj, tope, ref parcial)));
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { parcial = true; }
        if (sueltos > 0) carpetas.Add(new CarpetaEspacio("(archivos sueltos)", sueltos));
        var total = carpetas.Sum(c => c.Bytes);
        return new ResultadoEspacio(raiz, DateTimeOffset.UtcNow, Mayores(carpetas), total, parcial, (int)reloj.Elapsed.TotalSeconds);
    }
}
