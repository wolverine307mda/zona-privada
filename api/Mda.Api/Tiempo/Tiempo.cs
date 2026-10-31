using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Tiempo;

public record Lugar(string Nombre, double Lat, double Lon, string? Region = null);
public record Actual(double Temperatura, double Sensacion, int Codigo, bool DeDia, double Viento, int Humedad, string Texto, string Icono);
public record HoraTiempo(DateTime Hora, double Temperatura, int Lluvia, int Codigo, string Icono);
public record DiaTiempo(DateOnly Fecha, double Maxima, double Minima, int Lluvia, double Litros, int Codigo, string Texto, string Icono, string? Sale, string? Pone, double Uv);
public record Prevision(Lugar Lugar, Actual Ahora, IReadOnlyList<HoraTiempo> Horas, IReadOnlyList<DiaTiempo> Dias);

// El tiempo de donde vives con Open-Meteo (gratis y sin clave). El sitio se elige en la app y se guarda en Ajustes;
// la previsión se guarda en memoria 15 minutos para no preguntar de más.
public class ServicioTiempo(IHttpClientFactory http, IMemoryCache cache, IServiceScopeFactory scopes, ILogger<ServicioTiempo> log)
{
    public const string ClaveLugar = "tiempo-lugar";
    public const string ClaveAvisos = "tiempo-avisos";
    public const string ClaveAvisado = "tiempo-avisado";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<Lugar?> LugarGuardado(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveLugar, ct))?.Valor;
        try { return v is null ? null : JsonSerializer.Deserialize<Lugar>(v, Json); } catch (JsonException) { return null; }
    }

    public async Task GuardarLugar(AppDbContext db, Lugar l, CancellationToken ct)
    {
        var a = await db.Ajustes.FindAsync([ClaveLugar], ct) ?? db.Ajustes.Add(new Ajuste { Clave = ClaveLugar }).Entity;
        a.Valor = JsonSerializer.Serialize(l, Json);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Prevision?> Leer(CancellationToken ct = default)
    {
        if (await LugarGuardado(ct) is not { } l) return null;
        var clave = $"tiempo:{l.Lat.ToString(Inv)},{l.Lon.ToString(Inv)}";
        if (cache.TryGetValue(clave, out Prevision? p)) return p;
        var url = "https://api.open-meteo.com/v1/forecast?latitude=" + l.Lat.ToString(Inv) + "&longitude=" + l.Lon.ToString(Inv) +
            "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m,relative_humidity_2m" +
            "&hourly=temperature_2m,precipitation_probability,weather_code,is_day" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,sunrise,sunset,uv_index_max" +
            "&timezone=auto&forecast_days=7";
        try
        {
            var j = await http.CreateClient("tiempo").GetFromJsonAsync<JsonElement>(url, ct);
            p = Interpretar(l, j);
            cache.Set(clave, p, TimeSpan.FromMinutes(15));
            return p;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            log.LogWarning(e, "No se pudo leer el tiempo");
            return null;
        }
    }

    public async Task<IReadOnlyList<Lugar>> Buscar(string q, CancellationToken ct)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(q.Trim())}&count=8&language=es&format=json";
        try
        {
            var j = await http.CreateClient("tiempo").GetFromJsonAsync<JsonElement>(url, ct);
            if (!j.TryGetProperty("results", out var rs)) return [];
            return rs.EnumerateArray().Select(r => new Lugar(
                r.GetProperty("name").GetString() ?? "",
                r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(),
                string.Join(", ", new[] { Texto(r, "admin2"), Texto(r, "admin1"), Texto(r, "country") }.Where(x => !string.IsNullOrEmpty(x)).Distinct()))).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            log.LogWarning(e, "No se pudo buscar el sitio «{Q}»", q);
            return [];
        }
    }

    private static string? Texto(JsonElement r, string p) => r.TryGetProperty(p, out var v) ? v.GetString() : null;

    // la respuesta de Open-Meteo, a lo que pinta la app
    public static Prevision Interpretar(Lugar l, JsonElement j)
    {
        var c = j.GetProperty("current");
        var codigo = c.GetProperty("weather_code").GetInt32();
        var deDia = c.GetProperty("is_day").GetInt32() == 1;
        var ahora = new Actual(
            c.GetProperty("temperature_2m").GetDouble(), c.GetProperty("apparent_temperature").GetDouble(), codigo, deDia,
            c.GetProperty("wind_speed_10m").GetDouble(), (int)Math.Round(c.GetProperty("relative_humidity_2m").GetDouble()),
            Descripcion(codigo), Icono(codigo, deDia));

        var h = j.GetProperty("hourly");
        var horas = h.GetProperty("time").EnumerateArray().Select(x => DateTime.Parse(x.GetString()!, Inv)).ToList();
        var actual = DateTime.Parse(c.GetProperty("time").GetString()!, Inv);
        var desde = Math.Max(0, horas.FindIndex(x => x > actual.AddMinutes(-60)));
        var lista = Enumerable.Range(desde, Math.Min(24, horas.Count - desde)).Select(i =>
        {
            var cod = h.GetProperty("weather_code")[i].GetInt32();
            return new HoraTiempo(horas[i], h.GetProperty("temperature_2m")[i].GetDouble(), Entero(h.GetProperty("precipitation_probability")[i]),
                cod, Icono(cod, h.GetProperty("is_day")[i].GetInt32() == 1));
        }).ToList();

        var d = j.GetProperty("daily");
        var dias = d.GetProperty("time").EnumerateArray().Select((x, i) =>
        {
            var cod = d.GetProperty("weather_code")[i].GetInt32();
            return new DiaTiempo(DateOnly.Parse(x.GetString()!, Inv),
                d.GetProperty("temperature_2m_max")[i].GetDouble(), d.GetProperty("temperature_2m_min")[i].GetDouble(),
                Entero(d.GetProperty("precipitation_probability_max")[i]), Decimal(d.GetProperty("precipitation_sum")[i]),
                cod, Descripcion(cod), Icono(cod, true),
                Hora(d.GetProperty("sunrise")[i]), Hora(d.GetProperty("sunset")[i]), Decimal(d.GetProperty("uv_index_max")[i]));
        }).ToList();
        return new Prevision(l, ahora, lista, dias);
    }

    private static int Entero(JsonElement v) => v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : 0;
    private static double Decimal(JsonElement v) => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static string? Hora(JsonElement v) => v.GetString() is { Length: >= 16 } s ? s[11..16] : null;

    // ---- los códigos del tiempo de la OMM ----
    public static string Descripcion(int c) => c switch
    {
        0 => "Despejado",
        1 => "Casi despejado",
        2 => "Algo nublado",
        3 => "Nublado",
        45 or 48 => "Niebla",
        51 or 53 or 55 => "Llovizna",
        56 or 57 => "Llovizna helada",
        61 => "Lluvia débil",
        63 => "Lluvia",
        65 => "Lluvia fuerte",
        66 or 67 => "Lluvia helada",
        71 or 73 => "Nieve",
        75 => "Nevada fuerte",
        77 => "Granizo fino",
        80 or 81 => "Chubascos",
        82 => "Chubascos fuertes",
        85 or 86 => "Chubascos de nieve",
        95 => "Tormenta",
        96 or 99 => "Tormenta con granizo",
        _ => "—",
    };

    public static string Icono(int c, bool deDia) => c switch
    {
        0 => deDia ? "☀️" : "🌙",
        1 => deDia ? "🌤️" : "🌙",
        2 => "⛅",
        3 => "☁️",
        45 or 48 => "🌫️",
        >= 51 and <= 67 => "🌧️",
        >= 71 and <= 77 or 85 or 86 => "🌨️",
        >= 80 and <= 82 => "🌦️",
        >= 95 => "⛈️",
        _ => "🌡️",
    };

    // Una frase para el asistente, Telegram o el aviso: «Mañana en Madrid: lluvia, de 9 a 16 °C (80 % de lluvia)»
    public static string Frase(Prevision p, DateOnly dia, DateOnly hoy)
    {
        var cuando = dia == hoy ? "Hoy" : dia == hoy.AddDays(1) ? "Mañana" : dia.ToString("dddd d", new CultureInfo("es-ES"));
        cuando = char.ToUpper(cuando[0]) + cuando[1..];
        if (p.Dias.FirstOrDefault(d => d.Fecha == dia) is not { } d)
            return $"Solo tengo la previsión de los próximos {p.Dias.Count} días.";
        var t = $"{d.Icono} {cuando} en {p.Lugar.Nombre}: {d.Texto.ToLowerInvariant()}, de {d.Minima:0} a {d.Maxima:0} °C";
        t += d.Lluvia >= 20 ? $", {d.Lluvia} % de probabilidad de lluvia." : ", sin lluvia.";
        if (dia == hoy) t = $"Ahora {p.Ahora.Temperatura:0} °C ({p.Ahora.Texto.ToLowerInvariant()}). " + t;
        if (d.Lluvia >= 50) t += " ☂️ Coge paraguas.";
        if (d.Uv >= 7) t += " Protección solar: índice UV alto.";
        return t;
    }

    // El aviso de la víspera: lluvia probable, tormentas, mucho calor o helada
    public static (string Titulo, string Texto)? AvisoDe(Prevision p, DateOnly manana)
    {
        if (p.Dias.FirstOrDefault(d => d.Fecha == manana) is not { } d) return null;
        var rango = $"de {d.Minima:0} a {d.Maxima:0} °C en {p.Lugar.Nombre}";
        if (d.Codigo >= 95) return ("⛈️ Mañana hay tormentas", $"{d.Texto}, {rango}. {d.Lluvia} % de probabilidad de lluvia.");
        if (d.Lluvia >= 60) return ("☔ Mañana llueve", $"{d.Lluvia} % de probabilidad de lluvia, {rango}. Coge el paraguas.");
        if (d.Maxima >= 35) return ("🥵 Mañana mucho calor", $"Hasta {d.Maxima:0} °C ({rango}). Bebe agua y evita las horas centrales.");
        if (d.Minima <= 0) return ("🥶 Mañana helada", $"Mínima de {d.Minima:0} °C ({rango}). Ojo con el coche a primera hora.");
        return null;
    }
}
