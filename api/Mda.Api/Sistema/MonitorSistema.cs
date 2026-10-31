using System.Security.Cryptography;
using System.Text;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
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

// Lo último que ha mandado el agente de Windows (las métricas reales del PC).
// Si deja de llegar, el monitor vuelve a medir el contenedor.
public class EstadoAgente(TimeProvider reloj)
{
    public static readonly TimeSpan Caducidad = TimeSpan.FromSeconds(8);
    private MetricasSistema? _ultima;
    private DateTimeOffset _recibida;

    public void Recibir(MetricasSistema m)
    {
        _ultima = m with { Origen = "equipo" };
        _recibida = reloj.GetUtcNow();
    }

    private DateTimeOffset _visto;
    // el agente ha hablado hace poco (métricas, salud o preguntando por órdenes)
    public void Visto() => _visto = reloj.GetUtcNow();
    public bool Conectado(TimeSpan margen) => reloj.GetUtcNow() - (_visto > _recibida ? _visto : _recibida) < margen && (_visto != default || _recibida != default);

    public MetricasSistema? Reciente() =>
        _ultima is not null && reloj.GetUtcNow() - _recibida < Caducidad ? _ultima with { Hora = reloj.GetUtcNow() } : null;
}

// Canal en tiempo real: solo con sesión iniciada. El servidor emite "metricas" cada dos segundos.
[Authorize]
public class SistemaHub : Hub;

public class MonitorSistema(ILectorSistema lector, EstadoSistema estado, EstadoAgente agente, IHubContext<SistemaHub> hub, ILogger<MonitorSistema> log)
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
                // el agente manda los datos del PC de verdad; si no está, se mide donde corre la API
                var local = lector.Leer();
                var m = agente.Reciente() ?? local;
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
    public const int TokenMinimo = 32; // openssl rand -hex 32
    public static void MapSistema(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sistema", (EstadoSistema estado) =>
        {
            var historial = estado.Historial();
            return Results.Ok(new { actual = historial.LastOrDefault(), historial });
        }).RequireAuthorization();

        // El agente de Windows (agente/Mda.Agente) envía aquí sus lecturas con un token compartido (Agente:Token).
        // Sin token configurado (o con menos de 32 caracteres) la ruta no existe. El agente llama directo a la API, nunca
        // por el proxy: lo que llega con X-Forwarded-For viene de Internet y se rechaza. Cuerpo pequeño y comprobado
        // después del token (nada de leer 30 MB de JSON de quien no tiene permiso).
        app.MapPost("/api/sistema/agente", async (HttpRequest req, EstadoAgente agente, IConfiguration config) =>
        {
            var esperado = config["Agente:Token"];
            // UseForwardedHeaders retira X-Forwarded-For al procesarlo y deja la copia en X-Original-For
            var porProxy = req.Headers.ContainsKey("X-Forwarded-For") || req.Headers.ContainsKey("X-Original-For");
            if (esperado is not { Length: >= TokenMinimo } || porProxy) return Results.NotFound();
            var recibido = req.Headers["X-Agente-Token"].ToString();
            var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recibido), Encoding.UTF8.GetBytes(esperado));
            if (!ok) return Results.Unauthorized();
            // una lectura del PC ocupa ~1 KB: nada de cuerpos grandes
            const int MaxCuerpo = 16 * 1024;
            if (req.ContentLength > MaxCuerpo) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            if (req.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limite) limite.MaxRequestBodySize = MaxCuerpo;
            MetricasSistema? m;
            try { m = await req.ReadFromJsonAsync<MetricasSistema>(); }
            catch (Exception e) when (e is System.Text.Json.JsonException or BadHttpRequestException) { return Results.BadRequest(); }
            if (m is null) return Results.BadRequest();
            agente.Recibir(m);
            agente.Visto();
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting(Limites.Agente);

        app.MapHub<SistemaHub>("/hubs/sistema");
    }
}
