using System.Net;
using System.Text.Json;
using Mda.Api.Conexiones;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Agenda;

public record EventoGoogle(string Id, string Titulo, DateTimeOffset Inicio, DateTimeOffset? Fin, bool TodoElDia, string? Lugar, string? Notas, string? Url, string? Recurrente,
    string Cuenta = "google", string Calendario = "primary", string? NombreCalendario = null, string? Color = null);

// Google Calendar de una o varias cuentas: leer los eventos de los calendarios elegidos y copiar allí los eventos de la agenda
public class GoogleCalendar(ServicioConexiones conexiones, IMemoryCache cache, Zona zona, ILogger<GoogleCalendar> log)
{
    private const string Api = "https://www.googleapis.com/calendar/v3";
    private static readonly Proveedor G = ServicioConexiones.Google;
    private static readonly CalendarioGoogle Principal = new("primary", "Principal", null, true, true);
    private int version;   // sube al guardar o borrar: la caché de lecturas deja de valer

    private static string Eventos(string calendario) => $"{Api}/calendars/{Uri.EscapeDataString(calendario)}/events";

    // Las cuentas enlazadas con sus calendarios (en caché unos minutos; si Google no contesta, cada cuenta tiene al menos su calendario principal)
    public async Task<IReadOnlyList<CuentaGoogle>> Cuentas(CancellationToken ct)
    {
        var enlazadas = await conexiones.Listar(G, ct);
        var lista = new List<CuentaGoogle>();
        foreach (var c in enlazadas) lista.Add(new CuentaGoogle(c.Proveedor, c.Cuenta, await Calendarios(c.Proveedor, ct)));
        return lista;
    }

