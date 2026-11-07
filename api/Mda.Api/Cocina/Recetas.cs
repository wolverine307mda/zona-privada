using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Mda.Api.Cocina;

// Lo que no toca la base de datos: leer una receta de una página web (marcado schema.org/Recipe en JSON-LD, que usan casi todas las webs de cocina),
// separar «200 g de harina» en cantidad, unidad y nombre, y sumar ingredientes de varias recetas con las unidades normalizadas.
public record IngredienteLeido(string Nombre, double? Cantidad, string Unidad, string Texto);
public record RecetaLeida(string Nombre, int Raciones, int Minutos, List<string> Etiquetas, List<IngredienteLeido> Ingredientes, List<string> Pasos);

public static partial class SchemaRecipe
{
    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Bloques();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Etiquetas();

    // Pura: busca una Receta entre los bloques JSON-LD de la página (sueltos, en lista o dentro de «@graph»). null si no hay.
    public static RecetaLeida? Parsear(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        foreach (Match m in Bloques().Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim()));
                if (Buscar(doc.RootElement) is { } r) return r;
            }
            catch (JsonException) { /* un bloque roto no impide mirar los demás */ }
        }
        return null;
    }

    private static RecetaLeida? Buscar(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) if (Buscar(x) is { } r) return r; return null;
            case JsonValueKind.Object:
                if (EsReceta(e)) return Leer(e);
                if (e.TryGetProperty("@graph", out var g)) return Buscar(g);
                return null;
            default: return null;
        }
    }

    private static bool EsReceta(JsonElement e) => e.TryGetProperty("@type", out var t) && (t.ValueKind == JsonValueKind.String ? t.GetString() == "Recipe" : t.ValueKind == JsonValueKind.Array && t.EnumerateArray().Any(x => x.GetString() == "Recipe"));

    private static string Limpio(string? s) => Regex.Replace(Etiquetas().Replace(System.Net.WebUtility.HtmlDecode(s ?? ""), " "), @"\s+", " ").Trim();

    private static RecetaLeida? Leer(JsonElement e)
    {
        var nombre = Limpio(e.TryGetProperty("name", out var n) ? n.GetString() : null);
        if (nombre.Length == 0) return null;
        var ingredientes = e.TryGetProperty("recipeIngredient", out var ri) && ri.ValueKind == JsonValueKind.Array
            ? ri.EnumerateArray().Select(x => Ingrediente.Parsear(Limpio(x.GetString()))).Where(x => x.Nombre.Length > 0).ToList() : [];
        var pasos = new List<string>();
        if (e.TryGetProperty("recipeInstructions", out var pi)) Pasos(pi, pasos);
        var etiquetas = new List<string>();
        foreach (var clave in new[] { "keywords", "recipeCategory", "recipeCuisine" })
            if (e.TryGetProperty(clave, out var k))
                foreach (var t in (k.ValueKind == JsonValueKind.Array ? k.EnumerateArray().Select(x => x.GetString() ?? "") : (k.GetString() ?? "").Split(',')).Select(x => Limpio(x).ToLowerInvariant()).Where(x => x.Length is > 0 and <= 30)) etiquetas.Add(t);
        var minutos = Duracion(e.TryGetProperty("totalTime", out var tt) ? tt.GetString() : null) ?? Duracion(e.TryGetProperty("cookTime", out var ct) ? ct.GetString() : null) ?? 0;
        return new RecetaLeida(nombre[..Math.Min(nombre.Length, 120)], Raciones(e.TryGetProperty("recipeYield", out var y) ? y : default), minutos, etiquetas.Distinct().Take(10).ToList(), ingredientes.Take(80).ToList(), pasos.Take(60).ToList());
    }

    private static void Pasos(JsonElement e, List<string> pasos)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: foreach (var l in Limpio(e.GetString()).Split(". ", StringSplitOptions.RemoveEmptyEntries)) { var t = l.Trim(); if (t.Length > 0) pasos.Add(t.EndsWith('.') ? t : t + "."); } break;
            case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Pasos(x, pasos); break;
            case JsonValueKind.Object:
                if (e.TryGetProperty("itemListElement", out var l2)) Pasos(l2, pasos);                    // HowToSection
                else if (e.TryGetProperty("text", out var t2)) { var t = Limpio(t2.GetString()); if (t.Length > 0) pasos.Add(t); }
                else if (e.TryGetProperty("name", out var n2)) { var t = Limpio(n2.GetString()); if (t.Length > 0) pasos.Add(t); }
                break;
        }
    }

    // «PT1H30M» → 90, «PT45M» → 45, «P0DT0H20M» → 20
    public static int? Duracion(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        try { return (int)Math.Round(XmlConvert.ToTimeSpan(iso.Trim()).TotalMinutes); } catch (FormatException) { return null; }
    }

    // «4», «4 raciones», «["4","4 servings"]», 6 → 4, 4, 4, 6; sin dato, 4
    private static int Raciones(JsonElement y)
    {
        var texto = y.ValueKind switch
        {
            JsonValueKind.Number => y.GetRawText(),
            JsonValueKind.String => y.GetString() ?? "",
            JsonValueKind.Array => y.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetRawText() : x.GetString() ?? "").FirstOrDefault(x => x.Any(char.IsDigit)) ?? "",
            _ => "",
        };
        var m = Regex.Match(texto, @"\d+");
        return m.Success && int.TryParse(m.Value, out var n) && n is >= 1 and <= 50 ? n : 4;
    }
}

