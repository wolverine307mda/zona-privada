using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

// Una clave de un servicio (token de Telegram, apikey de WhatsApp, secreto de Spotify…). Se guarda cifrada con la clave maestra.
public class Secreto
{
    public string Servicio { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Valor { get; set; } = "";
    public DateTimeOffset Actualizado { get; set; } = DateTimeOffset.UtcNow;
}

public record CampoSecreto(string Nombre, string Titulo, string Config);
public record ServicioSecreto(string Id, string Titulo, string Resumen, CampoSecreto[] Campos);

// La configuración que lee el resto del código (config["Telegram:Token"]…) sale primero de aquí: lo que se guarda en Seguridad → Claves
// gana al .env, y el código no cambia. Es la última fuente de la cadena, así que tiene la prioridad más alta.
public sealed class SecretosFuente : Microsoft.Extensions.Configuration.IConfigurationSource
{
    public SecretosProveedor Proveedor { get; } = new();
    public Microsoft.Extensions.Configuration.IConfigurationProvider Build(Microsoft.Extensions.Configuration.IConfigurationBuilder builder) => Proveedor;
}

public sealed class SecretosProveedor : Microsoft.Extensions.Configuration.ConfigurationProvider
{
    public void Poner(Dictionary<string, string?> datos)
    {
        Data = new Dictionary<string, string?>(datos, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}

public class Secretos(SecretosFuente fuente, IConfiguration config, ILogger<Secretos> log)
{
    public static readonly ServicioSecreto[] Catalogo =
    [
        new("telegram", "Telegram", "El bot que te avisa y al que le escribes", [new("Token", "Token del bot", "Telegram:Token")]),
        new("whatsapp", "WhatsApp (CallMeBot)", "Avisos por WhatsApp", [new("Telefono", "Teléfono", "WhatsApp:Telefono"), new("ApiKey", "Apikey", "WhatsApp:ApiKey")]),
        new("correo", "Correo", "Avisos por email (SMTP)",
        [
            new("Servidor", "Servidor SMTP", "Correo:Servidor"), new("Puerto", "Puerto", "Correo:Puerto"), new("Usuario", "Usuario", "Correo:Usuario"),
            new("Clave", "Contraseña", "Correo:Clave"), new("Para", "Dirección donde recibes", "Correo:Para"), new("De", "Remitente", "Correo:De"),
        ]),
        new("spotify", "Spotify", "Música y lo que suena", [new("ClientId", "Client ID", "Spotify:ClientId"), new("ClientSecret", "Client secret", "Spotify:ClientSecret")]),
        new("google", "Google", "Calendario y cuenta", [new("ClientId", "Client ID", "Google:ClientId"), new("ClientSecret", "Client secret", "Google:ClientSecret")]),
        new("homeassistant", "Home Assistant", "Luces, enchufes y escenas de casa", [new("Url", "Dirección (http://homeassistant:8123)", "HomeAssistant:Url"), new("Token", "Token de acceso de larga duración", "HomeAssistant:Token")]),
        new("adguard", "AdGuard Home", "Bloqueo de anuncios de la red", [new("Url", "Dirección (http://adguard:3000)", "AdGuard:Url"), new("Usuario", "Usuario", "AdGuard:Usuario"), new("Clave", "Contraseña", "AdGuard:Clave")]),
        new("vigilancia", "Latido externo", "Healthchecks.io: te avisa si el PC deja de dar señales", [new("Url", "URL del latido", "Healthcheck:Url")]),
    ];

    public static CampoSecreto? Campo(string servicio, string nombre) =>
        Catalogo.FirstOrDefault(s => s.Id == servicio)?.Campos.FirstOrDefault(c => c.Nombre == nombre);

    // Al arrancar (después de las migraciones): lo que haya en el .env pasa a la base, cifrado, una sola vez por campo.
    // Con la base en memoria (pruebas y desarrollo sin Docker) no se importa: no se quiere mezclar claves entre ejecuciones.
    public async Task Iniciar(AppDbContext db, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            var hay = (await db.Secretos.AsNoTracking().Select(s => new { s.Servicio, s.Nombre }).ToListAsync(ct)).Select(s => (s.Servicio, s.Nombre)).ToHashSet();
            var n = 0;
            foreach (var s in Catalogo)
                foreach (var c in s.Campos)
                {
                    if (hay.Contains((s.Id, c.Nombre)) || string.IsNullOrWhiteSpace(config[c.Config])) continue;
                    db.Secretos.Add(new Secreto { Servicio = s.Id, Nombre = c.Nombre, Valor = config[c.Config]!.Trim() });
                    n++;
                }
            if (n > 0) { await db.SaveChangesAsync(ct); log.LogInformation("Claves de servicios: {N} importadas del .env a la base (ya se pueden quitar del .env)", n); }
        }
        await Recargar(db, ct);
    }

    public async Task Recargar(AppDbContext db, CancellationToken ct = default)
    {
        var datos = new Dictionary<string, string?>();
        foreach (var s in await db.Secretos.AsNoTracking().ToListAsync(ct))
            if (Campo(s.Servicio, s.Nombre) is { } c) datos[c.Config] = s.Valor; // una clave quitada queda en "" y anula la del .env
        fuente.Proveedor.Poner(datos);
    }

    public async Task Poner(AppDbContext db, string servicio, string nombre, string valor, TimeProvider reloj, CancellationToken ct = default)
    {
        var s = await db.Secretos.FindAsync([servicio, nombre], ct);
        if (s is null) db.Secretos.Add(s = new Secreto { Servicio = servicio, Nombre = nombre });
        s.Valor = valor; s.Actualizado = reloj.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await Recargar(db, ct);
        log.LogInformation("Clave de servicio actualizada: {Servicio}/{Nombre}", servicio, nombre);
    }
}

public record ValorEntrada(string? Valor);

public static class SecretosEndpoints
{
    public static void MapSecretos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/seguridad/claves").RequireAuthorization();

