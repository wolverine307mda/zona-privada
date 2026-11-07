using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Tiempo;

// Ropa tendida y lluvia: marcas «ropa tendida» (en Inicio, con el atajo del iPhone o con /ropa en Telegram) y, mientras esté tendida,
// el aviso de que va a llover en unos minutos. La marca caduca sola a las 10 horas o al marcar «recogida».
public record PuntoLluvia(DateTime Utc, double Mm);
public record LluviaProxima(int Minutos, double Mm);

public interface ILluvia { Task<IReadOnlyList<PuntoLluvia>?> Proxima(Lugar lugar, CancellationToken ct); }

// Open-Meteo, tramos de 15 minutos de lo que va a llover (en hora UTC para no liarse con zonas); se guarda 5 minutos
public sealed class ServicioLluvia(IHttpClientFactory http, IMemoryCache cache, ILogger<ServicioLluvia> log) : ILluvia
{
    public async Task<IReadOnlyList<PuntoLluvia>?> Proxima(Lugar l, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var clave = $"lluvia:{l.Lat.ToString(inv)},{l.Lon.ToString(inv)}";
        if (cache.TryGetValue(clave, out IReadOnlyList<PuntoLluvia>? hecho)) return hecho;
        var url = "https://api.open-meteo.com/v1/forecast?latitude=" + l.Lat.ToString(inv) + "&longitude=" + l.Lon.ToString(inv) +
                  "&minutely_15=precipitation&forecast_minutely_15=16&past_minutely_15=2&timezone=UTC";
        try
        {
            var j = await http.CreateClient("tiempo").GetFromJsonAsync<JsonElement>(url, ct);
            var m = j.GetProperty("minutely_15");
            var t = m.GetProperty("time").EnumerateArray().ToList(); var p = m.GetProperty("precipitation").EnumerateArray().ToList();
            var lista = t.Select((x, i) => new PuntoLluvia(DateTime.SpecifyKind(DateTime.Parse(x.GetString()!, inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), DateTimeKind.Utc),
                p[i].ValueKind == JsonValueKind.Number ? p[i].GetDouble() : 0)).ToList();
            cache.Set(clave, (IReadOnlyList<PuntoLluvia>)lista, TimeSpan.FromMinutes(5));
            return lista;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            log.LogWarning(e, "No se pudo leer la lluvia próxima");
            return null;
        }
    }
}

public static class Ropa
{
    public const string Clave = "ropa-tendida", ClaveAvisada = "ropa-avisada";
    public static readonly TimeSpan Caduca = TimeSpan.FromHours(10);
    public const int AvisarAntes = 30;                // minutos de margen con los que se avisa
    public const double Real = 0.2;                   // litros por tramo de 15 min que ya son lluvia de verdad (menos es llovizna que no moja)

    // Pura: en cuántos minutos empieza a llover. 0 = ya llueve; null = nada en las próximas 3 horas.
    // Un tramo suelto de llovizna (menos de 0,2 mm y sin otro al lado) es una falsa alarma y no cuenta.
    public static LluviaProxima? EnMinutos(IReadOnlyList<PuntoLluvia> datos, DateTime ahoraUtc)
    {
        var tramos = datos.OrderBy(d => d.Utc).ToList();
        for (var i = 0; i < tramos.Count; i++)
        {
            var d = tramos[i];
            var fin = d.Utc.AddMinutes(15);
            if (fin <= ahoraUtc || d.Utc > ahoraUtc.AddHours(3)) continue;
            var siguiente = i + 1 < tramos.Count ? tramos[i + 1].Mm : 0;
            var llueve = d.Mm >= Real || (d.Mm >= 0.1 && siguiente >= 0.1);
            if (!llueve) continue;
            var min = (int)Math.Max(0, Math.Ceiling((d.Utc - ahoraUtc).TotalMinutes));
            return new LluviaProxima(min, d.Mm);
        }
        return null;
    }

    public static string Frase(LluviaProxima l) => l.Minutos == 0 ? "Está lloviendo (o a punto): recoge la ropa" : $"Probablemente llueva en ~{l.Minutos} min: recoge la ropa";

    public static async Task<DateTimeOffset?> Desde(AppDbContext db, DateTimeOffset ahora, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor;
        if (!DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var f)) return null;
        return ahora - f > Caduca ? null : f;        // pasadas 10 horas se da por recogida
    }

    private static async Task Poner(AppDbContext db, string clave, string? valor, CancellationToken ct)
    {
        var a = await db.Ajustes.FirstOrDefaultAsync(x => x.Clave == clave, ct);
        if (valor is null) { if (a is not null) db.Ajustes.Remove(a); }
        else if (a is null) db.Ajustes.Add(new Ajuste { Clave = clave, Valor = valor }); else a.Valor = valor;
        await db.SaveChangesAsync(ct);
    }

    public static async Task Tender(AppDbContext db, DateTimeOffset ahora, CancellationToken ct)
    {
        await Poner(db, Clave, ahora.ToString("O", CultureInfo.InvariantCulture), ct);
        await Poner(db, ClaveAvisada, null, ct);
    }

    public static async Task Recoger(AppDbContext db, CancellationToken ct) { await Poner(db, Clave, null, ct); await Poner(db, ClaveAvisada, null, ct); }

    // Cada minuto (la rueda de MotorContexto): si hay ropa tendida y va a llover, un aviso por episodio con botón «Recogida»
    public static async Task Vigilar(AppDbContext db, ServicioTiempo tiempo, ILluvia lluvia, IAvisos avisos, Bot bot, DateTimeOffset ahora, ILogger log, CancellationToken ct)
    {
        if (await Desde(db, ahora, ct) is not { } desde) return;
        var avisada = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveAvisada, ct))?.Valor;
        if (avisada == desde.ToString("O", CultureInfo.InvariantCulture)) return;
        if (await tiempo.LugarGuardado(ct) is not { } lugar || await lluvia.Proxima(lugar, ct) is not { } datos) return;
        if (EnMinutos(datos, ahora.UtcDateTime) is not { } l || l.Minutos > AvisarAntes) return;

        await Poner(db, ClaveAvisada, desde.ToString("O", CultureInfo.InvariantCulture), ct);
        var texto = Frase(l);
        try
        {
            await avisos.PorPush("🧺 Ropa tendida", texto, "/", ct);
            if (bot.Vinculado) await bot.Enviar($"🧺 {texto}", new { inline_keyboard = new[] { new[] { new { text = "Recogida", callback_data = "rr:" } } } }, ct: ct);
        }
        catch (Exception e) { log.LogWarning(e, "No se pudo avisar de la ropa tendida"); }
    }

    public static void MapRopa(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/tiempo/ropa").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, ServicioTiempo tiempo, ILluvia lluvia, TimeProvider reloj, CancellationToken ct) =>
        {
            var ahora = reloj.GetUtcNow();
            var desde = await Desde(db, ahora, ct);
            LluviaProxima? l = null;
            if (desde is not null && await tiempo.LugarGuardado(ct) is { } lugar && await lluvia.Proxima(lugar, ct) is { } datos) l = EnMinutos(datos, ahora.UtcDateTime);
            return Results.Ok(new { tendida = desde is not null, desde, lluvia = l, aviso = l is null ? null : Frase(l) });
        });

        g.MapPost("/tender", async (AppDbContext db, TimeProvider reloj, CancellationToken ct) => { await Tender(db, reloj.GetUtcNow(), ct); return Results.Ok(new { tendida = true }); });
        g.MapPost("/recoger", async (AppDbContext db, CancellationToken ct) => { await Recoger(db, ct); return Results.Ok(new { tendida = false }); });
    }
}
