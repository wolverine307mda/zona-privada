// Agente de la zona privada: cada dos segundos lee el PC (CPU, memoria, discos, red) y lo envía a la API
// (POST /api/sistema/agente) con el token compartido. Así el panel enseña el equipo real y no Docker.
//   AGENTE_URL    dirección de la API (por defecto http://localhost:8080)
//   AGENTE_TOKEN  el mismo valor que AGENTE_TOKEN del .env; si no está en el entorno, se busca en el .env
// (El bucle vive en AgenteServicio.cs: el lanzador «Zona privada.exe» lo aloja dentro y no hace falta este programa.)
var url = Variable("AGENTE_URL") ?? "http://localhost:8080";
var token = Variable("AGENTE_TOKEN") ?? DelEnv("AGENTE_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Falta AGENTE_TOKEN (en el entorno o en el .env de la zona privada).");
    return 1;
}

using var cancelar = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelar.Cancel(); };

Console.WriteLine($"Enviando las métricas de {Environment.MachineName} a {url} cada 2 s. Ctrl+C para parar.");
await AgenteServicio.Ejecutar(url, token, estado => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {estado}"), cancelar.Token);
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
