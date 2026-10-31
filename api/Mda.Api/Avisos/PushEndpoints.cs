using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Avisos;

public record SuscripcionEntrada(string Endpoint, string P256dh, string Auth);

// Suscribir este dispositivo a las notificaciones y probarlas
public static class PushEndpoints
{
    // el servidor hace POST al «endpoint» que da el navegador: solo se acepta el de los servicios de push reales (no una URL cualquiera)
    private static readonly string[] Servicios =
        ["fcm.googleapis.com", "push.apple.com", "updates.push.services.mozilla.com", "push.services.mozilla.com", "notify.windows.com", "wns2-par02p.notify.windows.com"];
    internal static bool ServicioPushConocido(string host) =>
        Servicios.Any(s => host.Equals(s, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));

    public static void MapPush(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/push").RequireAuthorization();

        g.MapGet("/clave", async (Push push, CancellationToken ct) => Results.Ok(new { clave = (await push.Claves(ct)).PublicKey }));

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Push.AsNoTracking().OrderByDescending(p => p.Creado).Select(p => new { p.Id, p.Dispositivo, p.Creado, p.Endpoint }).ToListAsync(ct)));

        g.MapPost("/", async (SuscripcionEntrada e, HttpRequest req, AppDbContext db, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(e.Endpoint, UriKind.Absolute, out var u) || u.Scheme != "https" || !ServicioPushConocido(u.Host) || e.Endpoint.Length > 1000
                || string.IsNullOrWhiteSpace(e.P256dh) || e.P256dh.Length > 200 || string.IsNullOrWhiteSpace(e.Auth) || e.Auth.Length > 100)
                return Results.BadRequest();
            var s = await db.Push.FirstOrDefaultAsync(p => p.Endpoint == e.Endpoint, ct);
            if (s is null) db.Push.Add(s = new PushSuscripcion { Endpoint = e.Endpoint });
            s.P256dh = e.P256dh;
            s.Auth = e.Auth;
            var nombre = Dispositivo.Nombre(req.Headers.UserAgent.ToString());
            s.Dispositivo = nombre.Length > 120 ? nombre[..120] : nombre;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { s.Id });
        });

        g.MapDelete("/", async (string endpoint, AppDbContext db, CancellationToken ct) =>
        {
            db.Push.RemoveRange(await db.Push.Where(p => p.Endpoint == endpoint).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            db.Push.RemoveRange(await db.Push.Where(p => p.Id == id).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