    private async Task<IReadOnlyList<CalendarioGoogle>> Calendarios(string clave, CancellationToken ct)
    {
        var k = $"google-cal:{clave}";
        if (cache.TryGetValue(k, out IReadOnlyList<CalendarioGoogle>? guardado) && guardado is not null) return guardado;
        try
        {
            using var res = await conexiones.Llamar(G, HttpMethod.Get, $"{Api}/users/me/calendarList?minAccessRole=reader&maxResults=100", ct: ct, clave: clave);
            if (res is { IsSuccessStatusCode: true })
            {
                var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
                string? S(JsonElement x, string p) => x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var lista = j.GetProperty("items").EnumerateArray()
                    .Where(x => S(x, "id") is not null)
                    .Select(x => new CalendarioGoogle(S(x, "id")!, S(x, "summaryOverride") ?? S(x, "summary") ?? "(sin nombre)", S(x, "backgroundColor"),
                        x.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True, S(x, "accessRole") is "owner" or "writer"))
                    .OrderByDescending(x => x.Principal).ThenBy(x => x.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (lista.Count > 0) { cache.Set(k, (IReadOnlyList<CalendarioGoogle>)lista, TimeSpan.FromMinutes(5)); return lista; }
            }
            else if (res is not null) log.LogWarning("Google Calendar no dio la lista de calendarios: {Codigo}", (int)res.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            log.LogWarning(e, "No se pudo leer la lista de calendarios de Google");
        }
        return [Principal];
    }

    // Los eventos de los calendarios que se ven, de todas las cuentas; null si no hay ninguna cuenta o ninguna contesta
    public async Task<IReadOnlyList<EventoGoogle>?> Leer(DateTimeOffset desde, DateTimeOffset hasta, AjustesGoogle ajustes, CancellationToken ct)
    {
        var cuentas = await Cuentas(ct);
        if (cuentas.Count == 0) return null;
        var pedidos = cuentas.SelectMany(c => GoogleAjustes.Mostrados(c, ajustes).Select(cal => (Cuenta: c, Cal: cal))).ToList();
        var resultados = await Task.WhenAll(pedidos.Select(p => LeerCalendario(p.Cuenta.Clave, p.Cal, desde, hasta, ct)));
        return resultados.All(r => r is null) ? null : resultados.Where(r => r is not null).SelectMany(r => r!).ToList();
    }

    private async Task<IReadOnlyList<EventoGoogle>?> LeerCalendario(string cuenta, CalendarioGoogle cal, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var clave = $"google:{version}:{cuenta}:{cal.Id}:{desde:O}:{hasta:O}";
        if (cache.TryGetValue(clave, out IReadOnlyList<EventoGoogle>? guardado)) return guardado;
        var url = $"{Eventos(cal.Id)}?singleEvents=true&orderBy=startTime&maxResults=500&timeMin={Uri.EscapeDataString(desde.ToString("O"))}&timeMax={Uri.EscapeDataString(hasta.ToString("O"))}";
        try
        {
            using var res = await conexiones.Llamar(G, HttpMethod.Get, url, ct: ct, clave: cuenta);
            if (res is null) return null;
            if (!res.IsSuccessStatusCode) { log.LogWarning("Google Calendar respondió {Codigo}", (int)res.StatusCode); return null; }
            var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            var lista = j.GetProperty("items").EnumerateArray()
                .Where(x => x.TryGetProperty("status", out var s) && s.GetString() != "cancelled")
                .Select(x => Leer(x, cuenta, cal)).Where(x => x is not null).Select(x => x!).ToList();
            cache.Set(clave, (IReadOnlyList<EventoGoogle>)lista, TimeSpan.FromSeconds(60));
            return lista;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            log.LogWarning(e, "No se pudieron leer los eventos de Google");
            return null;
        }
    }

    private EventoGoogle? Leer(JsonElement x, string cuenta, CalendarioGoogle cal)
    {
        var (ini, todo) = Momento(x.GetProperty("start"));
        if (ini is null) return null;
        var (fin, _) = x.TryGetProperty("end", out var e) ? Momento(e) : (null, false);
        string? S(string p) => x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        // en Google el final de un evento de todo el día es el día siguiente
        return new EventoGoogle(S("id")!, S("summary") ?? "(sin título)", ini.Value, todo && fin is not null ? fin.Value.AddDays(-1) : fin, todo,
            S("location"), S("description"), S("htmlLink"), S("recurringEventId"), cuenta, cal.Id, cal.Nombre, cal.Color);
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

    // Crea o actualiza el evento en Google, en el calendario de destino (si estaba en otro, lo mueve); devuelve su id (o null si no se pudo)
    public async Task<string?> Guardar(Evento e, DestinoGoogle destino, CancellationToken ct)
    {
        var cuerpo = Cuerpo(e);
        try
        {
            // cambió de cuenta o de calendario: se quita del de antes y se crea en el nuevo
            if (e.GoogleId is not null && ((e.GoogleCuenta ?? "google") != destino.Cuenta || (e.GoogleCalendario ?? "primary") != destino.Calendario))
            {
                await Borrar(e, ct);
                e.GoogleId = null;
            }
            using var res = e.GoogleId is null
                ? await conexiones.Llamar(G, HttpMethod.Post, Eventos(destino.Calendario), cuerpo, ct, destino.Cuenta)
                : await conexiones.Llamar(G, HttpMethod.Put, $"{Eventos(destino.Calendario)}/{Uri.EscapeDataString(e.GoogleId)}", cuerpo, ct, destino.Cuenta);
            if (res is null) return null;
            // si se borró en Google, se vuelve a crear
            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone && e.GoogleId is not null)
            {
                e.GoogleId = null;
                return await Guardar(e, destino, ct);
            }
            if (!res.IsSuccessStatusCode) { log.LogWarning("Google Calendar no guardó el evento: {Codigo}", (int)res.StatusCode); return null; }
            Interlocked.Increment(ref version);
            e.GoogleCuenta = destino.Cuenta; e.GoogleCalendario = destino.Calendario;
            return (await res.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString();
        }
        catch (Exception x) when (x is HttpRequestException or JsonException or KeyNotFoundException)
        {
            log.LogWarning(x, "No se pudo guardar el evento en Google");
            return null;
        }
    }

    public async Task Borrar(Evento e, CancellationToken ct)
    {
        if (e.GoogleId is not { } id) return;
        try
        {
            using var res = await conexiones.Llamar(G, HttpMethod.Delete, $"{Eventos(e.GoogleCalendario ?? "primary")}/{Uri.EscapeDataString(id)}", ct: ct, clave: e.GoogleCuenta ?? "google");
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
