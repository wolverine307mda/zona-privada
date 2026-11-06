using System.Globalization;
using Mda.Api.Agenda;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Tareas;

// Una tarea «por hacer»: algo suelto que hay que sacar adelante, con o sin fecha límite.
// `Prioridad`: 0 baja, 1 normal, 2 alta. `Vence` es opcional; sin ella la tarea no caduca nunca.
public class Tarea
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Notas { get; set; } = "";
    public int Prioridad { get; set; } = 1;
    public DateOnly? Vence { get; set; }
    public bool Hecha { get; set; }
    public DateTimeOffset Creada { get; set; }
    public DateTimeOffset? HechaEn { get; set; }
    public DateOnly? AvisadoFecha { get; set; }   // el último día en que el vigilante la mencionó (para no repetir)
}

// `Vence` y `Hecha`: "" quita la fecha; en `Hecha` se manda un booleano
public record TareaEntrada(string? Titulo, string? Notas, int? Prioridad, string? Vence, bool? Hecha);
public record TareaDto(int Id, string Titulo, string Notas, int Prioridad, string? Vence, bool Hecha, bool Vencida, bool Hoy, int? Dias, DateTimeOffset? HechaEn);
public record ResumenTareas(int Pendientes, int Vencidas, int ParaHoy, int HechasHoy);

public static class ServicioTareas
{
    public const int MaximoPendientes = 300;
    public const int MaximoHechas = 60;

