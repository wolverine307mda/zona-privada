using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Mda.Api.Pc;

public record DominioAdGuard(string Dominio, int Veces);
public record ResumenAdGuard(int Consultas, int Bloqueadas, double PorcentajeBloqueado, int? MsMedio, bool Protegido, List<DominioAdGuard> TopBloqueados, List<DominioAdGuard> TopConsultados);

// Cliente de AdGuard Home (API REST con usuario y contraseña). La dirección y las claves salen de Seguridad → Claves; la dirección tiene que ser de tu red.
// Enseña cuántas consultas DNS ha bloqueado y deja pausar la protección un rato (por si una web que necesitas no carga).
public class ClienteAdGuard(IHttpClientFactory http, IConfiguration config)
{
    public const int PausaMaximaMinutos = 24 * 60;

    private string? Url => config["AdGuard:Url"] is { Length: > 0 } u ? u.TrimEnd('/') : null;
    private string? Usuario => config["AdGuard:Usuario"] is { Length: > 0 } u ? u : null;
    private string? Clave => config["AdGuard:Clave"] is { Length: > 0 } c ? c : null;
    public bool Configurado => Url is not null && Usuario is not null && Clave is not null;

    // «top_queried_domains»: [{"google.com": 120}, {"apple.com": 80}] → una lista de (dominio, veces)
    private static List<DominioAdGuard> Dominios(JsonElement e, string campo)
    {
        var l = new List<DominioAdGuard>();
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(campo, out var a) || a.ValueKind != JsonValueKind.Array) return l;
        foreach (var item in a.EnumerateArray().Take(5))
            foreach (var p in item.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number) l.Add(new DominioAdGuard(p.Name.Length > 80 ? p.Name[..80] : p.Name, p.Value.GetInt32()));
        return l;
    }

    public static ResumenAdGuard Parsear(JsonElement estadisticas, JsonElement estado)
    {
        int Num(string campo) => estadisticas.ValueKind == JsonValueKind.Object && estadisticas.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Min(int.MaxValue, v.GetInt64()) : 0;
        var consultas = Num("num_dns_queries"); var bloqueadas = Num("num_blocked_filtering");
        // «avg_processing_time» viene en segundos (0,012 = 12 ms)
        int? ms = estadisticas.ValueKind == JsonValueKind.Object && estadisticas.TryGetProperty("avg_processing_time", out var t) && t.ValueKind == JsonValueKind.Number ? (int)Math.Round(t.GetDouble() * 1000) : null;
        var protegido = estado.ValueKind == JsonValueKind.Object && estado.TryGetProperty("protection_enabled", out var p) && p.ValueKind == JsonValueKind.True;
        return new ResumenAdGuard(consultas, bloqueadas, consultas > 0 ? Math.Round(100.0 * bloqueadas / consultas, 1) : 0, ms, protegido, Dominios(estadisticas, "top_blocked_domains"), Dominios(estadisticas, "top_queried_domains"));
    }

    private HttpRequestMessage Peticion(HttpMethod metodo, string ruta, object? cuerpo = null)
    {
        var r = new HttpRequestMessage(metodo, Url + ruta);
        r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Usuario}:{Clave}")));
        if (cuerpo is not null) r.Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
        return r;
    }

    public async Task<ResumenAdGuard> Resumen(CancellationToken ct)
    {
        var cliente = http.CreateClient("hogar");
        using var s = await cliente.SendAsync(Peticion(HttpMethod.Get, "/control/stats"), ct);
        s.EnsureSuccessStatusCode();
        using var e = await cliente.SendAsync(Peticion(HttpMethod.Get, "/control/status"), ct);
        e.EnsureSuccessStatusCode();
        return Parsear(await s.Content.ReadFromJsonAsync<JsonElement>(ct), await e.Content.ReadFromJsonAsync<JsonElement>(ct));
    }

    // activar la protección, o pausarla durante unos minutos (AdGuard la reactiva solo al acabar)
    public async Task<bool> Proteccion(bool activa, int? minutos, CancellationToken ct)
    {
        object cuerpo = activa || minutos is null ? new { enabled = activa } : new { enabled = false, duration = minutos.Value * 60_000L };
        using var r = await http.CreateClient("hogar").SendAsync(Peticion(HttpMethod.Post, "/control/protection", cuerpo), ct);
        return r.IsSuccessStatusCode;
    }
}
