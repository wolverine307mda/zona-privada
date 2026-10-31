using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Tiempo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Luz;

public record AjustesLuz(bool Activa = false, int Umbral = 6, int AntelacionMin = 90, bool Amanecer = false, bool Astro = true, int Bortle = 5);

public record VentanaLuz(string Tipo, string Titulo, DateTimeOffset Inicio, DateTimeOffset Fin, int? Puntuacion, string Detalle);

// Avisos de luz para fotografiar: la hora dorada (tarde y, si quieres, mañana) y las noches de Vía Láctea.
// El sol, la luna y el núcleo galáctico se calculan en local (Astro); solo las nubes vienen de Open-Meteo, que ya usa el tiempo.
public class ServicioLuz(IHttpClientFactory http, IMemoryCache cache, Zona zona, ILogger<ServicioLuz> log)
{
    public const string ClaveAjustes = "luz-ajustes";
    public const string ClaveAvisado = "luz-avisado";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static async Task<AjustesLuz> Leer(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveAjustes, ct))?.Valor;
        try { return v is null ? new AjustesLuz() : JsonSerializer.Deserialize<AjustesLuz>(v, Json) ?? new AjustesLuz(); } catch (JsonException) { return new AjustesLuz(); }
    }

    public static AjustesLuz Normalizar(AjustesLuz a) => a with { Umbral = Math.Clamp(a.Umbral, 1, 10), AntelacionMin = Math.Clamp(a.AntelacionMin, 15, 360), Bortle = Math.Clamp(a.Bortle, 1, 9) };

    public static async Task Guardar(AppDbContext db, AjustesLuz a, CancellationToken ct)
    {
        var x = await db.Ajustes.FindAsync([ClaveAjustes], ct);
        if (x is null) db.Ajustes.Add(x = new Ajuste { Clave = ClaveAjustes });
        x.Valor = JsonSerializer.Serialize(Normalizar(a), Json);
        await db.SaveChangesAsync(ct);
    }

    // las nubes, por horas, de los próximos días (Open-Meteo, gratis y sin clave); null sin lugar o sin conexión
    public async Task<IReadOnlyList<HoraNubes>?> Nubes(Lugar l, CancellationToken ct)
    {
        var clave = $"nubes:{l.Lat.ToString(Inv)},{l.Lon.ToString(Inv)}";
        if (cache.TryGetValue(clave, out IReadOnlyList<HoraNubes>? guardado)) return guardado;
        var url = "https://api.open-meteo.com/v1/forecast?latitude=" + l.Lat.ToString(Inv) + "&longitude=" + l.Lon.ToString(Inv) +
            "&hourly=cloud_cover,cloud_cover_low,cloud_cover_mid,cloud_cover_high,precipitation_probability,visibility,wind_speed_10m&timezone=auto&forecast_days=4";
        try
        {
            var j = await http.CreateClient("tiempo").GetFromJsonAsync<JsonElement>(url, ct);
            var h = j.GetProperty("hourly");
            var horas = h.GetProperty("time").EnumerateArray().Select(x => DateTime.Parse(x.GetString()!, Inv)).ToList();
            int E(string campo, int i) => h.GetProperty(campo)[i] is { ValueKind: JsonValueKind.Number } v ? (int)Math.Round(v.GetDouble()) : 0;
            double D(string campo, int i) => h.GetProperty(campo)[i] is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : 0;
            var lista = horas.Select((t, i) => new HoraNubes(t, E("cloud_cover", i), E("cloud_cover_low", i), E("cloud_cover_mid", i), E("cloud_cover_high", i),
                E("precipitation_probability", i), D("visibility", i) / 1000, D("wind_speed_10m", i))).ToList();
            cache.Set(clave, (IReadOnlyList<HoraNubes>)lista, TimeSpan.FromMinutes(30));
            return lista;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            log.LogWarning("No se pudieron leer las nubes ({Motivo})", e.GetType().Name);
            return null;
        }
    }

    private static IReadOnlyList<HoraNubes> Entre(IReadOnlyList<HoraNubes> todas, DateTime desde, DateTime hasta) =>
        todas.Where(h => h.Hora >= desde.AddMinutes(-59) && h.Hora < hasta).ToList();

    // lo que hay hoy (y esta noche) para fotografiar, con su puntuación si hay previsión
    public async Task<List<VentanaLuz>> Ventanas(DateOnly dia, Lugar l, AjustesLuz a, CancellationToken ct)
    {
        var nubes = await Nubes(l, ct);
        var lista = new List<VentanaLuz>();
        var (manana, tarde) = Astro.HoraDorada(dia, zona.Info, l.Lat, l.Lon);
        int? Dorada(Tramo t) => nubes is null ? null : Astro.PuntuarDorada(Entre(nubes, Local(t.Inicio), Local(t.Fin)));
        if (a.Amanecer && manana is { } m) lista.Add(new VentanaLuz("amanecer", "Hora dorada de la mañana", m.Inicio, m.Fin, Dorada(m), "Sol subiendo"));
        if (tarde is { } t2) lista.Add(new VentanaLuz("atardecer", "Hora dorada de la tarde", t2.Inicio, t2.Fin, Dorada(t2), "Sol bajando"));
        if (a.Astro && Astro.NocheOscura(dia, zona.Info, l.Lat, l.Lon) is { } n)
        {
            var luna = Astro.IluminacionLuna(n.Inicio);
            var lunaArriba = Astro.AlturaLuna(n.Inicio.AddMinutes(n.Duracion.TotalMinutes / 2), l.Lat, l.Lon) > 0;
            int? p = nubes is null ? null : Astro.PuntuarNoche(Entre(nubes, Local(n.Inicio), Local(n.Fin)), luna, lunaArriba, a.Bortle);
            lista.Add(new VentanaLuz("noche", "Noche de Vía Láctea", n.Inicio, n.Fin, p, $"{Astro.NombreFase(n.Inicio)} ({(int)Math.Round(luna * 100)} %), núcleo visible {(int)n.Duracion.TotalHours} h {n.Duracion.Minutes} min"));
        }
        return lista;
    }

    private DateTime Local(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, zona.Info).DateTime;
}

