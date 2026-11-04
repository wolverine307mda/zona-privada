using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// Un aparato de la red de casa (impresora, NAS, cámara, router…) que se vigila abriendo una conexión a su puerto.
public class DispositivoRed
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Host { get; set; } = "";
    public int Puerto { get; set; }
    public string Icono { get; set; } = "otro";
    public bool Avisar { get; set; } = true;
    // la dirección física (AA:BB:CC:DD:EE:FF): con ella se puede despertar el aparato (Wake-on-LAN) desde el agente del PC
    public string Mac { get; set; } = "";
    public bool? EnLinea { get; set; }
    public int? LatenciaMs { get; set; }
    public DateTimeOffset? Comprobado { get; set; }
    public DateTimeOffset? Cambio { get; set; }
}

public record DispositivoEntrada(string Nombre, string Host, int Puerto, string? Icono, bool Avisar = true, string? Mac = null);

public interface IComprobadorRed
{
    Task<(bool EnLinea, int? Ms)> Comprobar(string host, int puerto, CancellationToken ct);
}

public sealed class ComprobadorTcp : IComprobadorRed
{
    public async Task<(bool EnLinea, int? Ms)> Comprobar(string host, int puerto, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(2));
        var reloj = Stopwatch.StartNew();
        try
        {
            using var c = new TcpClient();
            await c.ConnectAsync(host, puerto, limite.Token);
            return (true, (int)reloj.ElapsedMilliseconds);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or IOException) { return (false, null); }
    }
}

public static class RedCasa
{
    public const int Maximo = 20;
    public static readonly string[] Iconos = ["pc", "movil", "nas", "impresora", "camara", "tv", "router", "otro"];

    // Solo se vigilan direcciones de la red de casa (o de Tailscale): la API no debe hacer de escáner de Internet
    public static bool EsLocal(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any) || a.Equals(IPAddress.None)) return false;
        if (a.AddressFamily == AddressFamily.InterNetworkV6) return a.IsIPv6LinkLocal || (a.GetAddressBytes()[0] & 0xFE) == 0xFC;
        var b = a.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    // «aa-bb-cc-dd-ee-ff» o «AA:BB:…» → «AA:BB:CC:DD:EE:FF»; vacío si no hay; null si no es una MAC
    public static string? NormalizarMac(string? mac)
    {
        var m = (mac ?? "").Trim().Replace('-', ':').ToUpperInvariant();
        if (m.Length == 0) return "";
        return System.Text.RegularExpressions.Regex.IsMatch(m, "^([0-9A-F]{2}:){5}[0-9A-F]{2}$") ? m : null;
    }

    // el nombre o la IP tienen que resolver SOLO a direcciones locales
    public static async Task<string?> ValidarHost(string host, CancellationToken ct = default)
    {
        host = (host ?? "").Trim();
        if (host.Length is 0 or > 120 || host.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or '@' or '?' or '#')) return "Pon solo la IP o el nombre del aparato, sin http:// ni rutas.";
        try
        {
            var ips = IPAddress.TryParse(host, out var ip) ? [ip] : await Dns.GetHostAddressesAsync(host, ct);
            if (ips.Length == 0) return "Ese nombre no se resuelve.";
            return ips.All(EsLocal) ? null : "Solo se pueden vigilar aparatos de tu red de casa (192.168.x.x, 10.x.x.x, 172.16-31.x.x) o de Tailscale.";
        }
        catch (Exception e) when (e is SocketException or ArgumentException) { return "Ese nombre no se resuelve."; }
    }

    public static async Task Aplicar(AppDbContext db, DispositivoRed d, IComprobadorRed comprobador, DateTimeOffset ahora, CancellationToken ct)
    {
        var (en, ms) = await comprobador.Comprobar(d.Host, d.Puerto, ct);
        if (d.EnLinea != en) d.Cambio = ahora;
        (d.EnLinea, d.LatenciaMs, d.Comprobado) = (en, ms, ahora);
        await db.SaveChangesAsync(ct);
    }

    public static void MapRed(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/red").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(await db.DispositivosRed.AsNoTracking().OrderBy(d => d.Nombre).ToListAsync(ct)));

        async Task<IResult?> Validar(DispositivoEntrada e, CancellationToken ct)
        {
            var errores = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 60) errores["nombre"] = ["Ponle un nombre (hasta 60 letras)."];
            if (e.Puerto is < 1 or > 65535) errores["puerto"] = ["El puerto va de 1 a 65535."];
            if (NormalizarMac(e.Mac) is null) errores["mac"] = ["La dirección MAC son seis pares de letras y números, como AA:BB:CC:DD:EE:FF."];
            if (await ValidarHost(e.Host, ct) is { } motivo) errores["host"] = [motivo];
            return errores.Count > 0 ? Results.ValidationProblem(errores) : null;
        }
        static void Copiar(DispositivoEntrada e, DispositivoRed d)
        {
            d.Nombre = e.Nombre.Trim(); d.Host = e.Host.Trim(); d.Puerto = e.Puerto;
            d.Icono = Iconos.Contains((e.Icono ?? "").ToLowerInvariant()) ? e.Icono!.ToLowerInvariant() : "otro"; d.Avisar = e.Avisar;
            d.Mac = NormalizarMac(e.Mac) ?? "";
        }

        g.MapPost("/", async (DispositivoEntrada e, AppDbContext db, IComprobadorRed comprobador, TimeProvider reloj, CancellationToken ct) =>
        {
            if (await db.DispositivosRed.CountAsync(ct) >= Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Máximo {Maximo} aparatos."] });
            if (await Validar(e, ct) is { } mal) return mal;
            var d = new DispositivoRed();
            Copiar(e, d);
            db.DispositivosRed.Add(d);
            await db.SaveChangesAsync(ct);
            await Aplicar(db, d, comprobador, reloj.GetUtcNow(), ct);
            return Results.Ok(d);
        });

        g.MapPut("/{id:int}", async (int id, DispositivoEntrada e, AppDbContext db, IComprobadorRed comprobador, TimeProvider reloj, CancellationToken ct) =>
        {
            if (await db.DispositivosRed.FindAsync([id], ct) is not { } d) return Results.NotFound();
            if (await Validar(e, ct) is { } mal) return mal;
            Copiar(e, d);
            await Aplicar(db, d, comprobador, reloj.GetUtcNow(), ct);
            return Results.Ok(d);
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.DispositivosRed.FindAsync([id], ct) is not { } d) return Results.NotFound();
            db.DispositivosRed.Remove(d);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Wake-on-LAN: lo manda el agente desde el PC (el contenedor está detrás de NAT y su difusión no llegaría a la red de casa); pide Face ID
        g.MapPost("/{id:int}/despertar", async (int id, AppDbContext db, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            if (await db.DispositivosRed.FindAsync([id], ct) is not { } d) return Results.NotFound();
            if (d.Mac.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["mac"] = ["Este aparato no tiene dirección MAC: añádela para poder despertarlo."] });
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha (agente\\arrancar.cmd)." });
            if (!await Mda.Api.Seguridad.Reautenticar.Vigente(ctx, db, reloj)) return Mda.Api.Seguridad.Reautenticar.Pedir();
            var desde = Seguridad.Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()) + " · " + Seguridad.IpCliente.De(ctx);
            var o = await OrdenesPc.Encolar(db, reloj, "wol", d.Mac, desde, ct);
            _ = avisos.PorTelegram($"💻 Orden al PC: despertar «{d.Nombre}»\nDesde {desde}. Si no has sido tú, cierra sesiones en Seguridad.");
            return Results.Ok(o);
        }).RequireRateLimiting(Mda.Api.Seguridad.Limites.Sensible);

        g.MapPost("/{id:int}/probar", async (int id, AppDbContext db, IComprobadorRed comprobador, TimeProvider reloj, CancellationToken ct) =>
        {
            if (await db.DispositivosRed.FindAsync([id], ct) is not { } d) return Results.NotFound();
            if (await ValidarHost(d.Host, ct) is not null) return Results.Conflict(new { detalle = "Esa dirección ya no es de tu red de casa." });
            await Aplicar(db, d, comprobador, reloj.GetUtcNow(), ct);
            return Results.Ok(d);
        }).RequireRateLimiting(Mda.Api.Seguridad.Limites.Sensible);
    }
}

