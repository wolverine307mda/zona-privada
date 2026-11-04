using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mda.Lanzador;

// Un «Job Object» de Windows: agrupa un proceso (y sus hijos) para que muera con el lanzador y, si se pide, no pase de un tope de memoria.
// Así una API colgada no se queda gastando RAM y no sobrevive a un cierre del lanzador.
sealed class JobObject : IDisposable
{
    private nint _h;

    public JobObject(long? memoriaPorProceso = null)
    {
        _h = CreateJobObject(0, null);
        if (_h == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.Basic.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | (memoriaPorProceso is null ? 0u : JOB_OBJECT_LIMIT_PROCESS_MEMORY);
        if (memoriaPorProceso is { } m) info.ProcessMemoryLimit = (nuint)m;
        var len = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var p = Marshal.AllocHGlobal(len);
        try
        {
            Marshal.StructureToPtr(info, p, false);
            if (!SetInformationJobObject(_h, 9, p, (uint)len)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    public void Asignar(Process proceso)
    {
        if (!AssignProcessToJobObject(_h, proceso.Handle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (_h == 0) return;
        CloseHandle(_h); // con KILL_ON_JOB_CLOSE, cerrar el asa mata lo que hay dentro
        _h = 0;
    }

    private const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x100;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic;
        public IO_COUNTERS Io;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateJobObject(nint atributos, string? nombre);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(nint job, int clase, nint info, uint longitud);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(nint job, nint proceso);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint asa);
}

// Un proceso de larga duración (PostgreSQL, la API): lo lanza, lo vigila y, si se cae, lo vuelve a levantar esperando cada vez más (ver Retroceso).
// Solo guarda en el registro lo que sale por el error estándar, recortado: la salida normal se descarta (la API escribe sus registros a fichero).
sealed class ProcesoHijo : IDisposable
{
    private readonly string _nombre;
    private readonly Func<ProcessStartInfo> _crear;
    private readonly Action<string> _log;
    private readonly long? _memoria;
    private readonly CredencialServicio? _credencial;
    private readonly CancellationTokenSource _cancelar = new();
    private Process? _proceso;
    private JobObject? _job;
    private Task? _bucle;

    public ProcesoHijo(string nombre, Func<ProcessStartInfo> crear, Action<string> log, long? memoriaMaxima = null, CredencialServicio? credencial = null)
    {
        _nombre = nombre; _crear = crear; _log = log; _memoria = memoriaMaxima; _credencial = credencial;
    }

    public bool EnMarcha => _proceso is { HasExited: false };
    public int Reinicios { get; private set; }

    public void Iniciar() => _bucle ??= Task.Run(Vigilar);

    private async Task Vigilar()
    {
        var fallos = 0;
        while (!_cancelar.IsCancellationRequested)
        {
            var inicio = DateTime.UtcNow;
            try
            {
                var psi = _crear();
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                _credencial?.Aplicar(psi); // PostgreSQL y la API corren con la cuenta de servicio, no con la tuya
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                _job?.Dispose();
                _job = new JobObject(_memoria);
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (_, _) => { };
                p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _log($"[{_nombre}] {Recortar(e.Data)}"); };
                p.Start();
                _job.Asignar(p);
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                _proceso = p;
                _log($"{_nombre}: arrancado (pid {p.Id})");
                await p.WaitForExitAsync(_cancelar.Token);
                _log($"{_nombre}: terminó con código {p.ExitCode}");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                _log($"{_nombre}: no se pudo lanzar ({e.Message})");
            }
            if (_cancelar.IsCancellationRequested) break;
            fallos = Retroceso.Siguiente(fallos, DateTime.UtcNow - inicio);
            Reinicios++;
            var espera = Retroceso.Espera(fallos);
            _log($"{_nombre}: se reintenta en {espera.TotalSeconds:0} s");
            try { await Task.Delay(espera, _cancelar.Token); } catch (OperationCanceledException) { break; }
        }
    }

    private static string Recortar(string s) => s.Length <= 300 ? s : s[..300] + "…";

    // deja de vigilarlo (no lo reinicia) sin matarlo: para pararlo con calma desde fuera, como «pg_ctl stop»
    public void NoReiniciar() => _cancelar.Cancel();

    // para el proceso (y todo lo que haya lanzado) y espera un poco a que se vaya
    public async Task Parar(TimeSpan espera)
    {
        _cancelar.Cancel();
        try { if (_proceso is { HasExited: false } p) { p.Kill(true); await p.WaitForExitAsync(new CancellationTokenSource(espera).Token); } }
        catch (Exception e) when (e is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception) { }
        _job?.Dispose(); _job = null;
        if (_bucle is not null) { try { await _bucle.WaitAsync(espera); } catch (Exception e) when (e is TimeoutException or OperationCanceledException) { } }
    }

    public void Dispose() { _cancelar.Cancel(); _job?.Dispose(); _proceso?.Dispose(); }
}
