using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// Un texto o un enlace que pasa entre el móvil y el PC. Se guardan los 20 últimos y solo 24 horas.
public class ElementoPortapapeles
{
    public int Id { get; set; }
    public string Texto { get; set; } = "";
    public string Origen { get; set; } = "";
    public DateTimeOffset Creado { get; set; }
}

public record TextoEntrada(string Texto);

public static class PortapapelesCompartido
{
    public const int MaxTexto = 2000, Guardados = 20;
    public static readonly TimeSpan Vida = TimeSpan.FromHours(24);

    public static async Task<ElementoPortapapeles> Añadir(AppDbContext db, TimeProvider reloj, string texto, string origen, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow();
        var e = new ElementoPortapapeles { Texto = texto, Origen = origen.Length > 40 ? origen[..40] : origen, Creado = ahora };
        db.Portapapeles.Add(e);
        // fuera lo caducado y lo que sobre de los 20 últimos
        db.Portapapeles.RemoveRange(await db.Portapapeles.Where(x => x.Creado < ahora - Vida).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        var sobran = await db.Portapapeles.OrderByDescending(x => x.Id).Skip(Guardados).ToListAsync(ct);
        if (sobran.Count > 0) { db.Portapapeles.RemoveRange(sobran); await db.SaveChangesAsync(ct); }
        return e;
    }

    private static IResult? Validar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) || texto.Length > MaxTexto
            ? Results.ValidationProblem(new Dictionary<string, string[]> { ["texto"] = [$"Escribe algo (hasta {MaxTexto} letras)."] })
            : null;

    public static void MapPortapapeles(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/portapapeles").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            var desde = reloj.GetUtcNow() - Vida;
            return Results.Ok(await db.Portapapeles.AsNoTracking().Where(x => x.Creado >= desde).OrderByDescending(x => x.Id).ToListAsync(ct));
        });

        g.MapPost("/", async (TextoEntrada e, AppDbContext db, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
            Validar(e.Texto) ?? Results.Ok(await Añadir(db, reloj, e.Texto.Trim(), Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()), ct)));

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Portapapeles.FindAsync([id], ct) is not { } x) return Results.NotFound();
            db.Portapapeles.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("/", async (AppDbContext db, CancellationToken ct) =>
        {
            db.Portapapeles.RemoveRange(await db.Portapapeles.ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // al portapapeles del PC / traer el del PC: órdenes para el agente
        g.MapPost("/{id:int}/al-pc", async (int id, AppDbContext db, EstadoAgente agente, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            if (await db.Portapapeles.AsNoTracking().AnyAsync(x => x.Id == id, ct) is false) return Results.NotFound();
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha." });
            await OrdenesPc.Encolar(db, reloj, "portapapeles", id.ToString(), Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()), ct);
            return Results.NoContent();
        }).RequireRateLimiting(Limites.Sensible);

        g.MapPost("/del-pc", async (AppDbContext db, EstadoAgente agente, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha." });
            await OrdenesPc.Encolar(db, reloj, "copiar", "", Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()), ct);
            return Results.NoContent();
        }).RequireRateLimiting(Limites.Sensible);

        // lo que usa el agente (token compartido, sin pasar por el proxy)
        var a = app.MapGroup("/api/sistema/agente/portapapeles").AllowAnonymous().RequireRateLimiting(Limites.Agente);

        a.MapGet("/{id:int}", async (int id, HttpRequest req, IConfiguration config, EstadoAgente agente, AppDbContext db, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            agente.Visto();
            return await db.Portapapeles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) is { } x ? Results.Ok(new { texto = x.Texto }) : Results.NotFound();
        });

        a.MapPost("/", async (HttpRequest req, TextoEntrada e, IConfiguration config, EstadoAgente agente, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            agente.Visto();
            if (Validar(e.Texto) is { } mal) return mal;
            await Añadir(db, reloj, e.Texto.Trim(), "El PC", ct);
            return Results.NoContent();
        });
    }
}
