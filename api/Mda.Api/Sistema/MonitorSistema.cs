using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Mda.Api.Sistema;

// Las últimas lecturas en memoria (unos dos minutos) para que el panel pinte la gráfica nada más entrar.
public class EstadoSistema
{
    private const int Maximo = 60;
    private readonly Queue<MetricasSistema> _historial = new();
    private readonly Lock _cerrojo = new();

    public void Añadir(MetricasSistema m)
    {
        lock (_cerrojo)
        {
            _historial.Enqueue(m);
            while (_historial.Count > Maximo) _historial.Dequeue();
        }
    }

    public MetricasSistema[] Historial()
    {
        lock (_cerrojo) return [.. _historial];
    }
}

// Canal en tiempo real: solo con sesión iniciada. El servidor emite "metricas" cada dos segundos.
[Authorize]
public class SistemaHub : Hub;

public class MonitorSistema(ILectorSistema lector, EstadoSistema estado, IHubContext<SistemaHub> hub, ILogger<MonitorSistema> log)
    : BackgroundService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Intervalo);
        lector.Leer(); // primera lectura: solo fija la referencia de la CPU
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var m = lector.Leer();
                estado.Añadir(m);
                await hub.Clients.All.SendAsync("metricas", m, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "No se pudo leer el estado del sistema");
            }
        }
    }
}

public static class SistemaEndpoints
{
    public static void MapSistema(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sistema", (EstadoSistema estado) =>
        {
            var historial = estado.Historial();
            return Results.Ok(new { actual = historial.LastOrDefault(), historial });
        }).RequireAuthorization();

        app.MapHub<SistemaHub>("/hubs/sistema");
    }
}
