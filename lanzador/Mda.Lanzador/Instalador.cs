using System.Diagnostics;
using System.Text;

namespace Mda.Lanzador;

// «ZonaPrivada.exe --instalar»: un único aviso de administrador y, a partir de ahí, arranca solo al iniciar sesión.
static class Instalador
{
    private static string Exe => Environment.ProcessPath!;
    private static string Usuario => System.Security.Principal.WindowsIdentity.GetCurrent().Name;

    public static bool TareaExiste() => Schtasks($"/Query /TN \"{TareaProgramada.Nombre}\"").Result.Codigo == 0;

    public static async Task<bool> CrearTarea()
    {
        var xml = Path.Combine(Path.GetTempPath(), $"zona-privada-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(xml, TareaProgramada.Xml(Exe, Usuario, TimeSpan.FromSeconds(20)), Encoding.Unicode);
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

    public static int Desinstalar()
    {
        _ = QuitarTarea().Result;
        foreach (var r in Accesos()) { try { File.Delete(r); } catch (IOException) { } }
        MessageBox.Show("Hecho: ya no arrancará sola. Los contenedores siguen como estaban.", "Zona privada", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
}
