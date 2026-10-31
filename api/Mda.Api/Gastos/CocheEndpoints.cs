using Mda.Api.Agenda;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Gastos;

public record MantenimientoEntrada(string Nombre, int? CadaKm, int? CadaMeses, DateOnly? UltimaFecha, int? UltimoKm, string? Notas);
public record HechoEntrada(DateOnly? Fecha, int? Km);
public record EstadoMantenimiento(
    int Id, string Nombre, int? CadaKm, int? CadaMeses, DateOnly? UltimaFecha, int? UltimoKm, string? Notas,
    int? ProximoKm, int? FaltanKm, DateOnly? ProximaFecha, int? FaltanDias, string Nivel, double Progreso);

// Mantenimiento del coche: lo que toca por kilómetros (los del último repostaje) o por tiempo
public static class Coche
{
    public static async Task<int?> KmActual(AppDbContext db, CancellationToken ct = default) =>
        await db.Movimientos.Where(m => m.Categoria == "gasolina" && m.Kilometros != null).MaxAsync(m => m.Kilometros, ct);

    public static EstadoMantenimiento EstadoDe(Mantenimiento m, int? kmActual, DateOnly hoy)
    {
        int? proxKm = m.CadaKm is { } ck && m.UltimoKm is { } uk ? uk + ck : null;
        var km = Math.Max(kmActual ?? 0, m.UltimoKm ?? 0);
        int? faltanKm = proxKm is { } p ? p - km : null;
        DateOnly? proxFecha = m.CadaMeses is { } cm && m.UltimaFecha is { } uf ? uf.AddMonths(cm) : null;
        int? faltanDias = proxFecha is { } f ? f.DayNumber - hoy.DayNumber : null;
        var nivel = faltanKm is null && faltanDias is null ? "sin-datos"
            : faltanKm <= 0 || faltanDias <= 0 ? "vencido"
            : faltanKm <= 1000 || faltanDias <= 30 ? "pronto" : "ok";
        double progreso = 0;
        if (m.CadaKm is > 0 && faltanKm is { } fk) progreso = Math.Max(progreso, 1 - (double)fk / m.CadaKm.Value);
        if (m.CadaMeses is > 0 && m.UltimaFecha is { } u && proxFecha is { } pf)
            progreso = Math.Max(progreso, (double)(hoy.DayNumber - u.DayNumber) / Math.Max(1, pf.DayNumber - u.DayNumber));
        return new EstadoMantenimiento(m.Id, m.Nombre, m.CadaKm, m.CadaMeses, m.UltimaFecha, m.UltimoKm, m.Notas,
            proxKm, faltanKm, proxFecha, faltanDias, nivel, Math.Round(Math.Clamp(progreso, 0, 1), 3));
    }

    public static void MapCoche(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos/mantenimiento").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var km = await KmActual(db, ct);
            var hoy = Cartera.Hoy(reloj, zona);
            var lista = (await db.Mantenimientos.AsNoTracking().ToListAsync(ct)).Select(m => EstadoDe(m, km, hoy))
                .OrderBy(e => e.Nivel switch { "vencido" => 0, "pronto" => 1, "ok" => 2, _ => 3 }).ThenBy(e => e.FaltanDias ?? int.MaxValue).ToList();
            return Results.Ok(new { kmActual = km, lista });
        });

        g.MapPost("/", async (MantenimientoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (Validar(e) is { } error) return error;
            var m = new Mantenimiento();
            Copiar(e, m);
            db.Mantenimientos.Add(m);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/gastos/mantenimiento/{m.Id}", m);
        });

        g.MapPut("/{id:int}", async (int id, MantenimientoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (Validar(e) is { } error) return error;
            var m = await db.Mantenimientos.FindAsync([id], ct);
            if (m is null) return Results.NotFound();
            Copiar(e, m);
            await db.SaveChangesAsync(ct);
            return Results.Ok(m);
        });

        // «hecho»: la última vez pasa a ser hoy (o la fecha dada) con los km actuales (o los dados)
        g.MapPost("/{id:int}/hecho", async (int id, HechoEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var m = await db.Mantenimientos.FindAsync([id], ct);
            if (m is null) return Results.NotFound();
            m.UltimaFecha = e.Fecha ?? Cartera.Hoy(reloj, zona);
            m.UltimoKm = e.Km ?? await KmActual(db, ct) ?? m.UltimoKm;
            m.Avisado = null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(m);
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var m = await db.Mantenimientos.FindAsync([id], ct);
            if (m is null) return Results.NotFound();
            db.Mantenimientos.Remove(m);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static IResult? Validar(MantenimientoEntrada e)
    {
        var errores = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Length > 80) errores["nombre"] = ["Ponle un nombre (máximo 80 caracteres)."];
        if (e.CadaKm is null && e.CadaMeses is null) errores["cadaKm"] = ["Indica cada cuántos km o cada cuántos meses."];
        if (e.CadaKm is < 100 or > 1_000_000) errores["cadaKm"] = ["Los km no parecen correctos."];
        if (e.CadaMeses is < 1 or > 240) errores["cadaMeses"] = ["Entre 1 y 240 meses."];
        if (e.UltimoKm is < 0 or > 5_000_000) errores["ultimoKm"] = ["Los km no parecen correctos."];
        if (e.Notas?.Length > 500) errores["notas"] = ["Máximo 500 caracteres."];
        return errores.Count > 0 ? Results.ValidationProblem(errores) : null;
    }

    private static void Copiar(MantenimientoEntrada e, Mantenimiento m)
    {
        m.Nombre = e.Nombre.Trim();
        m.CadaKm = e.CadaKm;
        m.CadaMeses = e.CadaMeses;
        m.UltimaFecha = e.UltimaFecha;
        m.UltimoKm = e.UltimoKm;
        m.Notas = string.IsNullOrWhiteSpace(e.Notas) ? null : e.Notas.Trim();
        m.Avisado = null;
    }
}