        // el estado nunca lleva los valores: solo si hay clave y cuándo se cambió
        g.MapGet("/", async (AppDbContext db, HttpContext ctx, TimeProvider reloj) =>
        {
            var guardados = (await db.Secretos.AsNoTracking().ToListAsync()).ToDictionary(s => (s.Servicio, s.Nombre));
            return Results.Ok(new
            {
                reautenticado = await Reautenticar.Vigente(ctx, db, reloj),
                servicios = Secretos.Catalogo.Select(s => new
                {
                    s.Id, s.Titulo, s.Resumen,
                    campos = s.Campos.Select(c => new
                    {
                        c.Nombre, c.Titulo,
                        configurada = guardados.TryGetValue((s.Id, c.Nombre), out var x) && x.Valor.Length > 0,
                        actualizado = guardados.TryGetValue((s.Id, c.Nombre), out var y) ? y.Actualizado : (DateTimeOffset?)null,
                    }),
                }),
            });
        });

        // ver una clave pide confirmar con Face ID o contraseña; el valor sale una vez y la pantalla lo vuelve a ocultar
        g.MapGet("/{servicio}/{nombre}", async (string servicio, string nombre, AppDbContext db, HttpContext ctx, TimeProvider reloj) =>
        {
            if (Secretos.Campo(servicio, nombre) is null) return Results.NotFound();
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            var s = await db.Secretos.AsNoTracking().FirstOrDefaultAsync(x => x.Servicio == servicio && x.Nombre == nombre);
            return Results.Ok(new { valor = s?.Valor ?? "" });
        });

        g.MapPut("/{servicio}/{nombre}", async (string servicio, string nombre, ValorEntrada e, AppDbContext db, Secretos secretos, HttpContext ctx, TimeProvider reloj) =>
        {
            if (Secretos.Campo(servicio, nombre) is null) return Results.NotFound();
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            var valor = (e.Valor ?? "").Trim();
            if (valor.Length > 1000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["valor"] = ["Demasiado largo."] });
            if (valor.Length > 0 && servicio == "correo" && nombre == "Puerto" && !(int.TryParse(valor, out var p) && p is > 0 and < 65536))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["valor"] = ["El puerto es un número entre 1 y 65535."] });
            if (valor.Length > 0 && nombre == "Url" && servicio is "homeassistant" or "adguard" && await Mda.Api.Pc.Hogar.ValidarUrl(valor) is { } malaUrl)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["valor"] = [malaUrl] });
            if (valor.Length > 0 && servicio == "vigilancia" && !(Uri.TryCreate(valor, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["valor"] = ["Tiene que ser una dirección https://."] });
            await secretos.Poner(db, servicio, nombre, valor, reloj);
            return Results.NoContent();
        });

        // quitar = dejarla vacía (así tampoco vuelve a entrar la del .env)
        g.MapDelete("/{servicio}/{nombre}", async (string servicio, string nombre, AppDbContext db, Secretos secretos, HttpContext ctx, TimeProvider reloj) =>
        {
            if (Secretos.Campo(servicio, nombre) is null) return Results.NotFound();
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            await secretos.Poner(db, servicio, nombre, "", reloj);
            return Results.NoContent();
        });
    }
}
