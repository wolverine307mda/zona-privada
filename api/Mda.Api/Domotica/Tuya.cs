using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mda.Api.Domotica;

// Lo que se guarda (cifrado) en la configuración de un aparato de Tuya
public record ConfigTuya(string? DeviceId, string? InfraredId = null, string? RemoteId = null)
{
    public static ConfigTuya Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ConfigTuya((string?)null);
        try { return JsonSerializer.Deserialize<ConfigTuya>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new ConfigTuya((string?)null); }
        catch (JsonException) { return new ConfigTuya((string?)null); }
    }
    public string Json() => JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

public record DispositivoTuya(string Id, string Nombre, string Categoria, bool EnLinea, string? TipoSugerido);

// La firma de la API en la nube de Tuya (HMAC-SHA256 en mayúsculas de «client_id + token + t + nonce + cadena»)
public static class FirmaTuya
{
    public static string Hash(string cuerpo) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cuerpo)));

    public static string Firmar(string clientId, string? token, string t, string nonce, string metodo, string rutaYConsulta, string cuerpo, string secreto)
    {
        var aFirmar = $"{metodo.ToUpperInvariant()}\n{Hash(cuerpo)}\n\n{rutaYConsulta}";
        var texto = clientId + (token ?? "") + t + nonce + aFirmar;
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), Encoding.UTF8.GetBytes(texto)));
    }
}

// Qué dicen los códigos de Tuya y cómo se pide cada cosa; todo puro para poder probarlo sin aparato
public static class TraduccionTuya
{
    public static readonly Dictionary<string, string> TiposPorCategoria = new()
    {
        ["wsdcg"] = "sensor", ["mcs"] = "puerta", ["dj"] = "luz", ["dd"] = "luz", ["xdd"] = "luz", ["fwd"] = "luz", ["dc"] = "luz",
        ["cz"] = "enchufe", ["pc"] = "enchufe", ["wnykq"] = "ir", ["hwktwkq"] = "ir", ["infrared_ac"] = "clima",
    };

    // #rrggbb ↔ HSV de Tuya (h 0–360, s y v 0–1000)
    public static (int H, int S, int V) HexAHsv(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0; var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0; var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return ((int)Math.Round(h) % 360, (int)Math.Round((max == 0 ? 0 : d / max) * 1000), (int)Math.Round(max * 1000));
    }

    public static string HsvAHex(int h, int s, int v)
    {
        double S = Math.Clamp(s, 0, 1000) / 1000.0, V = Math.Clamp(v, 0, 1000) / 1000.0, c = V * S, hh = (((h % 360) + 360) % 360) / 60.0;
        var x = c * (1 - Math.Abs(hh % 2 - 1)); var m = V - c;
        var (r, g, b) = hh switch { < 1 => (c, x, 0.0), < 2 => (x, c, 0.0), < 3 => (0.0, c, x), < 4 => (0.0, x, c), < 5 => (x, 0.0, c), _ => (c, 0.0, x) };
        string Byte(double n) => ((int)Math.Round((n + m) * 255)).ToString("x2");
        return "#" + Byte(r) + Byte(g) + Byte(b);
    }

    // El estado que devuelve Tuya (lista de {code, value}) como los datos de nuestro catálogo
    public static Dictionary<string, string> Traducir(string tipo, IEnumerable<(string Code, JsonElement Value)> estado)
    {
        var r = new Dictionary<string, string>();
        string Num(double n, string f = "0.##") => n.ToString(f, CultureInfo.InvariantCulture);
        foreach (var (code, v) in estado)
        {
            switch (tipo, code)
            {
                case ("sensor", "va_temperature" or "temp_current" or "temperature") when v.ValueKind == JsonValueKind.Number: r["temperatura"] = Num(v.GetDouble() / 10, "0.0"); break;
                case ("sensor", "va_humidity" or "humidity_value" or "humidity") when v.ValueKind == JsonValueKind.Number: r["humedad"] = Num(v.GetDouble(), "0"); break;
                case ("puerta", "doorcontact_state"): r["abierto"] = v.ValueKind == JsonValueKind.True ? "true" : "false"; break;
                case ("luz", "switch_led" or "switch_1" or "switch"): r["encendido"] = v.ValueKind == JsonValueKind.True ? "true" : "false"; break;
                case ("luz", "bright_value_v2") when v.ValueKind == JsonValueKind.Number: r["brillo"] = Num(Math.Clamp(Math.Round(v.GetDouble() / 10), 1, 100), "0"); break;
                case ("luz", "colour_data_v2") when v.ValueKind == JsonValueKind.String:
                    try { using var d = JsonDocument.Parse(v.GetString()!); r["color"] = HsvAHex(d.RootElement.GetProperty("h").GetInt32(), d.RootElement.GetProperty("s").GetInt32(), d.RootElement.GetProperty("v").GetInt32()); }
                    catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { /* color con otro formato */ }
                    break;
                case ("enchufe", "switch_1" or "switch"): r["encendido"] = v.ValueKind == JsonValueKind.True ? "true" : "false"; break;
                case ("enchufe", "cur_power") when v.ValueKind == JsonValueKind.Number: r["potencia"] = Num(v.GetDouble() / 10, "0"); break;
            }
        }
        return r;
    }

