// La bandeja en los dos sentidos, la parte que no toca Windows: nombres seguros, ejecutables bloqueados y nombres que no pisan lo que ya hay.
static class BandejaLogica
{
    public const long Maximo = 100L * 1024 * 1024;

    static readonly HashSet<string> Ejecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".jse", ".wsf", ".wsh", ".msi", ".msp",
        ".lnk", ".hta", ".jar", ".reg", ".dll", ".cpl", ".scf", ".url", ".pif", ".msc", ".application", ".gadget",
    };

    static readonly HashSet<string> Reservados = new(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    public static bool Bloqueada(string nombre) => Ejecutables.Contains(Path.GetExtension(nombre));

    // Solo el nombre (nunca una ruta), sin caracteres que Windows no admite, sin puntos ni espacios al final, sin nombres reservados y de 120 letras como mucho. null si no queda nada.
    public static string? NombreSeguro(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        var n = nombre.Replace('\\', '/');
        n = n[(n.LastIndexOf('/') + 1)..];
        var invalidos = Path.GetInvalidFileNameChars();
        n = new string(n.Select(c => invalidos.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ').TrimStart('.');
        if (n.Length == 0) return null;
        if (Reservados.Contains(Path.GetFileNameWithoutExtension(n))) n = "_" + n;
        if (n.Length > 120) { var ext = Path.GetExtension(n); n = n[..Math.Max(1, 120 - ext.Length)] + (ext.Length < 20 ? ext : ""); }
        return n;
    }

    // «foto.jpg» → «foto (2).jpg» si ya existe
    public static string Unico(string nombre, Func<string, bool> existe)
    {
        if (!existe(nombre)) return nombre;
        var baseN = Path.GetFileNameWithoutExtension(nombre); var ext = Path.GetExtension(nombre);
        for (var i = 2; i < 1000; i++) { var c = $"{baseN} ({i}){ext}"; if (!existe(c)) return c; }
        return $"{baseN} ({Guid.NewGuid():N}){ext}";
    }
}
