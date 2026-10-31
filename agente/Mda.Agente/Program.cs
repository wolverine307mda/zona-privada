// Agente de la zona privada: cada dos segundos lee el PC (CPU, memoria, discos, red) y lo envía a la API
// (POST /api/sistema/agente) con el token compartido. Así el panel enseña el equipo real y no Docker.
//   AGENTE_URL    dirección de la API (por defecto http://localhost:8080)
//   AGENTE_TOKEN  el mismo valor que AGENTE_TOKEN del .env; si no está en el entorno, se busca en el .env
using System.Net.Http.Json;
using Mda.Api.Sistema;

var url = Variable("AGENTE_URL") ?? "http://localhost:8080";
var token = Variable("AGENTE_TOKEN") ?? DelEnv("AGENTE_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Falta AGENTE_TOKEN (en el entorno o en el .env de la zona privada).");
    return 1;
}

var lector = LectorSistema.ParaEsteEquipo();
lector.Leer(); // primera lectura: fija la referencia de la CPU y de la red

using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };
http.DefaultRequestHeaders.Add("X-Agente-Token", token);

using var cancelar = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelar.Cancel(); };

Console.WriteLine($"Enviando las métricas de {Environment.MachineName} a {url} cada 2 s. Ctrl+C para parar.");

// La salud del PC (discos, espacio, reinicio pendiente) cada 5 minutos, y las órdenes que mande la zona privada (conexión saliente)
using var esperaLarga = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(45) };
esperaLarga.DefaultRequestHeaders.Add("X-Agente-Token", token);
_ = Task.Run(async () =>
{
    using var cada5 = new PeriodicTimer(TimeSpan.FromMinutes(5));
    do
    {
        try { await http.PostAsJsonAsync("/api/sistema/agente/salud", Salud.Leer(), cancelar.Token); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { /* la API no está: en 5 min otra vez */ }
    } while (await cada5.WaitForNextTickAsync(cancelar.Token));
});
_ = Ordenes.Bucle(esperaLarga, token, cancelar.Token);
string? ultimoEstado = null;
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
try
{
    while (await timer.WaitForNextTickAsync(cancelar.Token))
    {
        string estado;
        try
        {
            var r = await http.PostAsJsonAsync("/api/sistema/agente", lector.Leer(), cancelar.Token);
            estado = r.IsSuccessStatusCode ? "conectado"
                : r.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "token incorrecto"
                : $"la API responde {(int)r.StatusCode}";
        }
        catch (HttpRequestException) { estado = "la API no responde (¿está arrancado Docker?)"; }
        catch (TaskCanceledException) when (!cancelar.IsCancellationRequested) { estado = "la API tarda demasiado"; }

        // solo se escribe cuando cambia, para no llenar la consola
        if (estado != ultimoEstado) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {estado}");
        ultimoEstado = estado;
    }
}
catch (OperationCanceledException) { }
return 0;

static string? Variable(string nombre) => Environment.GetEnvironmentVariable(nombre) is { Length: > 0 } v ? v : null;

// Busca la variable en el .env subiendo desde la carpeta actual y desde la del ejecutable
static string? DelEnv(string nombre)
{
    foreach (var inicio in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var dir = new DirectoryInfo(inicio); dir is not null; dir = dir.Parent)
        {
            var env = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(env)) continue;
            var linea = File.ReadLines(env).FirstOrDefault(l => l.StartsWith(nombre + "=", StringComparison.Ordinal));
            if (linea is not null) return linea[(nombre.Length + 1)..].Trim();
        }
    }
    return null;
}
