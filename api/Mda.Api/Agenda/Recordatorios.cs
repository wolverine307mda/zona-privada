using System.Globalization;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Agenda;

// Cada 30 segundos mira qué avisos tocan y los manda por correo y/o WhatsApp.
// Si el equipo estuvo apagado, los avisos de hace más de 12 horas se saltan (no llega una ristra de golpe).
public class Recordatorios(IServiceScopeFactory scopes, IAvisos avisos, Zona zona, TimeProvider reloj, ILogger<Recordatorios> log) : BackgroundService
{
    private static readonly CultureInfo Es = new("es-ES");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var t = new PeriodicTimer(TimeSpan.FromSeconds(30), reloj);
        do
        {
            try { await Revisar(ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Fallo revisando los recordatorios"); }
        } while (await t.WaitForNextTickAsync(ct));
    }

    public async Task Revisar(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ahora = reloj.GetUtcNow();
        var tocan = await db.Eventos.Where(e => e.ProximoAviso != null && e.ProximoAviso <= ahora).ToListAsync(ct);
        foreach (var e in tocan)
        {
            var de = e.AvisoDe ?? e.Inicio;
            if (ahora - e.ProximoAviso!.Value < TimeSpan.FromHours(12))
            {
                var texto = Texto(e, de);
                var ok = false;
                if (e.PorCorreo) ok |= await avisos.PorCorreo($"⏰ {e.Titulo}", texto, ct);
                if (e.PorWhatsApp) ok |= await avisos.PorWhatsApp(texto, ct);
                if (e.PorPush) ok |= await avisos.PorPush($"⏰ {e.Titulo}", Cuando(e, de), "/agenda", ct);
                if (ok) e.UltimoAviso = ahora;
                log.LogInformation("Aviso de «{Titulo}»: {Resultado}", e.Titulo, ok ? "enviado" : "sin canal que funcione");
            }
            var p = Ocurrencias.ProximoAviso(e, de.AddSeconds(1), ahora, zona.Info);
            e.ProximoAviso = p?.Aviso;
            e.AvisoDe = p?.De;
        }
        if (tocan.Count > 0) await db.SaveChangesAsync(ct);
    }

    private string Cuando(Evento e, DateTimeOffset de)
    {
        var local = TimeZoneInfo.ConvertTime(de, zona.Info);
        var cuando = e.TodoElDia ? local.ToString("dddd d 'de' MMMM", Es) : local.ToString("dddd d 'de' MMMM, HH:mm", Es);
        return e.Lugar is null ? cuando : $"{cuando} · {e.Lugar}";
    }

    private string Texto(Evento e, DateTimeOffset de)
    {
        var local = TimeZoneInfo.ConvertTime(de, zona.Info);
        var cuando = e.TodoElDia ? local.ToString("dddd d 'de' MMMM", Es) : local.ToString("dddd d 'de' MMMM, HH:mm", Es);
        var lineas = new List<string> { $"⏰ {e.Titulo}", cuando };
        if (e.Lugar is not null) lineas.Add($"📍 {e.Lugar}");
        if (e.Notas is not null) lineas.AddRange(["", e.Notas]);
        return string.Join('\n', lineas);
    }
}