public static partial class Ingrediente
{
    // unidad escrita → (unidad normalizada, factor a la base: g o ml); las unidades de «cosa» (cucharada, diente…) se quedan como están
    private static readonly Dictionary<string, (string Unidad, double Factor)> Unidades = new(StringComparer.OrdinalIgnoreCase)
    {
        ["g"] = ("g", 1), ["gr"] = ("g", 1), ["grs"] = ("g", 1), ["gramo"] = ("g", 1), ["gramos"] = ("g", 1), ["kg"] = ("g", 1000), ["kilo"] = ("g", 1000), ["kilos"] = ("g", 1000),
        ["ml"] = ("ml", 1), ["cl"] = ("ml", 10), ["dl"] = ("ml", 100), ["l"] = ("ml", 1000), ["lt"] = ("ml", 1000), ["litro"] = ("ml", 1000), ["litros"] = ("ml", 1000),
        ["cucharada"] = ("cucharada", 1), ["cucharadas"] = ("cucharada", 1), ["cda"] = ("cucharada", 1), ["cdas"] = ("cucharada", 1),
        ["cucharadita"] = ("cucharadita", 1), ["cucharaditas"] = ("cucharadita", 1), ["cdta"] = ("cucharadita", 1),
        ["diente"] = ("diente", 1), ["dientes"] = ("diente", 1), ["pizca"] = ("pizca", 1), ["pizcas"] = ("pizca", 1), ["lata"] = ("lata", 1), ["latas"] = ("lata", 1),
        ["taza"] = ("taza", 1), ["tazas"] = ("taza", 1), ["vaso"] = ("vaso", 1), ["vasos"] = ("vaso", 1), ["rama"] = ("rama", 1), ["ramas"] = ("rama", 1), ["hoja"] = ("hoja", 1), ["hojas"] = ("hoja", 1),
    };
    private static readonly Dictionary<string, double> Fracciones = new() { ["½"] = 0.5, ["¼"] = 0.25, ["¾"] = 0.75, ["⅓"] = 1.0 / 3, ["⅔"] = 2.0 / 3 };

    [GeneratedRegex(@"^\s*(?<n>\d+(?:[.,]\d+)?(?:\s+\d+/\d+|/\d+)?|\d*\s*[½¼¾⅓⅔])\s*(?<resto>.*)$")]
    private static partial Regex Cantidad();

    // Pura: «200 g de harina» → (200, g, harina); «1/2 cebolla» → (0.5, "", cebolla); «sal al gusto» → (null, "", sal al gusto)
    public static IngredienteLeido Parsear(string texto)
    {
        var t = (texto ?? "").Trim();
        var m = Cantidad().Match(t);
        if (!m.Success) return new IngredienteLeido(t, null, "", t);
        var cantidad = Numero(m.Groups["n"].Value.Trim());
        var resto = m.Groups["resto"].Value.Trim();
        if (cantidad is null) return new IngredienteLeido(t, null, "", t);
        var palabras = resto.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var unidad = "";
        if (palabras.Length > 0)
        {
            // «200g» (pegado) también vale; se mira la primera palabra
            var p0 = palabras[0].TrimEnd('.', ',');
            if (Unidades.TryGetValue(p0, out var u)) { cantidad *= u.Factor; unidad = u.Unidad; resto = palabras.Length > 1 ? palabras[1] : ""; }
        }
        resto = Regex.Replace(resto, @"^(de|del|d')\s+", "", RegexOptions.IgnoreCase).Trim();
        return new IngredienteLeido(resto.Length > 0 ? resto : t, cantidad, unidad, t);
    }

