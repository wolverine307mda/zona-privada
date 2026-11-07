using System.ComponentModel;
using System.Diagnostics;
using System.Text;

// Ubicación simulada del iPhone, desde el PC y por cable USB, con pymobiledevice3 (gratis: «pip install -U pymobiledevice3»).
// Solo se hace con una orden firmada, que a su vez solo existe si tú has encendido «Ubicación del iPhone» en la zona privada y has confirmado que eres tú.
//   · La simulación vive mientras viva un proceso de pymobiledevice3, así que el agente lo mantiene y lo vigila: si el agente se cierra, la ubicación vuelve sola.
//   · Siempre tiene hora de fin (de 1 minuto a 12 horas): cuando llega, se para y se restaura la ubicación real.
//   · El proceso se arranca con una lista de argumentos (nada pasa por una consola) y las coordenadas ya validadas.
//   AGENTE_PYMOBILEDEVICE3   ruta de pymobiledevice3.exe si no está en el PATH (si no, se prueba «python -m pymobiledevice3»)
//   AGENTE_IPHONE            UDID del iPhone si hay varios conectados
static class Ubicacion
{
    static readonly SemaphoreSlim Turno = new(1, 1);
    static Process? proceso;
    static CancellationTokenSource? fin;
    static string? gpx;
    static (double Lat, double Lon) punto;
    static (double Lat, double Lon, DateTimeOffset Salida, int Segundos)? trayecto;
    static (double Lat, double Lon) destino;
    static DateTimeOffset? hasta;
    static readonly StringBuilder ErrorReciente = new();

    public static async Task<ResultadoOrden> Hacer(string parametro, CancellationToken ct)
    {
        if (UbicacionLogica.Leer(parametro) is not { } p) return new(false, "parámetro no válido");
        await Turno.WaitAsync(ct);
        try
        {
            return p.Accion switch
            {
                "estado" => await Estado(ct),
                "parar" => await Parar(ct),
                "fijar" => await Fijar(p, ct),
                _ => await Ir(p, ct),
            };
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException) { return new(false, UbicacionLogica.Motivo(e.Message)); }
        finally { Turno.Release(); }
    }

    static bool Viva => proceso is { HasExited: false };

    static async Task<ResultadoOrden> Fijar(UbicacionLogica.Peticion p, CancellationToken ct)
    {
        await Detener(restaurar: false, ct);
        var error = await Lanzar(["set", "--", UbicacionLogica.Formato(p.Lat), UbicacionLogica.Formato(p.Lon)], ct);
        if (error is not null) return new(false, error);
        (punto, destino, trayecto) = ((p.Lat, p.Lon), (p.Lat, p.Lon), null);
        ProgramarFin(p.Minutos);
        return new(true, $"iPhone en {UbicacionLogica.Formato(p.Lat)}, {UbicacionLogica.Formato(p.Lon)} hasta las {UbicacionLogica.Hora(hasta!.Value)}");
    }

    static async Task<ResultadoOrden> Ir(UbicacionLogica.Peticion p, CancellationToken ct)
    {
        if (!Viva) return new(false, "primero fija un punto de partida: «ir» camina desde donde esté el iPhone ahora");
        var (la, lo) = Actual();
        var metros = UbicacionLogica.Distancia(la, lo, p.Lat, p.Lon);
        if (metros < 5) return new(false, "ya estás ahí");
        var segundos = UbicacionLogica.Segundos(metros, p.Modo);
        if (segundos > p.Minutos * 60) return new(false, $"{p.Modo} son {Duracion(segundos)}: no llega en {p.Minutos} min (sube el tiempo, hasta 12 h, o cambia de modo)");
        await Detener(restaurar: false, ct);
        var ruta = Path.Combine(Path.GetTempPath(), $"mda-ruta-{Guid.NewGuid():N}.gpx");
        await File.WriteAllTextAsync(ruta, UbicacionLogica.Gpx(la, lo, p.Lat, p.Lon, segundos, DateTime.UtcNow), ct);
        gpx = ruta;
        var error = await Lanzar(["play", ruta], ct);
        if (error is not null) { BorrarGpx(); return new(false, error); }
        trayecto = (la, lo, DateTimeOffset.UtcNow, segundos);
        (punto, destino) = ((la, lo), (p.Lat, p.Lon));
        ProgramarFin(p.Minutos);
        return new(true, $"{p.Modo}: {metros / 1000:0.0} km, llega en {Duracion(segundos)}; se restaura a las {UbicacionLogica.Hora(hasta!.Value)}");
    }

