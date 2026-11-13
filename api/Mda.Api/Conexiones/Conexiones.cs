using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Mda.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Conexiones;

// Una cuenta de fuera enlazada con OAuth (Spotify, Google Calendar). Los tokens se guardan cifrados.
public class Conexion
{
    public string Proveedor { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string? AccessToken { get; set; }
    public DateTimeOffset? Caduca { get; set; }
    public string? Cuenta { get; set; }
    public DateTimeOffset Conectado { get; set; } = DateTimeOffset.UtcNow;
}

public record Proveedor(string Id, string Nombre, string Clave, string Autorizar, string Token, string Permisos, string[] Extra, string Destino);

// OAuth 2 (código de autorización) en el servidor: el secreto del cliente y los tokens nunca llegan al navegador.
// El «state» se guarda en memoria al empezar (con sesión) y se comprueba a la vuelta, que llega sin cookie
// porque la cookie de sesión es SameSite=Strict y la vuelta viene de otro sitio.
public class ServicioConexiones(IConfiguration config, IServiceScopeFactory scopes, IHttpClientFactory http, IMemoryCache cache,
    IDataProtectionProvider proteccion, ILogger<ServicioConexiones> log)
{
    public static readonly Proveedor Spotify = new("spotify", "Spotify", "Spotify", "https://accounts.spotify.com/authorize", "https://accounts.spotify.com/api/token",
        "user-read-currently-playing user-read-playback-state user-modify-playback-state user-read-recently-played user-top-read playlist-read-private playlist-read-collaborative", [], "/musica");
    public static readonly Proveedor Google = new("google", "Google Calendar", "Google", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token",
        "https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.calendarlist.readonly", ["access_type=offline", "prompt=select_account%20consent"], "/agenda");
    public static readonly Proveedor[] Todos = [Spotify, Google];
    // Google admite varias cuentas: la primera se guarda como «google» (como siempre) y las demás como «google-a1b2c3»
    public static Proveedor? Por(string id) => Todos.FirstOrDefault(p => p.Id == id || (p == Google && id.StartsWith("google-", StringComparison.Ordinal) && id.Length is > 7 and <= 24));
    public static bool EsDe(Proveedor p, string clave) => clave == p.Id || (p == Google && clave.StartsWith("google-", StringComparison.Ordinal));

    private readonly IDataProtector cifra = proteccion.CreateProtector("conexiones");
    private readonly SemaphoreSlim cerrojo = new(1, 1);

    public bool Configurado(Proveedor p) => !string.IsNullOrWhiteSpace(config[$"{p.Clave}:ClientId"]) && !string.IsNullOrWhiteSpace(config[$"{p.Clave}:ClientSecret"]);

    // dirección de vuelta que hay que dar de alta en el proveedor (con App:UrlPublica, la de Internet)
    public string Vuelta(Proveedor p, HttpRequest req) =>
        $"{(config["App:UrlPublica"] is { Length: > 0 } u ? u.TrimEnd('/') : $"{req.Scheme}://{req.Host}")}/api/conexiones/{p.Id}/vuelta";

    public string UrlConectar(Proveedor p, HttpRequest req)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        cache.Set($"oauth:{state}", p.Id, TimeSpan.FromMinutes(10));
        var q = new Dictionary<string, string>
        {
            ["client_id"] = config[$"{p.Clave}:ClientId"]!, ["response_type"] = "code", ["redirect_uri"] = Vuelta(p, req),
            ["scope"] = p.Permisos, ["state"] = state,
        };
        return $"{p.Autorizar}?{string.Join('&', q.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}").Concat(p.Extra))}";
    }

    // El state vale una sola vez y solo para el proveedor con el que se empezó
    public bool StateValido(Proveedor p, string? state)
    {
        if (string.IsNullOrEmpty(state) || !cache.TryGetValue($"oauth:{state}", out string? id)) return false;
        cache.Remove($"oauth:{state}");
        return id == p.Id;
    }

    public async Task<bool> Completar(Proveedor p, string code, HttpRequest req, CancellationToken ct)
    {
        var t = await PedirToken(p, new() { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Vuelta(p, req) }, ct);
        if (t?.Refresh is null) return false;
        var cuenta = await NombreCuenta(p, t.Value.Access, ct);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existentes = (await db.Conexiones.ToListAsync(ct)).Where(x => EsDe(p, x.Proveedor)).ToList();
        // volver a enlazar la misma cuenta de Google la renueva; Spotify solo tiene una; otra cuenta de Google se añade
        var c = existentes.FirstOrDefault(x => p != Google || (cuenta is not null && x.Cuenta == cuenta));
        if (c is null)
        {
            var clave = existentes.Count == 0 ? p.Id : $"{p.Id}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}";
            c = db.Conexiones.Add(new Conexion { Proveedor = clave }).Entity;
        }
        c.RefreshToken = cifra.Protect(t.Value.Refresh);
        c.AccessToken = cifra.Protect(t.Value.Access);
        c.Caduca = DateTimeOffset.UtcNow.AddSeconds(t.Value.Segundos - 60);
        c.Conectado = DateTimeOffset.UtcNow;
        c.Cuenta = cuenta;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<string?> NombreCuenta(Proveedor p, string access, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, p == Spotify ? "https://api.spotify.com/v1/me" : "https://www.googleapis.com/calendar/v3/users/me/calendarList/primary");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var res = await http.CreateClient("conexiones").SendAsync(req, ct);
            var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (p == Spotify) return j.TryGetProperty("display_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : j.GetProperty("id").GetString();
            return j.GetProperty("id").GetString();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            log.LogWarning(e, "No se pudo leer la cuenta de {Proveedor}", p.Nombre);
            return null;
        }
    }

    // Todas las cuentas enlazadas de un proveedor, la más antigua primero (la primera de Google es la de siempre)
    public async Task<IReadOnlyList<Conexion>> Listar(Proveedor p, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var todas = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Conexiones.AsNoTracking().ToListAsync(ct);
        return todas.Where(c => EsDe(p, c.Proveedor)).OrderBy(c => c.Conectado).ToList();
    }

    // La primera cuenta del proveedor, o la de esa clave si se da
    public async Task<Conexion?> Leer(Proveedor p, CancellationToken ct = default, string? clave = null) =>
        (await Listar(p, ct)).FirstOrDefault(c => clave is null || c.Proveedor == clave);

    public async Task Desconectar(Proveedor p, CancellationToken ct = default, string? clave = null)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Conexiones.FindAsync([clave ?? p.Id], ct) is { } c) { db.Conexiones.Remove(c); await db.SaveChangesAsync(ct); }
    }

    // Un token de acceso válido (renovándolo si ha caducado), o null si no hay cuenta enlazada
    public async Task<string?> Token(Proveedor p, CancellationToken ct = default, string? clave = null)
    {
        await cerrojo.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Conexiones.FindAsync([clave ?? p.Id], ct);
            if (c is null) return null;
            try
            {
                if (c.AccessToken is not null && c.Caduca > DateTimeOffset.UtcNow) return cifra.Unprotect(c.AccessToken);
            }
            catch (System.Security.Cryptography.CryptographicException e)
            {
                log.LogError(e, "El token de {Proveedor} no se puede descifrar (¿se perdió el volumen «claves»?): hay que volver a conectar la cuenta", p.Nombre);
                return null;
            }
            string refresco;
            try { refresco = cifra.Unprotect(c.RefreshToken); }
            catch (System.Security.Cryptography.CryptographicException e)
            {
                log.LogError(e, "El token de {Proveedor} no se puede descifrar (¿se perdió el volumen «claves»?): hay que volver a conectar la cuenta", p.Nombre);
                return null;
            }
            var t = await PedirToken(p, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresco }, ct);
            if (t is null) return null;
            c.AccessToken = cifra.Protect(t.Value.Access);
            c.Caduca = DateTimeOffset.UtcNow.AddSeconds(t.Value.Segundos - 60);
            if (t.Value.Refresh is { } r) c.RefreshToken = cifra.Protect(r);
            await db.SaveChangesAsync(ct);
            return t.Value.Access;
        }
        finally { cerrojo.Release(); }
    }

    private async Task<(string Access, string? Refresh, int Segundos)?> PedirToken(Proveedor p, Dictionary<string, string> datos, CancellationToken ct)
    {
        datos["client_id"] = config[$"{p.Clave}:ClientId"]!;
        datos["client_secret"] = config[$"{p.Clave}:ClientSecret"]!;
        try
        {
            using var res = await http.CreateClient("conexiones").PostAsync(p.Token, new FormUrlEncodedContent(datos), ct);
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("{Proveedor} no dio el token: {Codigo}", p.Nombre, (int)res.StatusCode);
                return null;
            }
            var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            return (j.GetProperty("access_token").GetString()!,
                j.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
                j.TryGetProperty("expires_in", out var s) ? s.GetInt32() : 3600);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException)
        {
            log.LogWarning(e, "Error pidiendo el token a {Proveedor}", p.Nombre);
            return null;
        }
    }

    // Una petición a la API del proveedor con el token; null si no hay cuenta enlazada
    public async Task<HttpResponseMessage?> Llamar(Proveedor p, HttpMethod metodo, string url, object? cuerpo = null, CancellationToken ct = default, string? clave = null)
    {
        var token = await Token(p, ct, clave);
        if (token is null) return null;
        using var req = new HttpRequestMessage(metodo, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (cuerpo is not null) req.Content = JsonContent.Create(cuerpo);
        try { return await http.CreateClient("conexiones").SendAsync(req, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // sin red o proveedor caído: igual que «no conectado» para el que llama (la agenda sigue cargando sin Google)
            log.LogWarning("{Proveedor} no responde ({Motivo})", p.Nombre, e.GetType().Name);
            return null;
        }
    }
}

public static class ConexionesEndpoints
{
    public static void MapConexiones(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/conexiones");

        g.MapGet("/", async (ServicioConexiones s, HttpRequest req) =>
        {
            var lista = new List<object>();
            foreach (var p in ServicioConexiones.Todos)
            {
                var cuentas = await s.Listar(p);
                var c = cuentas.FirstOrDefault();
                lista.Add(new
                {
                    id = p.Id, nombre = p.Nombre, configurado = s.Configurado(p), conectado = c is not null, cuenta = c?.Cuenta, desde = c?.Conectado, vuelta = s.Vuelta(p, req),
                    cuentas = cuentas.Select(x => new { clave = x.Proveedor, cuenta = x.Cuenta, desde = x.Conectado }),
                });
            }
            return Results.Ok(lista);
        }).RequireAuthorization();

        // empieza el enlace: el front navega aquí y la API redirige a la página de permisos del proveedor
        g.MapGet("/{id}/conectar", (string id, ServicioConexiones s, HttpRequest req) =>
            ServicioConexiones.Por(id) is not { } p ? Results.NotFound()
            : !s.Configurado(p) ? Results.Redirect($"{p.Destino}?conexion=sin-configurar")
            : Results.Redirect(s.UrlConectar(p, req))).RequireAuthorization();

        // vuelta del proveedor: sin cookie (llega de otro sitio), la protege el state de un solo uso
        g.MapGet("/{id}/vuelta", async (string id, string? code, string? state, string? error, ServicioConexiones s, HttpRequest req, CancellationToken ct) =>
        {
            if (ServicioConexiones.Por(id) is not { } p) return Results.NotFound();
            if (!s.StateValido(p, state)) return Results.Redirect($"{p.Destino}?conexion=caducada");
            if (error is not null || string.IsNullOrEmpty(code)) return Results.Redirect($"{p.Destino}?conexion=cancelada");
            return Results.Redirect($"{p.Destino}?conexion={(await s.Completar(p, code, req, ct) ? "ok" : "error")}");
        }).AllowAnonymous();

        g.MapDelete("/{id}", async (string id, ServicioConexiones s) =>
        {
            if (ServicioConexiones.Por(id) is not { } p) return Results.NotFound();
            await s.Desconectar(p, clave: id);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
