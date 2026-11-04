using System.Globalization;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Agenda;

[Flags]
public enum Via { Nada = 0, Correo = 1, WhatsApp = 2, Telegram = 4, Push = 8 }

// Cada 30 segundos mira qué avisos tocan y los manda por los canales elegidos.
// - WhatsApp (CallMeBot) tarda en llegar: sale 5 minutos ANTES de la hora y, a la hora, el mismo aviso sale por los demás canales.
// - Cada canal se anota aparte: si uno falla se reintenta solo ese (a 1, 5 y 15 minutos) y los que ya llegaron no se repiten.
// - Se anota que se va a enviar ANTES de enviar: si el proceso cae a mitad, no se repite cada 30 segundos.
// - Si no se consigue por ningún canal, o el aviso llega con más de 12 horas de retraso (el equipo estuvo apagado), te lo cuenta por
//   Telegram en vez de callarse.
public class Recordatorios(IServiceScopeFactory scopes, IAvisos avisos, Zona zona, TimeProvider reloj, ILogger<Recordatorios> log, TimeSpan? whatsAppAntes = null) : BackgroundService
{
    private static readonly CultureInfo Es = new("es-ES");
    private static readonly TimeSpan[] Esperas = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];
    private const int MaxIntentos = 4;
    private static readonly TimeSpan Reserva = TimeSpan.FromMinutes(2), TiempoCanal = TimeSpan.FromSeconds(30), Caducado = TimeSpan.FromHours(12);
    private readonly TimeSpan antesWhatsApp = whatsAppAntes ?? TimeSpan.FromMinutes(5);

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
        var margen = ahora + antesWhatsApp; // el WhatsApp se adelanta
        var tocan = await db.Eventos.Where(e => e.ProximoAviso != null && e.ProximoAviso <= margen).ToListAsync(ct);
        foreach (var e in tocan)
        {
            // cada evento por separado y guardado al momento: si un canal falla, ni se repite el aviso ni se bloquean los demás
            try { await Atender(db, e, ahora, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.LogError(ex, "No se pudo avisar de «{Titulo}»", e.Titulo);
                db.ChangeTracker.Clear();
            }
        }
    }

    private static Via Activos(Evento e) =>
        (e.PorCorreo ? Via.Correo : 0) | (e.PorWhatsApp ? Via.WhatsApp : 0) | (e.PorTelegram ? Via.Telegram : 0) | (e.PorPush ? Via.Push : 0);

    private async Task Atender(AppDbContext db, Evento e, DateTimeOffset ahora, CancellationToken ct)
    {
        if (e.ReintentoDesde is { } r && r > ahora) return;
        var aviso = e.ProximoAviso!.Value;
        var de = e.AvisoDe ?? e.Inicio;
        var enHora = ahora >= aviso;

        if (ahora - aviso >= Caducado)
        {
            log.LogWarning("Aviso de «{Titulo}» con {Horas:0} h de retraso: se salta", e.Titulo, (ahora - aviso).TotalHours);
            await avisos.PorTelegram($"⚠️ Te perdiste el aviso de «{e.Titulo}» ({Cuando(e, de)}): el servidor no pudo enviarlo a tiempo.", ct);
            await Avanzar(db, e, de, ahora, ct);
            return;
        }

        var hecho = (Via)(e.CanalesEnviados | e.CanalesRendidos);
        var disponibles = avisos.Disponibles();
        var pedidos = new List<(Via Via, string Nombre, Func<Task<bool>> Enviar)>();
        var texto = Texto(e, de);
        if (e.PorCorreo && !hecho.HasFlag(Via.Correo) && enHora) pedidos.Add((Via.Correo, "correo", () => avisos.PorCorreo($"⏰ {e.Titulo}", texto, "/agenda", ct)));
        if (e.PorWhatsApp && !hecho.HasFlag(Via.WhatsApp) && ahora >= aviso - antesWhatsApp) pedidos.Add((Via.WhatsApp, "WhatsApp", () => avisos.PorWhatsApp(texto, ct)));
        if (e.PorTelegram && !hecho.HasFlag(Via.Telegram) && enHora) pedidos.Add((Via.Telegram, "Telegram", () => avisos.PorTelegram(texto, ct)));
        if (e.PorPush && !hecho.HasFlag(Via.Push) && enHora) pedidos.Add((Via.Push, "push", () => avisos.PorPush($"⏰ {e.Titulo}", Cuando(e, de), "/agenda", ct)));

        if (pedidos.Count == 0)
        {
            if (enHora) await Avanzar(db, e, de, ahora, ct); // todos los canales ya resueltos
            return;
        }

        // un canal que no está configurado no se reintenta: se da por perdido a la primera
        var sinConfigurar = pedidos.Where(p => (p.Via == Via.Correo && !disponibles.Correo) || (p.Via == Via.WhatsApp && !disponibles.WhatsApp) || (p.Via == Via.Telegram && !disponibles.Telegram)).ToList();
        foreach (var p in sinConfigurar) { e.CanalesRendidos |= (int)p.Via; log.LogInformation("Aviso de «{Titulo}»: {Canal} no está configurado", e.Titulo, p.Nombre); }
        pedidos = pedidos.Except(sinConfigurar).ToList();

        // reserva: si el proceso cae mientras envía, no se vuelve a intentar cada 30 s
        e.ReintentoDesde = ahora + Reserva;
        await db.SaveChangesAsync(ct);

        var resultados = await Task.WhenAll(pedidos.Select(p => Canal(p.Enviar, p.Nombre, e)));
        var fallidos = new List<(Via Via, string Nombre, Func<Task<bool>> Enviar)>();
        for (var i = 0; i < pedidos.Count; i++)
        {
            if (resultados[i]) { e.CanalesEnviados |= (int)pedidos[i].Via; e.UltimoAviso = ahora; }
            else fallidos.Add(pedidos[i]);
        }
        log.LogInformation("Aviso de «{Titulo}»: enviado por {Ok}{Mal}", e.Titulo, string.Join(", ", pedidos.Where((_, i) => resultados[i]).Select(p => p.Nombre).DefaultIfEmpty("ninguno")),
            fallidos.Count > 0 ? $"; falló {string.Join(", ", fallidos.Select(p => p.Nombre))}" : "");

        if (fallidos.Count > 0)
        {
            e.Intentos++;
            if (e.Intentos >= MaxIntentos)
            {
                foreach (var p in fallidos) e.CanalesRendidos |= (int)p.Via;
                e.Intentos = 0; e.ReintentoDesde = null;
                if (e.CanalesEnviados == 0) await AvisarFallo(e, de, fallidos.Select(p => p.Via).Aggregate(Via.Nada, (a, b) => a | b), ct);
            }
            else e.ReintentoDesde = ahora + Esperas[Math.Min(e.Intentos - 1, Esperas.Length - 1)];
        }
        else { e.Intentos = 0; e.ReintentoDesde = null; }

        // todos los canales activos resueltos (entregados o rendidos) y ya es la hora: a la siguiente repetición
        var activos = Activos(e);
        var resuelto = (Via)(e.CanalesEnviados | e.CanalesRendidos);
        if (enHora && (resuelto & activos) == activos) await Avanzar(db, e, de, ahora, ct);
        else await db.SaveChangesAsync(ct);
    }

    private async Task AvisarFallo(Evento e, DateTimeOffset de, Via canales, CancellationToken ct)
    {
        var texto = $"⚠️ No pude avisarte de «{e.Titulo}» ({Cuando(e, de)}) por {canales.ToString().ToLowerInvariant()}.";
        var ok = !canales.HasFlag(Via.Telegram) && await Canal(() => avisos.PorTelegram(texto, ct), "Telegram", e);
        if (!ok && !canales.HasFlag(Via.Push)) ok = await Canal(() => avisos.PorPush("⚠️ Aviso no enviado", texto, "/agenda", ct), "push", e);
        if (!ok) log.LogWarning("«{Titulo}»: no se pudo avisar por ningún canal ni contarlo", e.Titulo);
    }

    // pasa a la siguiente repetición (o la deja sin aviso) y deja el estado de envío limpio
    private async Task Avanzar(AppDbContext db, Evento e, DateTimeOffset de, DateTimeOffset ahora, CancellationToken ct)
    {
        e.UltimaOcurrenciaAvisada = de;
        e.CanalesEnviados = 0; e.CanalesRendidos = 0; e.Intentos = 0; e.ReintentoDesde = null;
        var p = Ocurrencias.ProximoAviso(e, de.AddSeconds(1), ahora, zona.Info);
        e.ProximoAviso = p?.Aviso;
        e.AvisoDe = p?.De;
        await db.SaveChangesAsync(ct);
    }

    // un canal que explota o tarda demasiado (tiempo agotado, asunto raro…) cuenta como «no enviado», sin tumbar a los otros
    private async Task<bool> Canal(Func<Task<bool>> enviar, string nombre, Evento e)
    {
        try { return await enviar().WaitAsync(TiempoCanal); }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            log.LogWarning(ex, "El aviso de «{Titulo}» por {Canal} ha fallado", e.Titulo, nombre);
            return false;
        }
        catch (TimeoutException)
        {
            log.LogWarning("El aviso de «{Titulo}» por {Canal} ha tardado demasiado", e.Titulo, nombre);
            return false;
        }
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
