using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Galeria;

public record TemaWebEntrada(string Modo, string Color);

// Tema por defecto del portfolio (modo y color), el mismo para todos los visitantes: se edita en Ajustes y la web pública lo lee
// de /api/publico/tema. Mientras no lo fijes no hay nada público (404) y cada navegador sigue con el suyo (sistema + naranja).
// Solo salen dos valores de una lista cerrada: la web los valida otra vez antes de usarlos.
public static class TemaWeb
{
    public const string Clave = "web-tema";
    public static readonly string[] Modos = ["light", "dark", "system"];
    public static readonly string[] Colores = ["naranja", "lima", "cobalto", "rosa", "rojo"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<TemaWebEntrada?> Leer(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor;
        try
        {
            var t = v is null ? null : JsonSerializer.Deserialize<TemaWebEntrada>(v, Json);
            return t is not null && Modos.Contains(t.Modo) && Colores.Contains(t.Color) ? t : null;
        }
        catch (JsonException) { return null; }
    }

    public static void MapTemaWeb(this IEndpointRouteBuilder app)
    {
        // gestión (con sesión)
        var g = app.MapGroup("/api/tema-web").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await Leer(db, ct) is { } t ? new { configurado = true, modo = t.Modo, color = t.Color } : new { configurado = false, modo = "system", color = "naranja" }));

        g.MapPut("/", async (TemaWebEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (!Modos.Contains(e.Modo)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["modo"] = ["Elige día, noche o sistema."] });
            if (!Colores.Contains(e.Color)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["color"] = ["Ese color no existe en el portfolio."] });
            var a = await db.Ajustes.FindAsync([Clave], ct);
            if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = Clave });
            a.Valor = JsonSerializer.Serialize(new TemaWebEntrada(e.Modo, e.Color), Json);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // «sin tema fijo»: la web vuelve a que cada navegador use el suyo
        g.MapDelete("/", async (AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Ajustes.FindAsync([Clave], ct) is { } a) { db.Ajustes.Remove(a); await db.SaveChangesAsync(ct); }
            return Results.NoContent();
        });

        // lo que lee la web pública: solo lectura, sin sesión
        app.MapGet("/api/publico/tema", async (AppDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            if (await Leer(db, ct) is not { } t) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "public, max-age=60";
            return Results.Ok(new { modo = t.Modo, color = t.Color });
        }).AllowAnonymous().RequireCors(GaleriaEndpoints.WebPublica).RequireRateLimiting(Limites.Publico);
    }
}