// Cada minuto prueba cada aparato; avisa cuando cambia (apagado o de vuelta), pero solo tras dos lecturas iguales seguidas
// para no avisar por un fallo suelto de la red.
public class VigilanteRed(IServiceScopeFactory scopes, IComprobadorRed comprobador, IAvisos avisos, TimeProvider reloj, ILogger<VigilanteRed> log) : BackgroundService
{
    private readonly Dictionary<int, int> _seguidas = [];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), reloj, ct); } catch (OperationCanceledException) { return; }
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(1), reloj);
        do
        {
            try { await Revisar(ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Fallo vigilando la red de casa"); }
        } while (await t.WaitForNextTickAsync(ct));
    }

    public async Task Revisar(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var d in await db.DispositivosRed.ToListAsync(ct))
        {
            if (await RedCasa.ValidarHost(d.Host, ct) is not null) continue;
            var (en, ms) = await comprobador.Comprobar(d.Host, d.Puerto, ct);
            var ahora = reloj.GetUtcNow();
            d.Comprobado = ahora; d.LatenciaMs = en ? ms : null;
            if (d.EnLinea is null) { d.EnLinea = en; d.Cambio = ahora; }               // la primera lectura no avisa
            else if (d.EnLinea == en) _seguidas.Remove(d.Id);
            else if (_seguidas.GetValueOrDefault(d.Id) + 1 >= 2)
            {
                _seguidas.Remove(d.Id);
                d.EnLinea = en; d.Cambio = ahora;
                if (d.Avisar) await avisos.PorTelegram(en ? $"🟢 «{d.Nombre}» vuelve a estar en línea." : $"🔴 «{d.Nombre}» ({d.Host}) no responde.", ct);
            }
            else _seguidas[d.Id] = _seguidas.GetValueOrDefault(d.Id) + 1;
            await db.SaveChangesAsync(ct);
        }
    }
}
