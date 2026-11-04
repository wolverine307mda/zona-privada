using System.Diagnostics;
using System.Text;

namespace Mda.Lanzador;

// «ZonaPrivada.exe --instalar»: un único aviso de administrador y, a partir de ahí, arranca solo al iniciar sesión.
static class Instalador
{
    private static string Exe => Environment.ProcessPath!;
    private static string Usuario => System.Security.Principal.WindowsIdentity.GetCurrent().Name;

    public static bool TareaExiste() => Schtasks($"/Query /TN \"{TareaProgramada.Nombre}\"").Result.Codigo == 0;

    public static async Task<bool> CrearTarea(string? exe = null)
    {
        var xml = Path.Combine(Path.GetTempPath(), $"zona-privada-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(xml, TareaProgramada.Xml(exe ?? Exe, Usuario, TimeSpan.FromSeconds(20)), Encoding.Unicode);
            return (await Schtasks($"/Create /TN \"{TareaProgramada.Nombre}\" /XML \"{xml}\" /F")).Codigo == 0;
        }
        finally { try { File.Delete(xml); } catch (IOException) { } }
    }

    public static async Task<bool> QuitarTarea() => (await Schtasks($"/Delete /TN \"{TareaProgramada.Nombre}\" /F")).Codigo == 0;

    private static Task<(int Codigo, string Salida)> Schtasks(string args) =>
        Docker.Ejecutar("schtasks.exe", args, null, TimeSpan.FromSeconds(20));

    private static IEnumerable<string> Accesos() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Zona privada.lnk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Zona privada.lnk"),
    ];

    private static void CrearAccesos()
    {
        var shell = Type.GetTypeFromProgID("WScript.Shell");
        if (shell is null) return;
        dynamic ws = Activator.CreateInstance(shell)!;
        foreach (var ruta in Accesos())
        {
            dynamic a = ws.CreateShortcut(ruta);
            a.TargetPath = Exe; a.WorkingDirectory = Path.GetDirectoryName(Exe); a.IconLocation = Exe; a.Description = "Arranca la zona privada";
            a.Save();
        }
    }

    public static int Instalar()
    {
        var ok = CrearTarea().Result;
        try { CrearAccesos(); } catch (Exception e) when (e is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException) { /* sin accesos directos: no es grave */ }
        MessageBox.Show(ok
            ? "Listo. «Zona privada» arrancará sola al iniciar sesión en Windows (sin más avisos) y tienes accesos directos en el escritorio y en el menú Inicio."
            : "No se pudo crear la tarea de inicio. Abre el programa y usa «Iniciar con Windows» en el menú del icono.", "Zona privada", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        return ok ? 0 : 1;
    }

    // «ZonaPrivada.exe --instalar-escritorio» desde la carpeta que genera herramientas\empaquetar-nativo.ps1: copia todo a Archivos de programa
    // (de solo lectura salvo para administradores), crea la tarea de inicio apuntando al exe instalado y apunta la instalación en la configuración.
    // No toca Docker ni su tarea de inicio actual: se sustituye por la nueva (misma tarea, mismo nombre).
    public static int InstalarEscritorio()
    {
        var origen = Path.GetDirectoryName(Exe)!;
        var faltan = InstalacionNativa.Faltan(origen);
        if (faltan.Count > 0)
        {
            MessageBox.Show("Esta carpeta no es el paquete de la versión de escritorio. Falta: " + string.Join(", ", faltan) + "\n\nGenera el paquete con herramientas\\empaquetar-nativo.ps1 (docs/09-escritorio.md).", "Zona privada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        var destino = InstalacionNativa.Destino(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Directory.CreateDirectory(destino);
        var (c, salida) = Docker.Ejecutar("robocopy.exe", InstalacionNativa.Robocopy(origen, destino), null, TimeSpan.FromMinutes(10)).Result;
        if (!InstalacionNativa.RobocopyBien(c)) { MessageBox.Show("No se pudo copiar a " + destino + ": " + salida.Trim(), "Zona privada", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        var (ci, si) = Docker.Ejecutar("icacls.exe", InstalacionNativa.IcaclsPrograma(destino), null, TimeSpan.FromMinutes(3)).Result;
        if (ci != 0) { MessageBox.Show("No se pudieron fijar los permisos de " + destino + ": " + si.Trim(), "Zona privada", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        var exe = Path.Combine(destino, "ZonaPrivada.exe");
        var ok = CrearTarea(exe).Result;
        try
        {
            var datos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZonaPrivada");
            Directory.CreateDirectory(datos);
            var cfg = Path.Combine(datos, "config.json");
            var actual = File.Exists(cfg) ? System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfg)).RootElement : default;
            string Texto(string k, string defecto) => actual.ValueKind == System.Text.Json.JsonValueKind.Object && actual.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString()! : defecto;
            File.WriteAllText(cfg, System.Text.Json.JsonSerializer.Serialize(new { carpeta = Texto("carpeta", ""), perfil = Texto("perfil", "publico"), modo = Texto("modo", "docker"), ensayo = true, instalacion = destino, urlNativa = Texto("urlNativa", "") }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        MessageBox.Show(ok
            ? $"Instalada en {destino}. Arranca sola al iniciar sesión.\n\nSigue en el modo de siempre (Docker): para probar la versión de escritorio, activa «Modo escritorio» en el menú del icono. Empieza en ensayo: no avisa ni toca tus aparatos."
            : $"Instalada en {destino}, pero no se pudo crear la tarea de inicio. Usa «Iniciar con Windows» en el menú del icono.", "Zona privada", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        return ok ? 0 : 1;
    }

    public static int Desinstalar()
    {
        _ = QuitarTarea().Result;
        foreach (var r in Accesos()) { try { File.Delete(r); } catch (IOException) { } }
        MessageBox.Show("Hecho: ya no arrancará sola. Los contenedores siguen como estaban.", "Zona privada", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
}
