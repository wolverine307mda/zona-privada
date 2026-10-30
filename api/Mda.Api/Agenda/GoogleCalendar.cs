using System.Net;
using System.Text.Json;
using Mda.Api.Conexiones;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Agenda;

public record EventoGoogle(string Id, string Titulo, DateTimeOffset Inicio, DateTimeOffset? Fin, bool TodoElDia, string? Lugar, string? Notas, string? Url, string? Recurrente);

// Google Calendar del calendario principal: leer los eventos de un rango y copiar allí los eventos de la agenda
public class GoogleCalendar(ServicioConexiones conexiones, IMemoryCache cache, Zona zona, ILogger<GoogleCalendar> log)
{
    private const string Base = "https://www.googleapis.com/calendar/v3/calendars/primary/events";
    private static readonly Proveedor G = ServicioConexiones.Google;
    private int version; // sube al guardar o borrar: la caché de lecturas deja de valer

    public async Task<IReadOnlyList<EventoGoogle>?> Leer(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var clave = $"google:{version}:{desde:O}:{hasta:O}";
        if (cache.TryGetValue(clave, out IReadOnlyList<EventoGoogle>? guardado)) return guardado;
        var url = $"{Base}?singleEvents=true&orderBy=startTime&maxResults=500&timeMin={Uri.EscapeDataString(desde.ToString("O"))}&timeMax={Uri.EscapeDataString(hasta.ToString("O"))}";
        try
        {
            using var res = await conexiones.Llamar(G, HttpMethod.Get, url, ct: ct);
            if (res is null) return null;
            if (!res.IsSuccessStatusCode) { log.LogWarning("Google Calendar respondió {Codigo}", (int)res.StatusCode); return null; }
            var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            var lista = j.GetProperty("items").EnumerateArray()
                .Where(x => x.TryGetProperty("status", out var s) && s.GetString() != "cancelled")
                .Select(Leer).Where(x => x is not null).Select(x => x!).ToList();
            cache.Set(clave, (IReadOnlyList<EventoGoogle>)lista, TimeSpan.FromSeconds(60));
            return lista;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException)
        {
            log.LogWarning(e, "No se pudieron leer los eventos de Google");
            return null;
        }
    }

    private EventoGoogle? Leer(JsonElement x)
    {
        var (ini, todo) = Momento(x.GetProperty("start"));
        if (ini is null) return null;
        var (fin, _) = x.TryGetProperty("end", out var e) ? Momento(e) : (null, false);
        string? S(string p) => x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        // en Google el final de un evento de todo el día es el día siguiente
        return new EventoGoogle(S("id")!, S("summary") ?? "(sin título)", ini.Value, todo && fin is not null ? fin.Value.AddDays(-1) : fin, todo,
            S("location"), S("description"), S("htmlLink"), S("recurringEventId"));
    }

    private (DateTimeOffset?, bool) Momento(JsonElement m)
    {
        if (m.TryGetProperty("dateTime", out var dt)) return (dt.GetDateTimeOffset(), false);
        if (m.TryGetProperty("date", out var d) && DateOnly.TryParse(d.GetString(), out var dia))
        {
            var l = dia.ToDateTime(TimeOnly.MinValue);
            return (new DateTimeOffset(l, zona.Info.GetUtcOffset(l)), true);
        }
        return (null, false);
    }

    // Crea o actualiza el evento en Google; devuelve su id (o null si no se pudo)
    public async Task<string?> Guardar(Evento e, CancellationToken ct)
    {
        var cuerpo = Cuerpo(e);
        try
        {
            using var res = e.GoogleId is null
                ? await conexiones.Llamar(G, HttpMethod.Post, Base, cuerpo, ct)
                : await conexiones.Llamar(G, HttpMethod.Put, $"{Base}/{Uri.EscapeDataString(e.GoogleId)}", cuerpo, ct);
            if (res is null) return null;
            // si se borró en Google, se vuelve a crear
            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone && e.GoogleId is not null)
            {
                e.GoogleId = null;
                return await Guardar(e, ct);
            }
            if (!res.IsSuccessStatusCode) { log.LogWarning("Google Calendar no guardó el evento: {Codigo}", (int)res.StatusCode); return null; }
            Interlocked.Increment(ref version);
            return (await res.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString();
        }
        catch (Exception x) when (x is HttpRequestException or JsonException or KeyNotFoundException)
        {
            log.LogWarning(x, "No se pudo guardar el evento en Google");
            return null;
        }
    }

    public async Task Borrar(string googleId, CancellationToken ct)
    {
        try
        {
            using var res = await conexiones.Llamar(G, HttpMethod.Delete, $"{Base}/{Uri.EscapeDataString(googleId)}", ct: ct);
            Interlocked.Increment(ref version);
        }
        catch (HttpRequestException x) { log.LogWarning(x, "No se pudo borrar el evento de Google"); }
    }

    private object Cuerpo(Evento e)
    {
        var fin = e.Fin ?? (e.TodoElDia ? e.Inicio : e.Inicio.AddMinutes(e.Tipo == TipoEvento.Recordatorio ? 15 : 60));
        object Cuando(DateTimeOffset m, bool esFin) => e.TodoElDia
            ? new { date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m, zona.Info).DateTime).AddDays(esFin ? 1 : 0).ToString("yyyy-MM-dd") }
            : new { dateTime = m.ToString("O"), timeZone = zona.Info.Id };
        var regla = e.Repetir switch
        {
            Repeticion.Diaria => "DAILY", Repeticion.Semanal => "WEEKLY", Repeticion.Mensual => "MONTHLY", Repeticion.Anual => "YEARLY", _ => null,
        };
        return new
        {
            summary = e.Tipo == TipoEvento.Recordatorio ? $"⏰ {e.Titulo}" : e.Titulo,
            description = e.Notas, location = e.Lugar,
            start = Cuando(e.Inicio, false), end = Cuando(fin, true),
            recurrence = regla is null ? null : new[] { $"RRULE:FREQ={regla}{(e.RepetirHasta is { } h ? $";UNTIL={h:yyyyMMdd}" : "")}" },
        };
    }
}
