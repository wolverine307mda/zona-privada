using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mda.Api.Seguridad;

namespace Mda.Api.Pc;

// Una cosa de casa que maneja Home Assistant: una luz, un enchufe, un ventilador, un interruptor virtual o una escena.
public record EntidadHogar(string Id, string Dominio, string Nombre, string Estado, int? Brillo);

// Cliente de Home Assistant (API REST con un token de acceso de larga duración). La dirección y el token salen de Seguridad → Claves (cifrados en la base);
// la dirección tiene que ser de tu red de casa. Solo se tocan luces, enchufes, ventiladores, interruptores virtuales y escenas: nada de cerraduras ni alarmas.
public partial class ClienteHogar(IHttpClientFactory http, IConfiguration config)
{
    public static readonly string[] Dominios = ["light", "switch", "fan", "input_boolean", "scene"];
    public static readonly Dictionary<string, string> Acciones = new() { ["encender"] = "turn_on", ["apagar"] = "turn_off", ["alternar"] = "toggle", ["activar"] = "turn_on" };
    public const int MaxEntidades = 80;

    [GeneratedRegex(@"^(light|switch|fan|input_boolean|scene)\.[a-z0-9_]{1,80}$")]
    private static partial Regex Valida();

    public static bool IdValido(string? id) => id is not null && Valida().IsMatch(id);

    private string? Url => config["HomeAssistant:Url"] is { Length: > 0 } u ? u.TrimEnd('/') : null;
    private string? Token => config["HomeAssistant:Token"] is { Length: > 0 } t ? t : null;
    public bool Configurado => Url is not null && Token is not null;

    // la respuesta de GET /api/states: se queda con lo que se puede manejar (y ordenado por dominio y nombre)
    public static List<EntidadHogar> Parsear(JsonElement estados)
    {
        var l = new List<EntidadHogar>();
        if (estados.ValueKind != JsonValueKind.Array) return l;
        foreach (var e in estados.EnumerateArray())
        {
            var id = e.TryGetProperty("entity_id", out var i) ? i.GetString() : null;
            if (!IdValido(id)) continue;
            var dominio = id![..id.IndexOf('.')];
            var atributos = e.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
            var nombre = atributos.ValueKind == JsonValueKind.Object && atributos.TryGetProperty("friendly_name", out var n) && n.GetString() is { Length: > 0 } fn ? fn : id[(id.IndexOf('.') + 1)..].Replace('_', ' ');
            int? brillo = atributos.ValueKind == JsonValueKind.Object && atributos.TryGetProperty("brightness", out var b) && b.ValueKind == JsonValueKind.Number ? (int)Math.Round(b.GetInt32() * 100.0 / 255) : null;
            l.Add(new EntidadHogar(id, dominio, nombre.Length > 60 ? nombre[..60] : nombre, e.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "", brillo));
        }
        return l.OrderBy(x => Array.IndexOf(Dominios, x.Dominio)).ThenBy(x => x.Nombre, StringComparer.CurrentCultureIgnoreCase).Take(MaxEntidades).ToList();
    }

    private HttpRequestMessage Peticion(HttpMethod metodo, string ruta, object? cuerpo = null)
    {
        var r = new HttpRequestMessage(metodo, Url + ruta);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (cuerpo is not null) r.Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
        return r;
    }

    public async Task<List<EntidadHogar>> Listar(CancellationToken ct)
    {
        using var r = await http.CreateClient("hogar").SendAsync(Peticion(HttpMethod.Get, "/api/states"), ct);
        r.EnsureSuccessStatusCode();
        return Parsear(await r.Content.ReadFromJsonAsync<JsonElement>(ct));
    }

    // «turn_on» / «turn_off» / «toggle» sobre una entidad; una escena solo se activa
    public async Task<bool> Llamar(string entidad, string accion, CancellationToken ct)
    {
        if (!IdValido(entidad) || !Acciones.TryGetValue(accion, out var servicio)) return false;
        var dominio = entidad[..entidad.IndexOf('.')];
        if (dominio == "scene" && accion != "activar") return false;
        using var r = await http.CreateClient("hogar").SendAsync(Peticion(HttpMethod.Post, $"/api/services/{dominio}/{servicio}", new { entity_id = entidad }), ct);
        return r.IsSuccessStatusCode;
    }
}

// Lo que la zona privada deja que se configure: solo una dirección de la red de casa (http o https), sin usuario ni rutas raras
public static class Hogar
{
    public static async Task<string?> ValidarUrl(string? url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || u.UserInfo.Length > 0 || u.Host.Length == 0)
            return "Escribe la dirección completa, como http://homeassistant:8123 o http://192.168.1.20:8123.";
        return await RedCasa.ValidarHost(u.Host, ct) is not null ? "Solo direcciones de tu red de casa (192.168.x.x, 10.x.x.x, 172.16-31.x.x), de Tailscale o de un contenedor." : null;
    }
}
