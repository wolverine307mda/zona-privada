using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;
using WebPush;

namespace Mda.Api.Avisos;

// Un móvil o navegador que ha aceptado recibir notificaciones de la app (Web Push)
public class PushSuscripcion
{
    public int Id { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string Dispositivo { get; set; } = "";
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
}

// Notificaciones push sin servicios de terceros: el estándar Web Push con claves VAPID propias.
// Las claves se crean la primera vez y se guardan en la base (ajustes), así no hace falta configurar nada.
public class Push(IServiceScopeFactory scopes, IConfiguration config, ILogger<Push> log)
{
    private readonly SemaphoreSlim cerrojo = new(1, 1);
    private VapidDetails? vapid;

    public async Task<VapidDetails> Claves(CancellationToken ct = default)
    {
        if (vapid is not null) return vapid;
        await cerrojo.WaitAsync(ct);
        try
        {
            if (vapid is not null) return vapid;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pub = await db.Ajustes.FindAsync(["vapid-publica"], ct);
            var priv = await db.Ajustes.FindAsync(["vapid-privada"], ct);
            if (pub is null || priv is null)
            {
                var k = VapidHelper.GenerateVapidKeys();
                db.Ajustes.RemoveRange(new[] { pub, priv }.Where(x => x is not null)!);
                pub = new Ajuste { Clave = "vapid-publica", Valor = k.PublicKey };
                priv = new Ajuste { Clave = "vapid-privada", Valor = k.PrivateKey };
                db.Ajustes.AddRange(pub, priv);
                await db.SaveChangesAsync(ct);
            }
            var sujeto = config["Correo:Para"] is { Length: > 0 } c ? $"mailto:{c}" : config["App:UrlPublica"] is { Length: > 0 } u ? u : "mailto:admin@localhost";
            return vapid = new VapidDetails(sujeto, pub.Valor, priv.Valor);
        }
        finally { cerrojo.Release(); }
    }

    // Manda a todos los dispositivos suscritos; los que ya no existen (404/410) se borran
    public async Task<bool> Enviar(string titulo, string texto, string? url, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subs = await db.Push.ToListAsync(ct);
        if (subs.Count == 0) return false;
        var claves = await Claves(ct);
        var cuerpo = JsonSerializer.Serialize(new { titulo, texto, url = url ?? "/inicio" });
        using var cliente = new WebPushClient();
        var ok = false;
        foreach (var s in subs)
        {
            try
            {
                await cliente.SendNotificationAsync(new PushSubscription(s.Endpoint, s.P256dh, s.Auth), cuerpo, claves, ct);
                ok = true;
            }
            catch (WebPushException e) when (e.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
            {
                db.Push.Remove(s);
            }
            catch (Exception e) when (e is WebPushException or HttpRequestException or TaskCanceledException)
            {
                log.LogWarning(e, "No se pudo mandar la notificación a {Dispositivo}", s.Dispositivo);
            }
        }
        await db.SaveChangesAsync(ct);
        return ok;
    }
}
