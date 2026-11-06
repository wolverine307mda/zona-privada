using System.Collections.Concurrent;
using System.Net.Http.Json;

// Cosas que pasan en el PC y que la zona privada te avisa en el móvil: una descarga terminada y Windows pidiendo reiniciar.
// El agente solo cuenta «qué» pasó; es la API la que decide si tienes el aviso activado.
record EventoPc(string Tipo, string Texto);

static class Vigias
{
    static readonly string[] Temporales = [".crdownload", ".part", ".tmp", ".download", ".opdownload", ".partial", ".aria2"];
    static readonly ConcurrentDictionary<string, DateTime> Vistos = new();

    public static async Task Enviar(HttpClient http, string tipo, string texto, CancellationToken ct)
    {
        try { await http.PostAsJsonAsync("/api/sistema/agente/eventos", new EventoPc(tipo, texto), ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { /* la API no está: se pierde este aviso, no pasa nada */ }
    }

    // La carpeta Descargas: un archivo «terminado» es el que no es temporal y ya no cambia de tamaño
    public static void Descargas(HttpClient http, CancellationToken ct) =>
        Vigilar(http, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "descarga", crear: false, ct);

    // La bandeja de entrada: una carpeta del PC («Zona privada\Bandeja» en tu carpeta de usuario, o la que pongas en AGENTE_BANDEJA) donde dejas cosas
    // desde cualquier sitio (el móvil con la nube, otro equipo…). Cada archivo nuevo sale en Mi PC y te llega un aviso.
    public static void Bandeja(HttpClient http, CancellationToken ct) =>
        Vigilar(http, Environment.GetEnvironmentVariable("AGENTE_BANDEJA") is { Length: > 0 } c && Path.IsPathRooted(c) ? c : RutaBandeja, "bandeja", crear: true, ct);

    public static readonly string RutaBandeja = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zona privada", "Bandeja");

    static void Vigilar(HttpClient http, string carpeta, string tipo, bool crear, CancellationToken ct)
    {
        try
        {
            if (crear) Directory.CreateDirectory(carpeta);
            if (!Directory.Exists(carpeta)) return;
            var w = new FileSystemWatcher(carpeta) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, EnableRaisingEvents = true };
            void Visto(string ruta) { _ = Terminada(http, ruta, tipo, ct); }
            w.Created += (_, e) => Visto(e.FullPath);
            w.Renamed += (_, e) => Visto(e.FullPath);
            ct.Register(w.Dispose);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { /* esa carpeta no se puede vigilar: el agente sigue con lo demás */ }
    }

    static async Task Terminada(HttpClient http, string ruta, string tipo, CancellationToken ct)
    {
        try
        {
            if (Temporales.Contains(Path.GetExtension(ruta), StringComparer.OrdinalIgnoreCase) || Directory.Exists(ruta)) return;
            // el mismo archivo da varios avisos (creado, renombrado…): uno cada 15 s
            var ahora = DateTime.UtcNow;
            if (Vistos.TryGetValue(ruta, out var antes) && ahora - antes < TimeSpan.FromSeconds(15)) return;
            Vistos[ruta] = ahora;
            foreach (var k in Vistos.Where(x => ahora - x.Value > TimeSpan.FromMinutes(5)).Select(x => x.Key).ToList()) Vistos.TryRemove(k, out _);

            long ultimo = -1;
            for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
            {
                await Task.Delay(1500, ct);
                if (!File.Exists(ruta)) return;
                var tamano = new FileInfo(ruta).Length;
                if (tamano == ultimo && tamano > 0) { await Enviar(http, tipo, $"{Path.GetFileName(ruta)} ({Humano(tamano)})", ct); return; }
                ultimo = tamano;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { /* el archivo se movió o se borró: no hay aviso */ }
    }

    static string Humano(long b) => b < 1024 * 1024 ? $"{b / 1024.0:0.#} KB" : b < 1024L * 1024 * 1024 ? $"{b / 1024.0 / 1024:0.#} MB" : $"{b / 1024.0 / 1024 / 1024:0.##} GB";
}
