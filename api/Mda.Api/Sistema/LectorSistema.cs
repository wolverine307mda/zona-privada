using System.Globalization;
using System.Runtime.InteropServices;

namespace Mda.Api.Sistema;

// Una lectura del equipo. La CPU es el % de uso desde la lectura anterior.
public record MetricasSistema(
    double Cpu,
    long MemoriaUsada,
    long MemoriaTotal,
    long DiscoUsado,
    long DiscoTotal,
    double EncendidoSegundos,
    int Nucleos,
    string Equipo,
    string SistemaOperativo,
    DateTimeOffset Hora);

public interface ILectorSistema
{
    MetricasSistema Leer();
}

public static class LectorSistema
{
    public static ILectorSistema ParaEsteEquipo() =>
        OperatingSystem.IsLinux() ? new LectorLinux()
        : OperatingSystem.IsWindows() ? new LectorWindows()
        : new LectorBasico();

    // Disco de la raíz del sistema: usado y total en bytes
    internal static (long Usado, long Total) Disco(string raiz)
    {
        try
        {
            var d = new DriveInfo(raiz);
            return (d.TotalSize - d.AvailableFreeSpace, d.TotalSize);
        }
        catch (IOException) { return (0, 0); }
        catch (UnauthorizedAccessException) { return (0, 0); }
    }

    // % de CPU entre dos lecturas de tiempos acumulados (inactivo y total)
    public static double Porcentaje(ulong inactivoAntes, ulong totalAntes, ulong inactivo, ulong total)
    {
        var dTotal = total - totalAntes;
        if (totalAntes == 0 || dTotal == 0) return 0;
        var dInactivo = inactivo - inactivoAntes;
        return Math.Round(Math.Clamp(100.0 * (dTotal - dInactivo) / dTotal, 0, 100), 1);
    }
}

// Linux (el contenedor de Docker): /proc/stat, /proc/meminfo y /proc/uptime
public sealed class LectorLinux : ILectorSistema
{
    private ulong _inactivo, _total;

    public MetricasSistema Leer()
    {
        var (inactivo, total) = ParsearStat(File.ReadAllText("/proc/stat"));
        var cpu = LectorSistema.Porcentaje(_inactivo, _total, inactivo, total);
        (_inactivo, _total) = (inactivo, total);

        var (memTotal, memLibre) = ParsearMeminfo(File.ReadAllText("/proc/meminfo"));
        var encendido = double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0], CultureInfo.InvariantCulture);
        var (discoUsado, discoTotal) = LectorSistema.Disco("/");

        return new MetricasSistema(cpu, memTotal - memLibre, memTotal, discoUsado, discoTotal, encendido,
            Environment.ProcessorCount, Environment.MachineName, RuntimeInformation.OSDescription, DateTimeOffset.UtcNow);
    }

    // Primera línea de /proc/stat: "cpu user nice system idle iowait irq softirq steal …" (en ticks)
    public static (ulong Inactivo, ulong Total) ParsearStat(string stat)
    {
        var linea = stat.Split('\n')[0];
        var v = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8)
            .Select(x => ulong.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        ulong total = 0;
        foreach (var x in v) total += x;
        var inactivo = v[3] + (v.Length > 4 ? v[4] : 0); // idle + iowait
        return (inactivo, total);
    }

    // MemTotal y MemAvailable de /proc/meminfo, en bytes
    public static (long Total, long Disponible) ParsearMeminfo(string meminfo)
    {
        long Valor(string clave)
        {
            var linea = meminfo.Split('\n').FirstOrDefault(l => l.StartsWith(clave + ":", StringComparison.Ordinal));
            if (linea is null) return 0;
            var kb = long.Parse(linea.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
            return kb * 1024;
        }
        return (Valor("MemTotal"), Valor("MemAvailable"));
    }
}

// Windows (desarrollo en el equipo de casa): GetSystemTimes y GlobalMemoryStatusEx
public sealed partial class LectorWindows : ILectorSistema
{
    private ulong _inactivo, _total;

    public MetricasSistema Leer()
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        ulong inactivo = idle, total = kernel + user; // el tiempo de kernel ya incluye el inactivo
        var cpu = LectorSistema.Porcentaje(_inactivo, _total, inactivo, total);
        (_inactivo, _total) = (inactivo, total);

        var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref mem);
        var raiz = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var (discoUsado, discoTotal) = LectorSistema.Disco(raiz);

        return new MetricasSistema(cpu, (long)(mem.TotalPhys - mem.AvailPhys), (long)mem.TotalPhys, discoUsado, discoTotal,
            Environment.TickCount64 / 1000.0, Environment.ProcessorCount, Environment.MachineName,
            RuntimeInformation.OSDescription, DateTimeOffset.UtcNow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

// Otros sistemas: lo que .NET sabe sin ayuda (sin CPU)
public sealed class LectorBasico : ILectorSistema
{
    public MetricasSistema Leer()
    {
        var gc = GC.GetGCMemoryInfo();
        var (discoUsado, discoTotal) = LectorSistema.Disco("/");
        return new MetricasSistema(0, gc.MemoryLoadBytes, gc.TotalAvailableMemoryBytes, discoUsado, discoTotal,
            Environment.TickCount64 / 1000.0, Environment.ProcessorCount, Environment.MachineName,
            RuntimeInformation.OSDescription, DateTimeOffset.UtcNow);
    }
}