    // La orden de nuestro catálogo como comandos de Tuya (lista de {code, value}); null si ese aparato no se maneja así
    public static List<Dictionary<string, object>>? Comandos(string tipo, string clave, string valor)
    {
        static Dictionary<string, object> C(string code, object v) => new() { ["code"] = code, ["value"] = v };
        switch (tipo, clave)
        {
            case ("luz", "encendido"): return [C("switch_led", valor == "true")];
            case ("luz", "brillo"): return [C("bright_value_v2", (int)Math.Clamp(Math.Round(double.Parse(valor, CultureInfo.InvariantCulture) * 10), 10, 1000))];
            case ("luz", "color"):
                var (h, s, v) = HexAHsv(valor);
                return [C("work_mode", "colour"), C("colour_data_v2", JsonSerializer.Serialize(new { h, s, v }))];
            case ("enchufe", "encendido"): return [C("switch_1", valor == "true")];
            default: return null;
        }
    }

    // Aire por infrarrojos (emisor IR de Tuya): modo 0 frío, 1 calor, 2 automático; eco = automático a 26 °
    public static Dictionary<string, object> OrdenAire(IReadOnlyDictionary<string, string> estado)
    {
        var modo = estado.GetValueOrDefault("modo", "auto");
        var temp = double.TryParse(estado.GetValueOrDefault("objetivo", "24"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? (int)Math.Round(t) : 24;
        return new()
        {
            ["power"] = estado.GetValueOrDefault("encendido") == "true" ? 1 : 0,
            ["mode"] = modo switch { "frio" => 0, "calor" => 1, _ => 2 },
            ["temp"] = modo == "eco" ? Math.Max(temp, 26) : temp,
            ["wind"] = 0,
        };
    }
}

// Habla con la API en la nube de Tuya. Las claves (Access ID, Access secret y región) salen de Seguridad → Claves.
public sealed class ClienteTuya(IHttpClientFactory http, IConfiguration config, TimeProvider reloj)
{
    private string? Id => config["Tuya:AccessId"] is { Length: > 0 } a ? a : null;
    private string? Secreto => config["Tuya:AccessSecret"] is { Length: > 0 } s ? s : null;
    public bool Configurado => Id is not null && Secreto is not null;
    private string Base => (config["Tuya:Region"]?.Trim().ToLowerInvariant()) switch
    {
        "us" => "https://openapi.tuyaus.com", "cn" => "https://openapi.tuyacn.com", "in" => "https://openapi.tuyain.com", _ => "https://openapi.tuyaeu.com",
    };

    private string? _token; private DateTimeOffset _caduca;
    private readonly SemaphoreSlim _cerrojo = new(1, 1);

    private async Task<JsonElement> Enviar(HttpMethod metodo, string ruta, string? token, object? cuerpo, CancellationToken ct)
    {
        var t = reloj.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var json = cuerpo is null ? "" : JsonSerializer.Serialize(cuerpo);
        var peticion = new HttpRequestMessage(metodo, Base + ruta);
        peticion.Headers.Add("client_id", Id);
        peticion.Headers.Add("sign", FirmaTuya.Firmar(Id!, token, t, nonce, metodo.Method, ruta, json, Secreto!));
        peticion.Headers.Add("t", t);
        peticion.Headers.Add("sign_method", "HMAC-SHA256");
        peticion.Headers.Add("nonce", nonce);
        if (token is not null) peticion.Headers.Add("access_token", token);
        if (cuerpo is not null) peticion.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var r = await http.CreateClient("conexiones").SendAsync(peticion, ct);
        var doc = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!doc.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException("Tuya: " + (doc.TryGetProperty("msg", out var m) ? m.GetString() : r.StatusCode.ToString()));
        return doc.TryGetProperty("result", out var res) ? res.Clone() : default;
    }

    private async Task<string> Token(CancellationToken ct)
    {
        await _cerrojo.WaitAsync(ct);
        try
        {
            if (_token is not null && reloj.GetUtcNow() < _caduca) return _token;
            var r = await Enviar(HttpMethod.Get, "/v1.0/token?grant_type=1", null, null, ct);
            _token = r.GetProperty("access_token").GetString()!;
            _caduca = reloj.GetUtcNow().AddSeconds(Math.Max(60, r.GetProperty("expire_time").GetInt32() - 120));
            return _token;
        }
        finally { _cerrojo.Release(); }
    }

    public async Task<JsonElement> Llamar(HttpMethod metodo, string ruta, object? cuerpo, CancellationToken ct) =>
        Configurado ? await Enviar(metodo, ruta, await Token(ct), cuerpo, ct) : throw new InvalidOperationException("Tuya sin configurar (Seguridad → Claves).");

    // Los dispositivos de tu cuenta (para elegirlos al añadir un aparato)
    public async Task<List<DispositivoTuya>> Listar(CancellationToken ct)
    {
        var r = await Llamar(HttpMethod.Get, "/v1.3/iot-03/devices?page_size=50", null, ct);
        var lista = r.ValueKind == JsonValueKind.Array ? r : r.ValueKind == JsonValueKind.Object && r.TryGetProperty("list", out var l) ? l : default;
        var salida = new List<DispositivoTuya>();
        if (lista.ValueKind != JsonValueKind.Array) return salida;
        foreach (var d in lista.EnumerateArray())
        {
            var cat = d.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
            salida.Add(new DispositivoTuya(d.GetProperty("id").GetString() ?? "", d.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", cat,
                d.TryGetProperty("online", out var o) && o.ValueKind == JsonValueKind.True, TraduccionTuya.TiposPorCategoria.GetValueOrDefault(cat)));
        }
        return salida;
    }
}

public sealed class DriverTuya(ClienteTuya cliente) : IDriverCasa
{
    public string Id => "tuya";
    // el aire por infrarrojos no devuelve su estado: se recuerda el último que se mandó
    private readonly ConcurrentDictionary<int, Dictionary<string, string>> _aire = new();

    public async Task<IReadOnlyDictionary<string, string>> Leer(Aparato a, CancellationToken ct)
    {
        if (!cliente.Configurado) return new Dictionary<string, string>();
        if (a.Tipo == "clima") { var e = _aire.GetOrAdd(a.Id, _ => new() { ["encendido"] = "false", ["objetivo"] = "24", ["modo"] = "auto" }); lock (e) return new Dictionary<string, string>(e); }
        var cfg = ConfigTuya.Leer(a.Config);
        if (string.IsNullOrEmpty(cfg.DeviceId)) return new Dictionary<string, string>();
        var r = await cliente.Llamar(HttpMethod.Get, $"/v1.0/devices/{Uri.EscapeDataString(cfg.DeviceId)}/status", null, ct);
        var estado = r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Where(x => x.TryGetProperty("code", out _) && x.TryGetProperty("value", out _)).Select(x => (x.GetProperty("code").GetString() ?? "", x.GetProperty("value"))).ToList()
            : [];
        return TraduccionTuya.Traducir(a.Tipo, estado);
    }

    public async Task<(bool Ok, string Mensaje)> Ejecutar(Aparato a, Campo campo, string valor, CancellationToken ct)
    {
        if (!cliente.Configurado) return (false, "Falta configurar Tuya en Seguridad → Claves.");
        var cfg = ConfigTuya.Leer(a.Config);
        if (a.Tipo == "clima")
        {
            if (string.IsNullOrEmpty(cfg.InfraredId) || string.IsNullOrEmpty(cfg.RemoteId)) return (false, "Falta el emisor IR y el aire de este aparato.");
            var e = _aire.GetOrAdd(a.Id, _ => new() { ["encendido"] = "false", ["objetivo"] = "24", ["modo"] = "auto" });
            Dictionary<string, string> copia;
            lock (e) { e[campo.Clave] = valor; copia = new(e); }
            await cliente.Llamar(HttpMethod.Post, $"/v2.0/infrareds/{Uri.EscapeDataString(cfg.InfraredId)}/air-conditioners/{Uri.EscapeDataString(cfg.RemoteId)}/scenes/command", TraduccionTuya.OrdenAire(copia), ct);
            return (true, "Orden enviada al aire.");
        }
        if (string.IsNullOrEmpty(cfg.DeviceId)) return (false, "Falta el identificador del aparato de Tuya.");
        if (TraduccionTuya.Comandos(a.Tipo, campo.Clave, valor) is not { } comandos) return (false, "Esa orden no se puede mandar a este aparato.");
        await cliente.Llamar(HttpMethod.Post, $"/v1.0/devices/{Uri.EscapeDataString(cfg.DeviceId)}/commands", new { commands = comandos }, ct);
        return (true, "Hecho.");
    }
}
