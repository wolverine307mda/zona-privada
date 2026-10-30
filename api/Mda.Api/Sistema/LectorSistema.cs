using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Mda.Api.Sistema;

// Un disco del equipo: nombre (C:\, /), usado y total en bytes
public record Disco(string Nombre, long Usado, long Total);

// Una lectura del equipo. La CPU y la red son la media desde la lectura anterior.
// Origen: "equipo" si viene del agente de Windows (datos reales del PC), "contenedor" si la mide la propia API.
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
    DateTimeOffset Hora,
    string CpuModelo = "",
    IReadOnlyList<Disco>? Discos = null,
    double RedBajada = 0,
    double RedSubida = 0,
    string Origen = "contenedor");

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

    // Discos fijos del equipo (en Windows, C:, D:…). En un contenedor solo cuenta la raíz.
    internal static IReadOnlyList<Disco> Discos(bool todos)
    {
        if (!todos)
        {
            var (u, t) = Disco("/");
            return t > 0 ? [new Disco("/", u, t)] : [];
        }
        var lista = new List<Disco>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 0)
                    lista.Add(new Disco(d.Name, d.TotalSize - d.AvailableFreeSpace, d.TotalSize));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return lista;
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

// Velocidad de red (bytes/s) sumando las interfaces activas que no son de bucle local
internal sealed class MedidorRed
{
    private long _bajada, _subida;
    private long _marca;

    public (double Bajada, double Subida) Leer()
    {
        long bajada = 0, subida = 0;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var s = n.GetIPStatistics();
                bajada += s.BytesReceived;
                subida += s.BytesSent;
            }
            catch (NetworkInformationException) { }
            catch (PlatformNotSupportedException) { }
        }
        var ahora = Environment.TickCount64;
        var segundos = (ahora - _marca) / 1000.0;
        var r = _marca == 0 || segundos <= 0
            ? (0d, 0d)
            : (Math.Max(0, (bajada - _bajada) / segundos), Math.Max(0, (subida - _subida) / segundos));
        (_bajada, _subida, _marca) = (bajada, subida, ahora);
        return r;
    }
}

// Linux (el contenedor de Docker): /proc/stat, /proc/meminfo, /proc/uptime y /proc/cpuinfo
public sealed class LectorLinux : ILectorSistema
{
    private ulong _inactivo, _total;
    private readonly MedidorRed _red = new();
    private readonly string _modelo = ModeloCpu();

    public MetricasSistema Leer()
    {
        var (inactivo, total) = ParsearStat(File.ReadAllText("/proc/stat"));
        var cpu = LectorSistema.Porcentaje(_inactivo, _total, inactivo, total);
        (_inactivo, _total) = (inactivo, total);

        var (memTotal, memLibre) = ParsearMeminfo(File.ReadAllText("/proc/meminfo"));
        var encendido = double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0], CultureInfo.InvariantCulture);
        var discos = LectorSistema.Discos(todos: false);
        var (bajada, subida) = _red.Leer();

        return new MetricasSistema(cpu, memTotal - memLibre, memTotal,
            discos.Sum(d => d.Usado), discos.Sum(d => d.Total), encendido,
            Environment.ProcessorCount, Environment.MachineName, RuntimeInformation.OSDescription, DateTimeOffset.UtcNow,
            _modelo, discos, bajada, subida, "contenedor");
    }

    private static string ModeloCpu()
    {
        try
        {
            var linea = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal));
            return linea?.Split(':', 2)[1].Trim() ?? "";
        }
        catch (IOException) { return ""; }
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

// Windows (el PC de verdad, desde el agente o con la API fuera de Docker): GetSystemTimes y GlobalMemoryStatusEx
public sealed class LectorWindows : ILectorSistema
{
    private ulong _inactivo, _total;
    private readonly MedidorRed _red = new();
    private readonly string _modelo = ModeloCpu();

    public MetricasSistema Leer()
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        ulong inactivo = idle, total = kernel + user; // el tiempo de kernel ya incluye el inactivo
        var cpu = LectorSistema.Porcentaje(_inactivo, _total, inactivo, total);
        (_inactivo, _total) = (inactivo, total);

        var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref mem);
        var discos = LectorSistema.Discos(todos: true);
        var sistema = discos.FirstOrDefault(d => string.Equals(d.Nombre, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
                      ?? discos.FirstOrDefault();
        var (bajada, subida) = _red.Leer();

        return new MetricasSistema(cpu, (long)(mem.TotalPhys - mem.AvailPhys), (long)mem.TotalPhys,
            sistema?.Usado ?? 0, sistema?.Total ?? 0, Environment.TickCount64 / 1000.0,
            Environment.ProcessorCount, Environment.MachineName, RuntimeInformation.OSDescription, DateTimeOffset.UtcNow,
            _modelo, discos, bajada, subida, "equipo");
    }

    private static string ModeloCpu()
    {
        if (!OperatingSystem.IsWindows()) return "";
        using var clave = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (clave?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
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
        var discos = LectorSistema.Discos(todos: false);
        return new MetricasSistema(0, gc.MemoryLoadBytes, gc.TotalAvailableMemoryBytes,
            discos.Sum(d => d.Usado), discos.Sum(d => d.Total), Environment.TickCount64 / 1000.0,
            Environment.ProcessorCount, Environment.MachineName, RuntimeInformation.OSDescription, DateTimeOffset.UtcNow,
            Discos: discos);
    }
}
