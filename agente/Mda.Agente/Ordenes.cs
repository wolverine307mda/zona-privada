using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// Las órdenes que manda la zona privada. El agente NUNCA abre un puerto: es él quien pregunta a la API (conexión saliente).
// Solo ejecuta una lista cerrada y solo si la firma cuadra con el secreto compartido y no ha caducado:
// una orden inventada, retocada, repetida tarde o de otro tipo se descarta.
record OrdenRecibida(int Id, string Tipo, string Parametro, long Caduca, string Firma);
record ResultadoOrden(bool Ok, string Resultado);

static partial class Ordenes
{
    static readonly HashSet<string> Permitidas = ["bloquear", "despierto", "reiniciar", "suspender", "apagar", "cancelar"];

    public static async Task Bucle(HttpClient esperaLarga, string secreto, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // la API retiene la petición hasta 20 s si no hay órdenes
                var lista = await esperaLarga.GetFromJsonAsync<List<OrdenRecibida>>("/api/sistema/agente/ordenes", ct) ?? [];
                foreach (var o in lista)
                {
                    var r = Ejecutar(o, secreto);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] orden {o.Tipo}: {(r.Ok ? "hecha" : "rechazada")} ({r.Resultado})");
                    try { await esperaLarga.PostAsJsonAsync($"/api/sistema/agente/ordenes/{o.Id}", r, ct); } catch (HttpRequestException) { /* la API no se entera: caduca */ }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct); // API reiniciándose o sin red: se reintenta con calma
            }
        }
    }

    static string Firma(string secreto, OrdenRecibida o) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), Encoding.UTF8.GetBytes($"{o.Id}|{o.Tipo}|{o.Parametro}|{o.Caduca}"))).ToLowerInvariant();

    internal static ResultadoOrden Ejecutar(OrdenRecibida o, string secreto)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Firma(secreto, o)), Encoding.UTF8.GetBytes(o.Firma ?? "")))
            return new(false, "firma no válida");
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > o.Caduca) return new(false, "caducada");
        if (!Permitidas.Contains(o.Tipo)) return new(false, "orden no permitida");
        try
        {
            switch (o.Tipo)
            {
                case "bloquear": return LockWorkStation() ? new(true, "pantalla bloqueada") : new(false, "Windows no la dejó bloquear");
                case "despierto": Despierto.Poner(o.Parametro != "off"); return new(true, o.Parametro == "off" ? "puede suspenderse" : "se mantiene despierto");
                case "reiniciar": Lanzar("shutdown", "/r /t 30 /c \"Zona privada: reinicio en 30 s (cancelable)\""); return new(true, "reinicio en 30 s");
                case "apagar": Lanzar("shutdown", "/s /t 30 /c \"Zona privada: apagado en 30 s (cancelable)\""); return new(true, "apagado en 30 s");
                case "suspender": Lanzar("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"); return new(true, "suspendiendo");
                case "cancelar": Lanzar("shutdown", "/a"); return new(true, "cancelado");
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return new(false, e.Message); }
        return new(false, "sin acción");
    }

    static void Lanzar(string exe, string args) =>
        Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false })?.Dispose();

    [LibraryImport("user32.dll", EntryPoint = "LockWorkStation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();
}

// «Mantener despierto»: Windows pide que SetThreadExecutionState lo llame un hilo que siga vivo, así que hay uno dedicado
static class Despierto
{
    const uint Continuo = 0x80000000, Sistema = 0x00000001;
    static Thread? hilo;
    static readonly ManualResetEventSlim Parar = new(false);

    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint estado);

    public static void Poner(bool activo)
    {
        if (!activo) { Parar.Set(); hilo = null; return; }
        if (hilo is { IsAlive: true }) return;
        Parar.Reset();
        hilo = new Thread(() => { SetThreadExecutionState(Continuo | Sistema); Parar.Wait(); SetThreadExecutionState(Continuo); }) { IsBackground = true, Name = "despierto" };
        hilo.Start();
    }
}