    // dónde está ahora (si va de camino, calculado por el tiempo que lleva)
    static (double Lat, double Lon) Actual()
    {
        if (trayecto is not { } t) return punto;
        var f = (DateTimeOffset.UtcNow - t.Salida).TotalSeconds / t.Segundos;
        return UbicacionLogica.Interpolar(t.Lat, t.Lon, destino.Lat, destino.Lon, f);
    }

    static async Task<ResultadoOrden> Estado(CancellationToken ct)
    {
        var aparato = await Aparato(ct);
        if (aparato is null) return new(false, "no veo ningún iPhone: conéctalo por cable, desbloquéalo y pulsa «Confiar» (y mira que pymobiledevice3 esté instalado)");
        if (!Viva) return new(true, $"{aparato}: ubicación real (no hay simulación)");
        var (la, lo) = Actual();
        return new(true, $"{aparato}: simulando {UbicacionLogica.Formato(la)}, {UbicacionLogica.Formato(lo)} hasta las {UbicacionLogica.Hora(hasta!.Value)}");
    }

    public static async Task<ResultadoOrden> Parar(CancellationToken ct)
    {
        var estaba = Viva;
        var fallo = await Detener(restaurar: true, ct);
        return fallo is null ? new(true, estaba ? "ubicación real restaurada" : "no había ninguna simulación (ubicación real)") : new(false, fallo);
    }

    // Para el proceso y, si se pide, le dice al iPhone que vuelva a su ubicación real (lo que no responda en 20 s se da por hecho: el iPhone la recupera al perder la sesión)
    static async Task<string?> Detener(bool restaurar, CancellationToken ct)
    {
        fin?.Cancel(); fin?.Dispose(); fin = null;
        var p = proceso; proceso = null;
        if (p is not null) { try { if (!p.HasExited) p.Kill(true); } catch (Exception e) when (e is InvalidOperationException or Win32Exception) { /* ya había terminado */ } p.Dispose(); }
        BorrarGpx();
        (trayecto, hasta) = (null, null);
        if (!restaurar) return null;
        try
        {
            using var tiempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tiempo.CancelAfter(TimeSpan.FromSeconds(20));
            var (codigo, salida) = await Correr(["clear"], tiempo.Token);
            return codigo == 0 ? null : UbicacionLogica.Motivo(salida);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    // Lanza pymobiledevice3 y espera unos segundos: si sigue vivo, la simulación está en marcha; si se cierra, se cuenta por qué
    static async Task<string?> Lanzar(string[] sub, CancellationToken ct)
    {
        var info = Arrancar(["developer", "dvt", "simulate-location", .. sub]);
        if (info is null) return "falta pymobiledevice3 en el PC («pip install -U pymobiledevice3»)";
        ErrorReciente.Clear();
        var p = Process.Start(info) ?? throw new Win32Exception("no se pudo arrancar pymobiledevice3");
        p.ErrorDataReceived += (_, e) => { if (e.Data is { } l) lock (ErrorReciente) { ErrorReciente.AppendLine(l); if (ErrorReciente.Length > 4000) ErrorReciente.Remove(0, ErrorReciente.Length - 4000); } };
        p.OutputDataReceived += (_, e) => { if (e.Data is { } l) lock (ErrorReciente) { ErrorReciente.AppendLine(l); if (ErrorReciente.Length > 4000) ErrorReciente.Remove(0, ErrorReciente.Length - 4000); } };
        p.BeginErrorReadLine(); p.BeginOutputReadLine();
        // abrir el túnel con el iPhone tarda: se le da hasta 12 s para fallar
        var salio = await Task.WhenAny(p.WaitForExitAsync(ct), Task.Delay(TimeSpan.FromSeconds(12), ct));
        if (p.HasExited)
        {
            string texto; lock (ErrorReciente) texto = ErrorReciente.ToString();
            p.Dispose();
            return UbicacionLogica.Motivo(texto);
        }
        _ = salio;
        proceso = p;
        // si más tarde el proceso se cae solo (cable fuera, iPhone bloqueado…), la API lo sabrá en la siguiente comprobación
        return null;
    }

    static void ProgramarFin(int minutos)
    {
        hasta = DateTimeOffset.UtcNow.AddMinutes(minutos);
        fin = new CancellationTokenSource();
        var token = fin.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromMinutes(minutos), token); } catch (OperationCanceledException) { return; }
            await Turno.WaitAsync();
            try { if (!token.IsCancellationRequested) { await Detener(restaurar: true, CancellationToken.None); Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ubicación: tiempo cumplido, restaurada"); } }
            finally { Turno.Release(); }
        });
    }

