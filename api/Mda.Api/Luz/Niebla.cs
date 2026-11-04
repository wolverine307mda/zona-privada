using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Tiempo;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Luz;

public record HoraNiebla(DateTime Hora, double Temp, double Rocio, double Humedad, double Viento, double NubesBajas, double? Temp850);
public record ResultadoNiebla(string Nivel, string Texto, int Puntos);
public record DiaNiebla(string Fecha, string Nivel, string Texto, int Puntos, string? Hora);

// Niebla, inversión térmica y mar de nubes al amanecer: son las mañanas que más piden salir con la cámara.
// Open-Meteo da la temperatura a 2 m, el punto de rocío, la humedad, el viento, las nubes bajas y la temperatura a 850 hPa (unos 1.500 m).
public class ServicioNiebla(IHttpClientFactory http, IMemoryCache cache, ILogger<ServicioNiebla> log)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Cada hora se clasifica con unas reglas sencillas y a la vez físicas:
    //  · mar de nubes: inversión (a 1.500 m hace más calor que abajo) con nubes bajas y poco viento; se ve desde un mirador por encima de ellas
    //  · niebla: el aire casi saturado (temperatura y rocío a menos de 1,5 °C, humedad ≥ 93 %) y sin viento
    //  · inversión húmeda: aire frío atrapado abajo con humedad; pueden salir jirones en los valles
    public static ResultadoNiebla Clasificar(HoraNiebla h)
    {
        var dif = h.Temp - h.Rocio;
        var inversion = h.Temp850 is { } t850 && t850 > h.Temp + 0.5;
        if (inversion && h.NubesBajas >= 70 && h.Viento <= 15)
            return new("mar-de-nubes", $"Mar de nubes probable: inversión térmica, {(int)h.NubesBajas} % de nubes bajas y viento flojo. Sube a un mirador por encima de ellas.", 90);
        if (dif <= 1.5 && h.Humedad >= 93 && h.Viento <= 8)
            return new("niebla", $"Niebla probable: temperatura y rocío casi iguales ({h.Temp:0.#} / {h.Rocio:0.#} °C), humedad {(int)h.Humedad} % y casi sin viento.", inversion ? 80 : 65);
        if (inversion && h.Viento <= 10 && dif <= 3)
            return new("inversion", "Inversión térmica con aire húmedo: pueden formarse jirones de niebla en los valles.", 50);
        return new("nada", "Sin niebla ni inversión prevista.", 0);
    }

    // de las horas de amanecer de cada día (5:00 a 9:59 locales) se queda con la mejor
    public static List<DiaNiebla> PorDias(IReadOnlyList<HoraNiebla> horas, int dias = 3) =>
        horas.Where(h => h.Hora.Hour is >= 5 and < 10).GroupBy(h => h.Hora.Date).OrderBy(g => g.Key).Take(dias).Select(g =>
        {
            var mejor = g.Select(h => (h, r: Clasificar(h))).OrderByDescending(x => x.r.Puntos).First();
            return new DiaNiebla(g.Key.ToString("yyyy-MM-dd"), mejor.r.Nivel, mejor.r.Texto, mejor.r.Puntos, mejor.r.Puntos > 0 ? mejor.h.Hora.ToString("HH:mm") : null);
        }).ToList();

    public async Task<IReadOnlyList<HoraNiebla>?> Leer(Lugar l, CancellationToken ct)
    {
        var clave = $"niebla:{l.Lat.ToString(Inv)},{l.Lon.ToString(Inv)}";
        if (cache.TryGetValue(clave, out IReadOnlyList<HoraNiebla>? guardado)) return guardado;
        var url = "https://api.open-meteo.com/v1/forecast?latitude=" + l.Lat.ToString(Inv) + "&longitude=" + l.Lon.ToString(Inv) +
            "&hourly=temperature_2m,dew_point_2m,relative_humidity_2m,wind_speed_10m,cloud_cover_low,temperature_850hPa&timezone=auto&forecast_days=4";
        try
        {
            var h = (await http.CreateClient("tiempo").GetFromJsonAsync<JsonElement>(url, ct)).GetProperty("hourly");
            double? N(string campo, int i) => h.TryGetProperty(campo, out var a) && a.ValueKind == JsonValueKind.Array && a[i] is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;
            var lista = h.GetProperty("time").EnumerateArray().Select((x, i) => new HoraNiebla(DateTime.Parse(x.GetString()!, Inv),
                N("temperature_2m", i) ?? 0, N("dew_point_2m", i) ?? -99, N("relative_humidity_2m", i) ?? 0, N("wind_speed_10m", i) ?? 0, N("cloud_cover_low", i) ?? 0, N("temperature_850hPa", i))).ToList();
            cache.Set(clave, (IReadOnlyList<HoraNiebla>)lista, TimeSpan.FromMinutes(30));
            return lista;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        {
            log.LogWarning("No se pudo leer la previsión de niebla ({Motivo})", e.GetType().Name);
            return null;
        }
    }
}