    public static DateOnly Hoy(TimeProvider reloj, Zona zona) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).Date);

    public static TareaDto Dto(Tarea t, DateOnly hoy)
    {
        var dias = t.Vence is { } v ? v.DayNumber - hoy.DayNumber : (int?)null;
        return new TareaDto(t.Id, t.Titulo, t.Notas, t.Prioridad, t.Vence?.ToString("yyyy-MM-dd"), t.Hecha,
            !t.Hecha && dias < 0, !t.Hecha && dias == 0, dias, t.HechaEn);
    }

    // Pendientes: primero las vencidas y las de más cerca, luego las sin fecha; a igualdad, la más importante. Hechas: las últimas.
    public static async Task<List<TareaDto>> Lista(AppDbContext db, DateOnly hoy, bool hechas, CancellationToken ct)
    {
        if (hechas)
        {
            var h = await db.Tareas.AsNoTracking().Where(t => t.Hecha).OrderByDescending(t => t.HechaEn).Take(MaximoHechas).ToListAsync(ct);
            return h.Select(t => Dto(t, hoy)).ToList();
        }
        var l = await db.Tareas.AsNoTracking().Where(t => !t.Hecha).ToListAsync(ct);
        return l.OrderBy(t => t.Vence is null ? 1 : 0).ThenBy(t => t.Vence).ThenByDescending(t => t.Prioridad).ThenBy(t => t.Id).Select(t => Dto(t, hoy)).ToList();
    }

    public static async Task<ResumenTareas> Resumen(AppDbContext db, DateOnly hoy, TimeProvider reloj, Zona zona, CancellationToken ct)
    {
        var pendientes = await db.Tareas.AsNoTracking().Where(t => !t.Hecha).Select(t => t.Vence).ToListAsync(ct);
        var inicioHoy = new DateTimeOffset(hoy.ToDateTime(TimeOnly.MinValue), zona.Info.GetUtcOffset(hoy.ToDateTime(TimeOnly.MinValue)));
        var hechasHoy = await db.Tareas.CountAsync(t => t.Hecha && t.HechaEn >= inicioHoy, ct);
        return new ResumenTareas(pendientes.Count, pendientes.Count(v => v < hoy), pendientes.Count(v => v == hoy), hechasHoy);
    }

    public static Dictionary<string, string[]> Validar(TareaEntrada e, bool nueva)
    {
        var mal = new Dictionary<string, string[]>();
        if (nueva || e.Titulo is not null) { if (string.IsNullOrWhiteSpace(e.Titulo) || e.Titulo.Trim().Length > 120) mal["titulo"] = ["Ponle un título (máximo 120 letras)."]; }
        if (e.Notas is { Length: > 1000 }) mal["notas"] = ["Las notas llegan a 1000 letras."];
        if (e.Prioridad is { } p && p is < 0 or > 2) mal["prioridad"] = ["La prioridad va de 0 (baja) a 2 (alta)."];
        if (!string.IsNullOrEmpty(e.Vence) && !DateOnly.TryParseExact(e.Vence, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            mal["vence"] = ["La fecha va como 2026-11-06."];
        return mal;
    }

    public static DateOnly? Fecha(string? s) => !string.IsNullOrEmpty(s) && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

    // Marca una tarea como hecha (o la reabre). Devuelve null si no existe.
    public static async Task<Tarea?> Completar(AppDbContext db, int id, bool hecha, TimeProvider reloj, CancellationToken ct)
    {
        var t = await db.Tareas.FindAsync([id], ct);
        if (t is null) return null;
        t.Hecha = hecha;
        t.HechaEn = hecha ? reloj.GetUtcNow() : null;
        await db.SaveChangesAsync(ct);
        return t;
    }

    // Quita de la lista las hechas que llevan más de 90 días: no hace falta guardarlas para siempre
    public static async Task Podar(AppDbContext db, TimeProvider reloj, CancellationToken ct)
    {
        var limite = reloj.GetUtcNow().AddDays(-90);
        var viejas = await db.Tareas.Where(t => t.Hecha && t.HechaEn < limite).ToListAsync(ct);
        if (viejas.Count == 0) return;
        db.Tareas.RemoveRange(viejas);
        await db.SaveChangesAsync(ct);
    }
}

public static class TareasEndpoints
{
    public static void MapTareas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/tareas").RequireAuthorization();

        g.MapGet("/", async (bool? hechas, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
            Results.Ok(await ServicioTareas.Lista(db, ServicioTareas.Hoy(reloj, zona), hechas ?? false, ct)));

        g.MapGet("/resumen", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
            Results.Ok(await ServicioTareas.Resumen(db, ServicioTareas.Hoy(reloj, zona), reloj, zona, ct)));

        g.MapPost("/", async (TareaEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (ServicioTareas.Validar(e, true) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.Tareas.CountAsync(t => !t.Hecha, ct) >= ServicioTareas.MaximoPendientes)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["titulo"] = [$"Ya tienes {ServicioTareas.MaximoPendientes} tareas pendientes: cierra alguna."] });
            var t = new Tarea
            {
                Titulo = e.Titulo!.Trim(), Notas = (e.Notas ?? "").Trim(), Prioridad = e.Prioridad ?? 1,
                Vence = ServicioTareas.Fecha(e.Vence), Creada = reloj.GetUtcNow(),
            };
            db.Tareas.Add(t);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/tareas/{t.Id}", ServicioTareas.Dto(t, ServicioTareas.Hoy(reloj, zona)));
        });

        g.MapPut("/{id:int}", async (int id, TareaEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (ServicioTareas.Validar(e, false) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var t = await db.Tareas.FindAsync([id], ct);
            if (t is null) return Results.NotFound();
            if (e.Titulo is not null) t.Titulo = e.Titulo.Trim();
            if (e.Notas is not null) t.Notas = e.Notas.Trim();
            if (e.Prioridad is { } p) t.Prioridad = p;
            if (e.Vence is not null) { t.Vence = ServicioTareas.Fecha(e.Vence); t.AvisadoFecha = null; }
            if (e.Hecha is { } h && h != t.Hecha) { t.Hecha = h; t.HechaEn = h ? reloj.GetUtcNow() : null; }
            await db.SaveChangesAsync(ct);
            return Results.Ok(ServicioTareas.Dto(t, ServicioTareas.Hoy(reloj, zona)));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var t = await db.Tareas.FindAsync([id], ct);
            if (t is null) return Results.NotFound();
            db.Tareas.Remove(t);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Vacía la lista de hechas
        g.MapDelete("/hechas", async (AppDbContext db, CancellationToken ct) =>
        {
            var hechas = await db.Tareas.Where(t => t.Hecha).ToListAsync(ct);
            db.Tareas.RemoveRange(hechas);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { borradas = hechas.Count });
        });
    }
}
