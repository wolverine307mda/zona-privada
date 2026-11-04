// «Abrir en el PC»: una dirección web (http o https) o una de unas pocas aplicaciones de una lista cerrada. Nada de rutas ni de otros esquemas
// (file:, javascript:, ms-settings: ni parecidos) y nada pasa por una consola: se abre con el sistema, sin interpretar el texto. Sin nada de Windows para poder probarlo.
static class Abrir
{
    public const int MaxLongitud = 300;

    // nombre en la app → programa de Windows
    public static readonly Dictionary<string, string> Aplicaciones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calculadora"] = "calc.exe", ["bloc"] = "notepad.exe", ["explorador"] = "explorer.exe",
    };

    // («url», la dirección normalizada) o («app», el programa); null si no vale
    public static (string Tipo, string Valor)? Destino(string? parametro)
    {
        var p = (parametro ?? "").Trim();
        if (p.Length is 0 or > MaxLongitud) return null;
        if (Aplicaciones.TryGetValue(p, out var exe)) return ("app", exe);
        if (p.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '<' or '>' or '^' or '`' || c == (char)92)) return null; // 92 es la barra invertida
        if (!Uri.TryCreate(p, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || string.IsNullOrEmpty(u.Host) || u.UserInfo.Length > 0) return null;
        return ("url", u.AbsoluteUri);
    }
}