    static void BorrarGpx() { if (gpx is not null) { try { File.Delete(gpx); } catch (IOException) { /* la limpia Windows */ } gpx = null; } }

    // Al cerrarse el agente, la ubicación vuelve sola (se mata el proceso; no se espera a nada más)
    public static void ParaAlSalir()
    {
        try { if (proceso is { HasExited: false } p) p.Kill(true); } catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
        BorrarGpx();
    }

    static async Task<string?> Aparato(CancellationToken ct)
    {
        try
        {
            using var tiempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tiempo.CancelAfter(TimeSpan.FromSeconds(15));
            var (codigo, salida) = await Correr(["usbmux", "list"], tiempo.Token, sinDispositivo: true);
            return codigo == 0 ? UbicacionLogica.Aparato(salida) ?? (salida.Contains("Identifier", StringComparison.OrdinalIgnoreCase) ? "iPhone" : null) : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    // Una orden corta de pymobiledevice3 (hasta que acabe). «sinDispositivo»: comandos que no llevan --udid
    static async Task<(int Codigo, string Salida)> Correr(string[] args, CancellationToken ct, bool sinDispositivo = false)
    {
        var info = Arrancar(sinDispositivo ? args : ["developer", "dvt", "simulate-location", .. args]);
        if (info is null) return (-1, "falta pymobiledevice3");
        using var p = Process.Start(info) ?? throw new Win32Exception("no se pudo arrancar pymobiledevice3");
        var salida = p.StandardOutput.ReadToEndAsync(ct);
        var error = p.StandardError.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
        return (p.ExitCode, await salida + "\n" + await error);
    }

    // pymobiledevice3.exe si está en el PATH (o en la variable); si no, «python -m pymobiledevice3»
    static ProcessStartInfo? Arrancar(string[] args)
    {
        string? exe = Environment.GetEnvironmentVariable("AGENTE_PYMOBILEDEVICE3") is { Length: > 0 } a && File.Exists(a) ? a : BuscarEnPath("pymobiledevice3.exe");
        string[] antes = [];
        if (exe is null) { exe = BuscarEnPath("python.exe"); antes = ["-m", "pymobiledevice3"]; }
        if (exe is null) return null;
        var info = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8 };
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var x in antes) info.ArgumentList.Add(x);
        foreach (var x in args) info.ArgumentList.Add(x);
        // el UDID va en el subcomando (set / play / clear), nunca antes
        if (Environment.GetEnvironmentVariable("AGENTE_IPHONE") is { Length: > 0 } udid && UdidValido(udid) && args.Length > 0 && args[0] != "usbmux")
        {
            var i = info.ArgumentList.IndexOf(args.Contains("set") ? "set" : args.Contains("play") ? "play" : "clear");
            if (i >= 0) { info.ArgumentList.Insert(i + 1, "--udid"); info.ArgumentList.Insert(i + 2, udid); }
        }
        return info;
    }

    static bool UdidValido(string u) => u.Length is >= 8 and <= 64 && u.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    static string? BuscarEnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => { try { return Path.Combine(d.Trim('"'), exe); } catch (ArgumentException) { return ""; } }).FirstOrDefault(File.Exists);

    static string Duracion(int s) => s >= 3600 ? $"{s / 3600} h {s % 3600 / 60} min" : s >= 60 ? $"{s / 60} min" : $"{s} s";
}
