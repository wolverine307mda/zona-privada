using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Tiempo;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Luz;

public record KpPrevisto(DateTimeOffset Hora, double Kp);
public record PrevisionAurora(double? MinKp, double KpMaximo, DateTimeOffset? HoraMaxima, bool Posible, string Resumen, IReadOnlyList<KpPrevisto> Horas);

// Aurora boreal: el índice Kp previsto del NOAA SWPC (gratis, sin clave) contra el Kp que hace falta en tu latitud para verla en el horizonte norte.
public class ServicioAurora(IHttpClientFactory http, IMemoryCache cache, ILogger<ServicioAurora> log)
{
    public const string Url = "https://services.swpc.noaa.gov/products/noaa-planetary-k-index-forecast.json";

    // Kp mínimo para verla a simple vista desde esa latitud (aproximado, latitud geográfica del hemisferio norte); null = prácticamente imposible
    public static double? KpNecesario(double lat) => lat switch
    {
        >= 66 => 1d, >= 62 => 2d, >= 58 => 3d, >= 55 => 4d, >= 52 => 5d, >= 48 => 6d, >= 44 => 7d, >= 41 => 8d, >= 38 => 9d, _ => null,
    };

    // acepta las dos formas de la respuesta del NOAA: lista de objetos, o lista de filas con cabecera
    public static List<KpPrevisto> Parsear(JsonElement j)
    {
        var lista = new List<KpPrevisto>();
        if (j.ValueKind != JsonValueKind.Array) return lista;
        foreach (var f in j.EnumerateArray())
        {
            string? t = null, kp = null;
            if (f.ValueKind == JsonValueKind.Object) { t = Texto(f, "time_tag"); kp = Texto(f, "kp"); }
            else if (f.ValueKind == JsonValueKind.Array && f.GetArrayLength() >= 2) { t = f[0].ToString(); kp = f[1].ToString(); }
            if (t is null || !DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var hora)) continue;
            if (!double.TryParse(kp, NumberStyles.Float, CultureInfo.InvariantCulture, out var k)) continue;
            lista.Add(new KpPrevisto(hora, k));
        }
        return lista;
    }

    private static string? Texto(JsonElement o, string campo) => o.TryGetProperty(campo, out var v) ? v.ToString() : null;

    public static PrevisionAurora Evaluar(IReadOnlyList<KpPrevisto> kps, double lat, DateTimeOffset ahora)
    {
        var futuros = kps.Where(k => k.Hora >= ahora.AddHours(-3)).OrderBy(k => k.Hora).ToList();
        var minimo = KpNecesario(lat);
        if (futuros.Count == 0) return new PrevisionAurora(minimo, 0, null, false, "Sin previsión del NOAA ahora mismo.", []);
        var max = futuros.MaxBy(k => k.Kp)!;
        var posible = minimo is { } m && max.Kp >= m;
        var resumen = minimo is null ? $"Desde tu latitud la aurora casi nunca se ve (Kp máximo previsto: {max.Kp:0.#})."
            : posible ? $"Kp {max.Kp:0.#} previsto: se necesita {minimo:0.#} en tu latitud. Mira al norte, lejos de las luces."
            : $"Kp máximo previsto {max.Kp:0.#}; en tu latitud hace falta {minimo:0.#}. Hoy no toca.";
        return new PrevisionAurora(minimo, max.Kp, max.Hora, posible, resumen, futuros);
    }

    public async Task<IReadOnlyList<KpPrevisto>?> Leer(CancellationToken ct)
    {
        if (cache.TryGetValue("aurora-kp", out IReadOnlyList<KpPrevisto>? guardado)) return guardado;
        try
        {
            var lista = Parsear(await http.CreateClient("tiempo").GetFromJsonAsync<JsonElement>(Url, ct));
            if (lista.Count == 0) return null;
            cache.Set("aurora-kp", (IReadOnlyList<KpPrevisto>)lista, TimeSpan.FromMinutes(30));
            return lista;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            log.LogWarning("No se pudo leer el Kp del NOAA ({Motivo})", e.GetType().Name);
            return null;
        }
    }
}
