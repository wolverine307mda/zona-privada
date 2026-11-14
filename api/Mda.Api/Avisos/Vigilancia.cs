using System.Collections.Concurrent;

namespace Mda.Api.Avisos;

public record PasoVigilancia(string Nombre, DateTimeOffset UltimaVez, bool UltimoOk, string? UltimoError, DateTimeOffset? UltimoFallo, int Fallos);

// Qué hizo por última vez cada comprobación del Vigilante (y cuántas veces falló) y cuántos errores 5xx sirvió la API hace poco.
// Solo vive en memoria: sirve para ver «¿está viva esta vigilancia?» desde la app sin abrir Seq.
public class RegistroVigilancia(TimeProvider reloj)
{
    private readonly ConcurrentDictionary<string, PasoVigilancia> _pasos = new();
    private readonly ConcurrentQueue<DateTimeOffset> _errores = new();

    public void Anotar(string nombre, Exception? fallo = null)
    {
        var ahora = reloj.GetUtcNow();
        _pasos.AddOrUpdate(nombre,
            _ => new PasoVigilancia(nombre, ahora, fallo is null, fallo?.Message, fallo is null ? null : ahora, fallo is null ? 0 : 1),
            (_, p) => fallo is null
                ? p with { UltimaVez = ahora, UltimoOk = true }
                : p with { UltimaVez = ahora, UltimoOk = false, UltimoError = fallo.Message, UltimoFallo = ahora, Fallos = p.Fallos + 1 });
    }

    public IReadOnlyList<PasoVigilancia> Pasos() => _pasos.Values.OrderBy(p => p.Nombre).ToList();

    public void Error5xx()
    {
        _errores.Enqueue(reloj.GetUtcNow());
        while (_errores.Count > 500 && _errores.TryDequeue(out _)) { }
    }

    public int Errores5xx(TimeSpan ventana)
    {
        var desde = reloj.GetUtcNow() - ventana;
        return _errores.Count(e => e >= desde);
    }
}

// Cada minuto mira cuántos errores del servidor ha habido: 5 o más en 10 minutos avisan por Telegram y push (como mucho una vez por hora).
public class AlertaErrores(RegistroVigilancia registro, IAvisos avisos, TimeProvider reloj, ILogger<AlertaErrores> log, Mda.Api.Servidor.GuardiaServicio? guardia = null) : BackgroundService
{
    public const int Umbral = 5;
    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(10), Silencio = TimeSpan.FromHours(1);
    private DateTimeOffset? _ultimoAviso;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(1), reloj);
        try
        {
            while (await t.WaitForNextTickAsync(ct))
            {
                try { await Revisar(ct); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Fallo al vigilar los errores del servidor"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task Revisar(CancellationToken ct)
    {
        var n = registro.Errores5xx(Ventana);
        if (n < Umbral) return;
        var ahora = reloj.GetUtcNow();
        if (_ultimoAviso is { } u && ahora - u < Silencio) return;
        _ultimoAviso = ahora;
        var texto = $"La API ha dado {n} errores del servidor en los últimos 10 minutos. Mira los registros en Seq (http://localhost:5341).";
        await avisos.Avisar("seguridad", "🔥 Errores en la API", texto, textoTelegram: $"🔥 {texto}", ct: ct);
        if (guardia is not null) await guardia.Proponer("errores5xx", "Errores en la API", texto, ct);
    }
}

public static class VigilanciaEndpoints
{
    public static void MapVigilancia(this IEndpointRouteBuilder app)
    {
        // Lo que hizo cada comprobación del Vigilante por última vez y los errores 5xx recientes
        app.MapGet("/api/vigilancia", (RegistroVigilancia r) => Results.Ok(new
        {
            pasos = r.Pasos(),
            errores10min = r.Errores5xx(AlertaErrores.Ventana),
            umbral = AlertaErrores.Umbral,
        })).RequireAuthorization();
    }
}