    private static double? Numero(string s)
    {
        s = s.Trim();
        foreach (var (f, v) in Fracciones)
            if (s.EndsWith(f)) { var ent = s[..^f.Length].Trim(); return (ent.Length > 0 && double.TryParse(ent, CultureInfo.InvariantCulture, out var e) ? e : 0) + v; }
        var mix = Regex.Match(s, @"^(\d+)\s+(\d+)/(\d+)$");
        if (mix.Success) return int.Parse(mix.Groups[1].Value) + (double)int.Parse(mix.Groups[2].Value) / int.Parse(mix.Groups[3].Value);
        var frac = Regex.Match(s, @"^(\d+)/(\d+)$");
        if (frac.Success && int.Parse(frac.Groups[2].Value) != 0) return (double)int.Parse(frac.Groups[1].Value) / int.Parse(frac.Groups[2].Value);
        return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static string Clave(string nombre) => Regex.Replace(string.Concat(nombre.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant(), @"\s+", " ").Trim();

    // Pura: «1,5 kg» se escribe «1,5 kg», no «1500 g»; los ml pasan a litros a partir de 1000
    public static string Escribir(double cantidad, string unidad)
    {
        var c = cantidad;
        if (unidad == "g" && c >= 1000) { c /= 1000; unidad = "kg"; }
        else if (unidad == "ml" && c >= 1000) { c /= 1000; unidad = "l"; }
        var n = c % 1 == 0 ? ((long)c).ToString(CultureInfo.GetCultureInfo("es-ES")) : c.ToString(c < 10 ? "0.##" : "0.#", CultureInfo.GetCultureInfo("es-ES"));
        return unidad.Length > 0 ? $"{n} {unidad}" : n;
    }

    // Suma los ingredientes de varias recetas (cada una con su factor de raciones): mismo nombre y misma unidad se suman; lo que no tiene cantidad se queda como «al gusto»
    public static List<(string Nombre, string Cantidad)> Sumar(IEnumerable<(IngredienteLeido Ing, double Factor)> todos)
    {
        var grupos = new Dictionary<(string Clave, string Unidad), (string Nombre, double Total, bool Sin)>();
        foreach (var (ing, factor) in todos)
        {
            var k = (Clave(ing.Nombre), ing.Cantidad is null ? "?" : ing.Unidad);
            grupos.TryGetValue(k, out var g);
            grupos[k] = (g.Nombre ?? ing.Nombre, g.Total + (ing.Cantidad ?? 0) * factor, g.Sin || ing.Cantidad is null);
        }
        return grupos.OrderBy(x => x.Value.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => (x.Value.Nombre, x.Key.Unidad == "?" ? "" : Escribir(Math.Round(x.Value.Total, 2), x.Key.Unidad))).ToList();
    }
}

// Pura: ¿se puede pedir esta dirección? Solo http/https, con nombre de dominio o IP pública; nada de la red local ni de este equipo
public static class UrlsRecetas
{
    public const int MaxBytes = 2_000_000;

    public static bool Valida(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
        if (u.Scheme is not ("http" or "https") || string.IsNullOrEmpty(u.Host) || u.UserInfo.Length > 0 || (!u.IsDefaultPort && u.Port is not (8080 or 8443))) return false;
        if (IPAddress.TryParse(u.Host.Trim('[', ']'), out var ip) && EsLocal(ip)) return false;
        if (u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) || !u.Host.Contains('.') && !IPAddress.TryParse(u.Host, out _)) return false;
        uri = u; return true;
    }

    public static bool EsLocal(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) { var b = ip.GetAddressBytes(); return (b[0] & 0xFE) == 0xFC; }       // fc00::/7
        var a = ip.GetAddressBytes();
        return a[0] == 10 || a[0] == 127 || a[0] == 0 || (a[0] == 100 && a[1] is >= 64 and <= 127) || (a[0] == 169 && a[1] == 254) || (a[0] == 172 && a[1] is >= 16 and <= 31) || (a[0] == 192 && a[1] == 168) || a[0] >= 224;
    }
}