// Cada 10 minutos mira si empieza pronto una luz buena y, si es el caso y no estás trabajando ni tienes un evento, avisa por push y Telegram
public class AvisosLuz(IServiceScopeFactory scopes, ServicioLuz luz, ServicioTiempo tiempo, IAvisos avisos, Zona zona, TimeProvider reloj, ILogger<AvisosLuz> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), reloj, ct); } catch (OperationCanceledException) { return; }
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(10), reloj);
        do
        {
            try { await Revisar(ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Fallo en los avisos de luz"); }
        } while (await t.WaitForNextTickAsync(ct));
    }

    public async Task Revisar(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var a = await ServicioLuz.Leer(db, ct);
        if (!a.Activa || await tiempo.LugarGuardado(ct) is not { } lugar) return;
        var ahora = reloj.GetUtcNow();
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, zona.Info).DateTime);
        var horario = await Horario.Leer(db, ct);
        var avisado = (await db.Ajustes.FindAsync([ServicioLuz.ClaveAvisado], ct))?.Valor?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? [];

        // las ventanas de hoy y, para la noche de Vía Láctea, también la de mañana (se avisa la víspera por la tarde)
        var candidatas = (await luz.Ventanas(hoy, lugar, a, ct)).ToList();
        if (a.Astro) candidatas.AddRange((await luz.Ventanas(hoy.AddDays(1), lugar, a with { Amanecer = false }, ct)).Where(v => v.Tipo == "noche"));
        foreach (var v in candidatas)
        {
            var dia = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(v.Inicio, zona.Info).DateTime);
            var clave = $"{dia:yyyy-MM-dd}:{v.Tipo}";
            if (avisado.Contains(clave) || v.Puntuacion is not { } p || p < a.Umbral) continue;
            var local = TimeZoneInfo.ConvertTime(v.Inicio, zona.Info);
            // cuándo avisar: «antelación» minutos antes de la hora dorada; la noche, la tarde anterior (de 17:00 a 21:00)
            var ahoraLocal = TimeZoneInfo.ConvertTime(ahora, zona.Info);
            var avisarYa = v.Tipo == "noche"
                ? ahoraLocal.Hour is >= 17 and < 21 && DateOnly.FromDateTime(ahoraLocal.DateTime) >= dia.AddDays(-1) && DateOnly.FromDateTime(ahoraLocal.DateTime) <= dia
                : ahora >= v.Inicio.AddMinutes(-a.AntelacionMin) && ahora < v.Fin.AddMinutes(-10);
            if (!avisarYa) continue;
            // no avisar si estás en el trabajo, si mañana entras a trabajar (noche) o si tienes un evento encima
            var fin = TimeZoneInfo.ConvertTime(v.Fin, zona.Info).DateTime;
            if (v.Tipo != "noche" && horario is not null && horario.Pisa(local.DateTime, fin)) continue;
            if (v.Tipo == "noche" && horario is not null && horario.Trabaja(dia.AddDays(1))) continue;
            if (v.Tipo != "noche" && await db.Eventos.AnyAsync(e => e.Tipo == TipoEvento.Evento && e.Inicio < v.Fin && (e.Fin ?? e.Inicio) > v.Inicio, ct)) continue;

            var texto = v.Tipo == "noche"
                ? $"{v.Detalle}. Noche de {local:dddd d} · {local:HH:mm} · puntuación {p}/10."
                : $"{v.Titulo}: de {local:HH:mm} a {TimeZoneInfo.ConvertTime(v.Fin, zona.Info):HH:mm} · puntuación {p}/10. Estás libre.";
            var titulo = v.Tipo == "noche" ? "🌌 Noche para la Vía Láctea" : v.Tipo == "amanecer" ? "🌅 Hora dorada de la mañana" : "🌇 Hora dorada";
            await avisos.PorPush(titulo, texto, "/fotos", ct);
            await avisos.PorTelegram($"{titulo}\n{texto}", ct);
            avisado.Add(clave);
            var reg = await db.Ajustes.FindAsync([ServicioLuz.ClaveAvisado], ct);
            if (reg is null) db.Ajustes.Add(reg = new Ajuste { Clave = ServicioLuz.ClaveAvisado });
            reg.Valor = string.Join(',', avisado.TakeLast(14));
            await db.SaveChangesAsync(ct);
        }
    }
}

public static class LuzEndpoints
{
    public static void MapLuz(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/luz").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, ServicioTiempo tiempo, ServicioLuz luz, Zona zona, TimeProvider reloj, CancellationToken ct) =>
        {
            var a = await ServicioLuz.Leer(db, ct);
            var lugar = await tiempo.LugarGuardado(ct);
            if (lugar is null) return Results.Ok(new { ajustes = a, lugar = (object?)null, hoy = Array.Empty<VentanaLuz>(), manana = Array.Empty<VentanaLuz>() });
            var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).DateTime);
            // en la pantalla se ve todo, esté el aviso activado o no
            var todo = a with { Amanecer = true, Astro = true };
            return Results.Ok(new { ajustes = a, lugar = lugar.Nombre, hoy = await luz.Ventanas(hoy, lugar, todo, ct), manana = await luz.Ventanas(hoy.AddDays(1), lugar, todo, ct) });
        });

        g.MapPut("/ajustes", async (AjustesLuz a, AppDbContext db, CancellationToken ct) =>
        {
            await ServicioLuz.Guardar(db, a, ct);
            return Results.NoContent();
        });
    }
}
