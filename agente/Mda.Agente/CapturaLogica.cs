using System.Text.RegularExpressions;

// La parte de las fotos bajo demanda que no toca el equipo (se puede probar en cualquier sitio).
static class CapturaLogica
{
    public static readonly string[] Origenes = ["pantalla", "camara"];
    public static readonly string[] Motivos = ["manual", "panico", "intruso"];
    public const int LadoMaximo = 1280;

    // «pantalla», «camara:intruso»… igual que lo valida la API; el agente lo vuelve a comprobar
    public static bool Valido(string? parametro, out string origen, out string motivo)
    {
        var partes = (parametro ?? "").Split(':');
        (origen, motivo) = (partes[0], partes.Length > 1 ? partes[1] : "manual");
        return partes.Length <= 2 && Origenes.Contains(origen) && Motivos.Contains(motivo);
    }

    // Las cámaras que lista ffmpeg con «-list_devices true -f dshow -i dummy» (por la salida de error):
    //   [dshow @ 000001] "Integrated Camera" (video)
    //   [dshow @ 000001] "Microphone (Realtek)" (audio)
    public static List<string> CamarasDe(string salida) =>
        Regex.Matches(salida, "\"(?<n>[^\"\\r\\n]+)\"\\s+\\(video\\)").Select(m => m.Groups["n"].Value).Where(n => !n.StartsWith("@device", StringComparison.Ordinal)).Distinct().ToList();

    // Tamaño final manteniendo la proporción, sin agrandar
    public static (int Ancho, int Alto) Reducir(int ancho, int alto)
    {
        if (ancho < 1 || alto < 1) return (1, 1);
        var escala = Math.Min(1.0, (double)LadoMaximo / Math.Max(ancho, alto));
        return (Math.Max(1, (int)Math.Round(ancho * escala)), Math.Max(1, (int)Math.Round(alto * escala)));
    }
}
