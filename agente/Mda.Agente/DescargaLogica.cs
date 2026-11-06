using System.Net;
using System.Net.Sockets;

// Lo que decide qué se puede descargar al PC y con qué nombre. Sin nada de Windows para poder probarlo.
// Una descarga es la orden más delicada después de apagar: trae un archivo de fuera al equipo. Por eso solo http(s) sin usuario ni clave,
// nunca hacia la red local ni hacia el propio PC, nunca programas (.exe, .bat, .ps1…) y siempre a una carpeta fija.
static class DescargaLogica
{
    public const int MaxUrl = 300;
    public const long MaxBytes = 4L * 1024 * 1024 * 1024;            // 4 GB por archivo
    public const long ReservaLibre = 2L * 1024 * 1024 * 1024;        // el disco no se llena por una descarga
    public const int MaxRedirecciones = 5;

    // los mismos que bloquea Documentos al subir archivos (Archivos/Carpetas.cs)
    static readonly HashSet<string> Ejecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".jse", ".wsf", ".wsh", ".msi", ".msp",
        ".lnk", ".hta", ".jar", ".reg", ".dll", ".cpl", ".scf", ".url", ".pif", ".msc", ".application", ".gadget",
    };

    static readonly HashSet<string> ReservadosWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // La dirección normalizada, o null si no vale
    public static Uri? Direccion(string? parametro)
    {
        var p = (parametro ?? "").Trim();
        if (p.Length is 0 or > MaxUrl) return null;
        if (p.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '<' or '>' or '^' or '`' or '\\')) return null;
        if (!Uri.TryCreate(p, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || string.IsNullOrEmpty(u.Host) || u.UserInfo.Length > 0) return null;
        return u;
    }

    // Direcciones que no son de Internet: el propio PC, la red de casa, enlace local, Tailscale (100.64/10) y multidifusión
    public static bool EsLocal(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b6 = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b6[0] & 0xFE) == 0xFC;   // fc00::/7 son las privadas
        }
        var b = ip.GetAddressBytes();
        return b[0] is 0 or 10 or 127 or >= 224
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    public static bool Bloqueado(string nombre) => Ejecutables.Contains(Path.GetExtension(nombre));

    // Un nombre de archivo que Windows acepte y que no se salga de la carpeta, a partir del que diga el servidor o el final de la dirección
    public static string Nombre(string? propuesto, Uri url)
    {
        var n = propuesto;
        if (string.IsNullOrWhiteSpace(n))
        {
            try { n = Uri.UnescapeDataString(Path.GetFileName(url.AbsolutePath)); } catch (UriFormatException) { n = ""; }
        }
        n = Path.GetFileName((n ?? "").Replace('\\', '/'));   // sin carpetas de por medio
        var limpio = new string(n.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (limpio.Length == 0 || limpio.All(c => c == '_')) limpio = "descarga";
        var baseNombre = Path.GetFileNameWithoutExtension(limpio);
        if (ReservadosWindows.Contains(baseNombre)) limpio = "_" + limpio;
        if (limpio.Length > 120)
        {
            var ext = Path.GetExtension(limpio);
            limpio = limpio[..Math.Max(1, 120 - ext.Length)] + ext;
        }
        return limpio;
    }

    // Un nombre que no pise nada de la carpeta: «a.pdf», «a (1).pdf», «a (2).pdf»…
    public static string Libre(string carpeta, string nombre)
    {
        if (!File.Exists(Path.Combine(carpeta, nombre))) return nombre;
        var baseNombre = Path.GetFileNameWithoutExtension(nombre);
        var ext = Path.GetExtension(nombre);
        for (var i = 1; i < 1000; i++)
        {
            var candidato = $"{baseNombre} ({i}){ext}";
            if (!File.Exists(Path.Combine(carpeta, candidato))) return candidato;
        }
        return $"{baseNombre} ({Guid.NewGuid():N}){ext}";
    }
}
