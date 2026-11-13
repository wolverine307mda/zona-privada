using System.Diagnostics;
using System.Net.Sockets;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Servicios;

// Una página o un servicio al que vigilas que siga vivo: tu web, el router, el NAS, el servidor de un amigo.
// Cada 2 minutos se comprueba; tras 2 fallos seguidos te avisa de que cayó y, cuando vuelve, de cuánto estuvo caído.
// Tipo «http»: una dirección web; responde si contesta con algo por debajo de 500. Tipo «tcp»: «host:puerto» abierto.
public class ServicioVigilado
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = "http";
    public string Direccion { get; set; } = "";
    public bool Activo { get; set; } = true;
    public string Estado { get; set; } = "?";            // ? (aún sin comprobar) · ok · caido
    public DateTimeOffset? Desde { get; set; }           // desde cuándo está en ese estado
    public DateTimeOffset? Ultima { get; set; }          // última comprobación
    public int? Ms { get; set; }                         // lo que tardó en contestar la última vez
    public int FallosSeguidos { get; set; }
    public string? UltimoError { get; set; }
    public int Caidas { get; set; }
    public DateTimeOffset? UltimaCaida { get; set; }
}

public record ServicioEntrada(string? Nombre, string? Tipo, string? Direccion, bool? Activo);
public record ServicioDto(int Id, string Nombre, string Tipo, string Direccion, bool Activo, string Estado, DateTimeOffset? Desde, DateTimeOffset? Ultima, int? Ms, string? UltimoError, int Caidas, DateTimeOffset? UltimaCaida);
public record Comprobacion(bool Ok, int? Ms, string? Error);
public record Cambio(bool Cayo, bool Volvio, TimeSpan? Duracion);

public interface IComprobadorServicios
{
    Task<Comprobacion> Comprobar(ServicioVigilado s, CancellationToken ct);
}

