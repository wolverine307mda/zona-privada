using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Servidor;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// Wake-on-LAN: el «paquete mágico» son 6 bytes FF y la dirección física 16 veces. Lo manda la propia API (el mini PC, que está siempre encendido,
// despierta al PC principal aunque no haya ningún agente de por medio) y, si el agente está conectado, también lo pide a él por si la API
// corre en un sitio sin acceso a la red de casa (Docker).
public static partial class WakeOnLan
{
    [GeneratedRegex("^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$")]
    private static partial Regex Formato();

    public static bool MacValida(string? mac) => mac is not null && Formato().IsMatch(mac.Trim());

    public static byte[] Paquete(string mac)
    {
        if (!MacValida(mac)) throw new ArgumentException("Dirección física no válida.", nameof(mac));
        var bytes = mac.Trim().Split(':', '-').Select(x => Convert.ToByte(x, 16)).ToArray();
        var p = new byte[6 + 16 * 6];
        for (var i = 0; i < 6; i++) p[i] = 0xFF;
        for (var i = 0; i < 16; i++) Array.Copy(bytes, 0, p, 6 + i * 6, 6);
        return p;
    }

    // direcciones de difusión a las que probar: la general y la de cada red IPv4 local (más fiable que solo la general)
    public static IEnumerable<IPAddress> Difusiones()
    {
        yield return IPAddress.Broadcast;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            foreach (var u in n.GetIPProperties().UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork && u.IPv4Mask is not null))
            {
                var ip = u.Address.GetAddressBytes(); var m = u.IPv4Mask!.GetAddressBytes();
                yield return new IPAddress(ip.Select((b, i) => (byte)(b | ~m[i])).ToArray());
            }
    }

    // true si salió algún paquete (no se puede saber si el PC lo recibió: se mira después si responde)
    public static async Task<bool> Enviar(string mac, CancellationToken ct = default)
    {
        var paquete = Paquete(mac);
        var salio = false;
        using var udp = new UdpClient { EnableBroadcast = true };
        foreach (var destino in Difusiones().Distinct())
            foreach (var puerto in new[] { 9, 7 })
            {
                try { await udp.SendAsync(paquete, new IPEndPoint(destino, puerto), ct); salio = true; }
                catch (SocketException) { /* esa red no deja difundir: se prueba con las otras */ }
            }
        return salio;
    }
}

// «El PC principal»: el aparato de la lista de Red que se despierta con un toque (app, Siri, Telegram…). Se elige en Red y se guarda como ajuste.
public static class PcPrincipal
{
    public const string Clave = "pc-principal";

    public static async Task<DispositivoRed?> Leer(AppDbContext db, CancellationToken ct = default)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor;
        return int.TryParse(v, out var id) ? await db.DispositivosRed.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) : null;
    }

    // despierta ese aparato; el resultado ya viene redactado para decírselo a quien lo pidió
    public static async Task<(bool Ok, string Mensaje)> Despertar(DispositivoRed d, AppDbContext db, EstadoAgente agente, TimeProvider reloj, string desde, CancellationToken ct = default)
    {
        if (!WakeOnLan.MacValida(d.Mac)) return (false, $"«{d.Nombre}» no tiene una dirección física (MAC) válida: ponla en Red.");
        if (d.EnLinea == true) return (true, $"«{d.Nombre}» ya está encendido.");
        var directo = await WakeOnLan.Enviar(d.Mac, ct);
        var porAgente = agente.Conectado(OrdenesPc.AgenteConectado);
        if (porAgente) await OrdenesPc.Encolar(db, reloj, "wol", d.Mac, desde, ct);
        if (!directo && !porAgente) return (false, "No pude mandar el paquete: la API no tiene acceso a la red de casa y el agente del PC no está en marcha.");
        await ServidorLog.Anotar(db, reloj, "pc", $"Despertar «{d.Nombre}»", $"{desde} · {(directo ? "paquete directo" : "")}{(directo && porAgente ? " + " : "")}{(porAgente ? "orden al agente" : "")}", ct: ct);
        return (true, $"Despertando «{d.Nombre}». Si no responde en un minuto, mira que tenga activado Wake-on-LAN en la BIOS y en la tarjeta de red.");
    }

    public static void MapPcPrincipal(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pc/principal").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(new { principal = await Leer(db, ct) is { } d ? new { d.Id, d.Nombre, d.Mac, d.EnLinea, conMac = WakeOnLan.MacValida(d.Mac) } : null }));

        // elegir cuál es (o quitarlo con null)
        g.MapPut("/", async (PrincipalEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (e.Id is { } id)
            {
                var d = await db.DispositivosRed.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                if (d is null) return Results.NotFound();
                if (!WakeOnLan.MacValida(d.Mac)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["id"] = ["Ese aparato no tiene una dirección física (MAC) válida: ponla en Red."] });
            }
            var a = await db.Ajustes.FindAsync([Clave], ct);
            if (e.Id is null) { if (a is not null) db.Ajustes.Remove(a); }
            else { if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = Clave }); a.Valor = e.Id.Value.ToString(); }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("/despertar", async (AppDbContext db, EstadoAgente agente, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            if (await Leer(db, ct) is not { } d) return Results.Conflict(new { mensaje = "Aún no has elegido cuál es tu PC principal (Red › «Es mi PC principal»)." });
            var (ok, mensaje) = await Despertar(d, db, agente, reloj, "App · " + IpCliente.De(ctx), ct);
            return ok ? Results.Ok(new { mensaje }) : Results.Conflict(new { mensaje });
        });
    }
}

public record PrincipalEntrada(int? Id);
