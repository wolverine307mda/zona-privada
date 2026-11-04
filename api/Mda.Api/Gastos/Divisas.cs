using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Gastos;

public record Conversion(string Moneda, decimal Original, decimal Euros, decimal Tipo, DateOnly FechaTipo);

// Gastos en otra moneda: se convierten a euros con el tipo de cambio de referencia del Banco Central Europeo (gratis, sin clave, de los últimos 90 días).
// El gasto se guarda en euros (para que las sumas cuadren) y se anota la moneda y el importe original.
public class ServicioDivisas(IHttpClientFactory http, IMemoryCache cache, ILogger<ServicioDivisas> log)
{
    public const string Url = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist-90d.xml";
    public static readonly string[] Habituales = ["USD", "GBP", "CHF", "JPY", "MXN", "ARS", "COP", "BRL", "CAD", "AUD", "SEK", "NOK", "DKK", "PLN", "CZK", "TRY", "MAD"];

    // cada día con sus monedas: unidades de esa moneda por 1 euro
    public static Dictionary<DateOnly, Dictionary<string, decimal>> Parsear(string xml)
    {
        var d = new Dictionary<DateOnly, Dictionary<string, decimal>>();
        foreach (var dia in XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "Cube" && e.Attribute("time") is not null))
        {
            if (!DateOnly.TryParseExact((string?)dia.Attribute("time"), "yyyy-MM-dd", out var fecha)) continue;
            var monedas = new Dictionary<string, decimal>();
            foreach (var c in dia.Elements().Where(e => e.Attribute("currency") is not null && e.Attribute("rate") is not null))
                if (decimal.TryParse((string?)c.Attribute("rate"), NumberStyles.Number, CultureInfo.InvariantCulture, out var r) && r > 0) monedas[(string)c.Attribute("currency")!] = r;
            d[fecha] = monedas;
        }
        return d;
    }

    // el tipo del día pedido o, si fue fin de semana o festivo, el último anterior (hasta 10 días atrás)
    public static Conversion? Convertir(IReadOnlyDictionary<DateOnly, Dictionary<string, decimal>> tipos, string moneda, decimal importe, DateOnly fecha)
    {
        moneda = moneda.Trim().ToUpperInvariant();
        if (moneda == "EUR" || importe <= 0) return null;
        for (var i = 0; i <= 10; i++)
        {
            var dia = fecha.AddDays(-i);
            if (tipos.TryGetValue(dia, out var m) && m.TryGetValue(moneda, out var tipo))
                return new Conversion(moneda, importe, Math.Round(importe / tipo, 2, MidpointRounding.AwayFromZero), tipo, dia);
        }
        // fecha más nueva que los datos (hoy aún sin publicar) o más vieja que 90 días: el tipo más cercano que haya
        var cerca = tipos.Where(kv => kv.Value.ContainsKey(moneda)).OrderBy(kv => Math.Abs(kv.Key.DayNumber - fecha.DayNumber)).FirstOrDefault();
        return cerca.Value is null ? null : new Conversion(moneda, importe, Math.Round(importe / cerca.Value[moneda], 2, MidpointRounding.AwayFromZero), cerca.Value[moneda], cerca.Key);
    }

    public async Task<Dictionary<DateOnly, Dictionary<string, decimal>>?> Tipos(CancellationToken ct)
    {
        if (cache.TryGetValue("divisas-bce", out Dictionary<DateOnly, Dictionary<string, decimal>>? guardado)) return guardado;
        try
        {
            var xml = await http.CreateClient("tiempo").GetStringAsync(Url, ct);
            var d = Parsear(xml);
            if (d.Count == 0) return null;
            cache.Set("divisas-bce", d, TimeSpan.FromHours(6));
            return d;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            log.LogWarning("No se pudieron leer los tipos de cambio del BCE ({Motivo})", e.GetType().Name);
            return null;
        }
    }

    public static bool MonedaValida(string? m) => m is { Length: 3 } && m.All(c => c is >= 'A' and <= 'Z');
}

public static class DivisasEndpoints
{
    public static void MapDivisas(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/gastos/divisas", () => Results.Ok(ServicioDivisas.Habituales)).RequireAuthorization();

        app.MapGet("/api/gastos/divisas/convertir", async (string? moneda, decimal? importe, string? fecha, ServicioDivisas divisas, TimeProvider reloj, CancellationToken ct) =>
        {
            if (!ServicioDivisas.MonedaValida(moneda?.ToUpperInvariant()) || importe is null or <= 0 or > 100_000_000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["moneda"] = ["Pon una moneda de 3 letras (USD, GBP…) y un importe."] });
            var dia = DateOnly.TryParseExact(fecha, "yyyy-MM-dd", out var f) ? f : DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
            if (await divisas.Tipos(ct) is not { } tipos) return Results.Json(new { detalle = "No puedo consultar el Banco Central Europeo ahora mismo." }, statusCode: StatusCodes.Status502BadGateway);
            return ServicioDivisas.Convertir(tipos, moneda!, importe.Value, dia) is { } c ? Results.Ok(c) : Results.NotFound(new { detalle = "El BCE no publica esa moneda." });
        }).RequireAuthorization();
    }
}