// La comprobación de verdad. No lee ni devuelve el contenido de lo que contesta: solo el código y lo que tardó.
public sealed class ComprobadorReal : IComprobadorServicios, IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 3, ConnectTimeout = TimeSpan.FromSeconds(8) }) { Timeout = TimeSpan.FromSeconds(12) };

    public ComprobadorReal() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("ZonaPrivada-Vigilancia/1.0");

    public async Task<Comprobacion> Comprobar(ServicioVigilado s, CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        try
        {
            if (s.Tipo == "tcp")
            {
                var (host, puerto) = ServicioServicios.HostPuerto(s.Direccion)!.Value;
                using var cliente = new TcpClient();
                using var tope = CancellationTokenSource.CreateLinkedTokenSource(ct);
                tope.CancelAfter(TimeSpan.FromSeconds(8));
                await cliente.ConnectAsync(host, puerto, tope.Token);
                return new Comprobacion(true, (int)reloj.ElapsedMilliseconds, null);
            }
            using var r = await _http.GetAsync(s.Direccion, HttpCompletionOption.ResponseHeadersRead, ct);
            var ms = (int)reloj.ElapsedMilliseconds;
            return (int)r.StatusCode < 500 ? new Comprobacion(true, ms, null) : new Comprobacion(false, ms, $"responde {(int)r.StatusCode}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new Comprobacion(false, null, "tardó demasiado en contestar"); }
        catch (HttpRequestException e) { return new Comprobacion(false, null, Corto(e.InnerException?.Message ?? e.Message)); }
        catch (SocketException e) { return new Comprobacion(false, null, Corto(e.Message)); }
    }

    private static string Corto(string t) => t.Length > 100 ? t[..100] : t;

    public void Dispose() => _http.Dispose();
}

public static class ServicioServicios
{
    public const int Maximo = 20;
    public const int FallosParaCaida = 2;
    public static readonly string[] Tipos = ["http", "tcp"];

    // «192.168.1.1:80» o «mi-nas.local:5000»
    public static (string Host, int Puerto)? HostPuerto(string? d)
    {
        d = (d ?? "").Trim();
        var i = d.LastIndexOf(':');
        if (i <= 0 || i == d.Length - 1) return null;
        var host = d[..i];
        if (host.Length > 100 || !host.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_')) return null;
        return int.TryParse(d[(i + 1)..], out var p) && p is >= 1 and <= 65535 ? (host, p) : null;
    }

    public static bool DireccionValida(string tipo, string? d)
    {
        d = (d ?? "").Trim();
        if (d.Length is 0 or > 300) return false;
        if (tipo == "tcp") return HostPuerto(d) is not null;
        if (d.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return false;
        return Uri.TryCreate(d, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && !string.IsNullOrEmpty(u.Host) && u.UserInfo.Length == 0;
    }

    public static Dictionary<string, string[]> Validar(ServicioEntrada e, bool nuevo)
    {
        var mal = new Dictionary<string, string[]>();
        if (nuevo || e.Nombre is not null) { if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 60) mal["nombre"] = ["Ponle un nombre (máximo 60 letras)."]; }
        if (e.Tipo is not null && !Tipos.Contains(e.Tipo)) mal["tipo"] = ["El tipo es http o tcp."];
        if ((nuevo || e.Direccion is not null) && !DireccionValida(e.Tipo ?? "http", e.Direccion))
            mal["direccion"] = [(e.Tipo ?? "http") == "tcp" ? "Pon host:puerto, por ejemplo 192.168.1.1:80." : "Pon una dirección web completa (https://…)."];
        return mal;
    }

    public static ServicioDto Dto(ServicioVigilado s) => new(s.Id, s.Nombre, s.Tipo, s.Direccion, s.Activo, s.Estado, s.Desde, s.Ultima, s.Ms, s.UltimoError, s.Caidas, s.UltimaCaida);

    // Apunta el resultado de una comprobación y dice si hay que avisar de algo (cayó o volvió)
    public static Cambio Aplicar(ServicioVigilado s, Comprobacion c, DateTimeOffset ahora)
    {
        s.Ultima = ahora; s.Ms = c.Ms;
        if (c.Ok)
        {
            var volvio = s.Estado == "caido";
            var duracion = volvio && s.Desde is { } d ? ahora - d : (TimeSpan?)null;
            if (s.Estado != "ok") { s.Estado = "ok"; s.Desde = ahora; }
            s.FallosSeguidos = 0; s.UltimoError = null;
            return new Cambio(false, volvio, duracion);
        }
        s.FallosSeguidos++; s.UltimoError = c.Error;
        if (s.FallosSeguidos >= FallosParaCaida && s.Estado != "caido")
        {
            s.Estado = "caido"; s.Desde = ahora; s.Caidas++; s.UltimaCaida = ahora;
            return new Cambio(true, false, null);
        }
        return new Cambio(false, false, null);
    }

    public static string Duracion(TimeSpan t) =>
        t.TotalMinutes < 1 ? "menos de un minuto" : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min" : t.TotalDays < 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{(int)t.TotalDays} d {t.Hours} h";
}

// Cada 2 minutos comprueba los servicios activos (de 5 en 5) y avisa de las caídas y las vueltas
public sealed class MotorServicios(IServiceScopeFactory ambitos, IComprobadorServicios comprobador, IAvisos avisos, TimeProvider reloj, RegistroVigilancia registro, ILogger<MotorServicios> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(75), reloj, ct);
            using var t = new PeriodicTimer(TimeSpan.FromMinutes(2), reloj);
            do
            {
                try { using var scope = ambitos.CreateScope(); await Vuelta(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct); registro.Anotar("servicios vigilados"); }
                catch (Exception e) when (e is not OperationCanceledException) { registro.Anotar("servicios vigilados", e); log.LogWarning(e, "No se pudieron comprobar los servicios vigilados"); }
            } while (await t.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { /* se apaga la API */ }
    }

    public async Task Vuelta(AppDbContext db, CancellationToken ct)
    {
        var lista = await db.ServiciosVigilados.Where(s => s.Activo).ToListAsync(ct);
        if (lista.Count == 0) return;
        using var cupo = new SemaphoreSlim(5);
        var resultados = await Task.WhenAll(lista.Select(async s =>
        {
            await cupo.WaitAsync(ct);
            try { return await comprobador.Comprobar(s, ct); } finally { cupo.Release(); }
        }));
        var ahora = reloj.GetUtcNow();
        var avisar = new List<(ServicioVigilado S, Cambio C)>();
        for (var i = 0; i < lista.Count; i++)
        {
            var cambio = ServicioServicios.Aplicar(lista[i], resultados[i], ahora);
            if (cambio.Cayo || cambio.Volvio) avisar.Add((lista[i], cambio));
        }
        await db.SaveChangesAsync(ct);
        foreach (var (s, c) in avisar)
        {
            var texto = c.Cayo ? $"🔴 «{s.Nombre}» no responde ({s.UltimoError ?? "sin respuesta"})." : $"🟢 «{s.Nombre}» vuelve a responder (estuvo caído {ServicioServicios.Duracion(c.Duracion ?? TimeSpan.Zero)}).";
            await avisos.Avisar("servicios", c.Cayo ? "Servicio caído" : "Servicio recuperado", texto, textoTelegram: texto, ct: ct);
        }
    }
}

public static class ServiciosEndpoints
{
    public static void MapServicios(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/servicios").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok((await db.ServiciosVigilados.AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct)).Select(ServicioServicios.Dto)));

        g.MapPost("/", async (ServicioEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (ServicioServicios.Validar(e, true) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.ServiciosVigilados.CountAsync(ct) >= ServicioServicios.Maximo)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Como mucho {ServicioServicios.Maximo} servicios: borra alguno."] });
            var s = new ServicioVigilado { Nombre = e.Nombre!.Trim(), Tipo = e.Tipo ?? "http", Direccion = e.Direccion!.Trim(), Activo = e.Activo ?? true };
            db.ServiciosVigilados.Add(s);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/servicios/{s.Id}", ServicioServicios.Dto(s));
        });

        g.MapPut("/{id:int}", async (int id, ServicioEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            var s = await db.ServiciosVigilados.FindAsync([id], ct);
            if (s is null) return Results.NotFound();
            // la dirección se valida contra el tipo que quedará
            if (ServicioServicios.Validar(e with { Tipo = e.Tipo ?? s.Tipo }, false) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (e.Tipo is not null && e.Tipo != s.Tipo && e.Direccion is null && !ServicioServicios.DireccionValida(e.Tipo, s.Direccion))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["direccion"] = ["Al cambiar el tipo, pon también la dirección que le toca."] });
            if (e.Nombre is not null) s.Nombre = e.Nombre.Trim();
            if ((e.Tipo is not null && e.Tipo != s.Tipo) || e.Direccion is not null)
            {
                s.Tipo = e.Tipo ?? s.Tipo; s.Direccion = e.Direccion?.Trim() ?? s.Direccion;
                s.Estado = "?"; s.Desde = null; s.FallosSeguidos = 0; s.UltimoError = null; s.Ms = null;
            }
            if (e.Activo is { } a) s.Activo = a;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ServicioServicios.Dto(s));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var s = await db.ServiciosVigilados.FindAsync([id], ct);
            if (s is null) return Results.NotFound();
            db.ServiciosVigilados.Remove(s);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // «Comprobar ahora»: mira ya y lo apunta, sin avisos (los avisos son del vigilante)
        g.MapPost("/{id:int}/comprobar", async (int id, AppDbContext db, IComprobadorServicios comprobador, TimeProvider reloj, CancellationToken ct) =>
        {
            var s = await db.ServiciosVigilados.FindAsync([id], ct);
            if (s is null) return Results.NotFound();
            var c = await comprobador.Comprobar(s, ct);
            ServicioServicios.Aplicar(s, c, reloj.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.Ok(ServicioServicios.Dto(s));
        }).RequireRateLimiting(Mda.Api.Seguridad.Limites.Sensible);
    }
}
