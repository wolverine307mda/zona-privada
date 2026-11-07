using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

// Aplicaciones del PC: el catálogo que tú eliges (Get-StartApps), abrirlas por su identificador y cerrar las que tienen ventana.
// Reglas: solo se abre un AppID que sigue estando en Get-StartApps (nunca una ruta que llegue en la orden), nada pasa por una consola
// y un proceso se cierra solo si coincide su número Y su hora de inicio (otro programa podría reutilizar el número) y no es de los protegidos.
record AppInstalada(string Nombre, string AppId);
record ProcesoVentana(int Pid, long InicioUnix, string Nombre, string Titulo, double MemoriaMb, bool Protegido);

static partial class AppsPc
{
    public const int Maximo = 300;

    // Protegidos: cerrar uno de estos deja el equipo, la zona privada o la red inservibles (se comparan sin mayúsculas y sin «.exe»)
    static readonly string[] Protegidos = ["explorer", "winlogon", "wininit", "csrss", "services", "lsass", "svchost", "dwm", "system", "smss", "fontdrvhost", "sihost", "taskhostw",
        "ctfmon", "searchhost", "startmenuexperiencehost", "shellexperiencehost", "runtimebroker", "audiodg", "spoolsv", "mda.api", "mda.agente", "mda.lanzador", "zonaprivada", "postgres", "docker", "dockerd", "com.docker.backend"];

    public static bool EsProtegido(string nombre)
    {
        var n = nombre.Trim().ToLowerInvariant();
        if (n.EndsWith(".exe")) n = n[..^4];
        return Protegidos.Contains(n) || n.StartsWith("tailscale") || n.StartsWith("postgres") || n.StartsWith("mda.");
    }

    [GeneratedRegex(@"^[\p{L}\p{N}._!{}\-\\ ()]+$")]
    private static partial Regex CaracteresAppId();

    // Pura: un identificador de aplicación del menú Inicio («Microsoft.WindowsCalculator_8wekyb3d8bbwe!App» o «{GUID}\Google\Chrome\Application\chrome.exe»)
    public static bool AppIdValido(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 260 && CaracteresAppId().IsMatch(id) && !id.Contains("..") && !id.StartsWith('\\') && !id.Contains(':');

    // Pura: «pid:inicioUnix:amable|forzar»
    public static (int Pid, long Inicio, bool Forzar)? Cierre(string? parametro)
    {
        var p = (parametro ?? "").Split(':');
        if (p.Length != 3 || !int.TryParse(p[0], out var pid) || pid <= 4 || !long.TryParse(p[1], out var inicio) || inicio <= 0 || p[2] is not ("amable" or "forzar")) return null;
        return (pid, inicio, p[2] == "forzar");
    }

    // el mismo proceso: la hora de inicio coincide con 2 segundos de margen (Windows la da con décimas)
    public static bool MismoProceso(long inicioPedido, long inicioReal) => Math.Abs(inicioPedido - inicioReal) <= 2;

    // Lo que pone Get-StartApps en JSON: un objeto si hay uno solo o una lista; se descartan los que no tienen un AppID válido
    public static List<AppInstalada> InterpretarStartApps(string json)
    {
        var l = new List<AppInstalada>();
        if (string.IsNullOrWhiteSpace(json)) return l;
        try
        {
            using var d = JsonDocument.Parse(json);
            var items = d.RootElement.ValueKind == JsonValueKind.Array ? d.RootElement.EnumerateArray().ToList() : [d.RootElement];
            foreach (var x in items)
            {
                var nombre = x.TryGetProperty("Name", out var n) ? n.GetString() : null;
                var id = x.TryGetProperty("AppID", out var a) ? a.GetString() : null;
                if (!string.IsNullOrWhiteSpace(nombre) && AppIdValido(id)) l.Add(new AppInstalada(nombre.Trim()[..Math.Min(nombre.Trim().Length, 80)], id!));
            }
        }
        catch (JsonException) { /* sin lista */ }
        return l.DistinctBy(x => x.AppId).OrderBy(x => x.Nombre, StringComparer.CurrentCultureIgnoreCase).Take(Maximo).ToList();
    }

    // ---- lo que toca Windows ----
    public static async Task<List<AppInstalada>> Instaladas(CancellationToken ct)
    {
        var inicio = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress" }) inicio.ArgumentList.Add(a);
        inicio.StandardOutputEncoding = System.Text.Encoding.UTF8;
        using var p = Process.Start(inicio) ?? throw new InvalidOperationException("no arrancó PowerShell");
        var salida = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return InterpretarStartApps(salida);
    }

    public static async Task<(bool Ok, string Mensaje)> Abrir(string appId, CancellationToken ct)
    {
        if (!AppIdValido(appId)) return (false, "identificador de aplicación no válido");
        // solo si Windows sigue teniéndola en el menú Inicio (la lista de la zona privada puede estar vieja)
        if ((await Instaladas(ct)).All(a => !string.Equals(a.AppId, appId, StringComparison.OrdinalIgnoreCase))) return (false, "esa aplicación ya no está en el menú Inicio");
        var inicio = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        inicio.ArgumentList.Add($"shell:AppsFolder\\{appId}");
        Process.Start(inicio);
        return (true, "abierta");
    }

    public static List<ProcesoVentana> ConVentana()
    {
        var l = new List<ProcesoVentana>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                l.Add(new ProcesoVentana(p.Id, new DateTimeOffset(p.StartTime).ToUnixTimeSeconds(), p.ProcessName, p.MainWindowTitle[..Math.Min(p.MainWindowTitle.Length, 120)],
                    Math.Round(p.WorkingSet64 / 1048576.0, 0), EsProtegido(p.ProcessName)));
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { /* acaba de cerrarse o no se deja mirar */ }
            finally { p.Dispose(); }
        }
        return l.OrderByDescending(x => x.MemoriaMb).Take(60).ToList();
    }

    // Cierre amable (CloseMainWindow) y, solo si se pidió «forzar», Kill. Siempre comprueba número + hora de inicio y que no sea protegido.
    public static async Task<(bool Ok, string Mensaje)> Cerrar(int pid, long inicio, bool forzar, CancellationToken ct)
    {
        Process p;
        try { p = Process.GetProcessById(pid); } catch (ArgumentException) { return (false, "ese programa ya no está abierto"); }
        using (p)
        {
            try
            {
                if (!MismoProceso(inicio, new DateTimeOffset(p.StartTime).ToUnixTimeSeconds())) return (false, "el número ya es de otro programa: refresca la lista");
                if (EsProtegido(p.ProcessName)) return (false, $"{p.ProcessName} es un programa protegido: no se cierra desde aquí");
                var nombre = p.ProcessName;
                if (!forzar)
                {
                    if (!p.CloseMainWindow()) return (false, $"{nombre} no tiene ventana que cerrar");
                    for (var i = 0; i < 100; i++) { if (p.HasExited) return (true, $"{nombre} cerrado"); await Task.Delay(100, ct); }   // 10 s de cortesía
                    return (false, $"{nombre} sigue abierto (quizá pregunta si guardar): usa «Forzar»");
                }
                p.Kill();
                return (true, $"{nombre} forzado a cerrar");
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return (false, "no se pudo: " + e.Message); }
        }
    }
}
