using System.Diagnostics;

namespace Mda.Lanzador;

// Lanza docker.exe y Docker Desktop sin ventanas. Todo con límite de tiempo: nada se queda colgado.
static class Docker
{
    private static string? _exe;

    public static string Exe => _exe ??= Entorno.PrimeroQueExiste(
        [
            EnPath("docker.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DockerDesktop", "resources", "bin", "docker.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "resources", "bin", "docker.exe"),
        ], File.Exists) ?? "docker.exe";

    public static string? DockerDesktop => Entorno.PrimeroQueExiste(
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DockerDesktop", "Docker Desktop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe"),
        ], File.Exists);

    // ¿está Docker Desktop instalado? (docker.exe o su aplicación)
    public static bool Instalado => File.Exists(Exe) || DockerDesktop is not null;

    // instala Docker Desktop con winget (viene con Windows 11); tarda varios minutos y puede pedir reiniciar
    public static async Task<(bool Ok, string Salida)> InstalarConWinget()
    {
        var (codigo, salida) = await Ejecutar("winget.exe", "install -e --id Docker.DockerDesktop --accept-package-agreements --accept-source-agreements", null, TimeSpan.FromMinutes(25));
        _exe = null; // que vuelva a buscar docker.exe
        return (codigo == 0, salida);
    }

    private static string? EnPath(string nombre) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => { try { return Path.Combine(d.Trim(), nombre); } catch (ArgumentException) { return null; } })
            .FirstOrDefault(p => p is not null && File.Exists(p));

    public static async Task<(int Codigo, string Salida)> Ejecutar(string exe, string argumentos, string? carpeta, TimeSpan limite)
    {
        var info = new ProcessStartInfo(exe, argumentos)
        {
            WorkingDirectory = carpeta ?? Environment.CurrentDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        try
        {
            using var p = Process.Start(info)!;
            using var cts = new CancellationTokenSource(limite);
            var salida = p.StandardOutput.ReadToEndAsync(cts.Token);
            var error = p.StandardError.ReadToEndAsync(cts.Token);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } return (-1, "tiempo agotado"); }
            return (p.ExitCode, (await salida) + (await error));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return (-1, e.Message); }
    }

    public static Task<(int Codigo, string Salida)> Cmd(string argumentos, string? carpeta = null, int segundos = 30) =>
        Ejecutar(Exe, argumentos, carpeta, TimeSpan.FromSeconds(segundos));

    // ¿responde el motor? («docker info» falla si Docker Desktop no ha terminado de arrancar)
    public static async Task<bool> Responde() => (await Cmd("info --format {{.ServerVersion}}", segundos: 15)).Codigo == 0;

    // Docker Desktop se abre «desde el explorador»: así no hereda los permisos de administrador de este programa
    public static void AbrirDockerDesktop()
    {
        if (Process.GetProcessesByName("Docker Desktop").Length > 0 || DockerDesktop is not { } exe) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = true });
    }
}
