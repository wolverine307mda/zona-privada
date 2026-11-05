using System.Globalization;
using Mda.Api.Agenda;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Habitos;

// Un hábito: algo que quieres hacer cada día (o ciertos días de la semana). `Dias` es una máscara: lunes = 1, martes = 2, miércoles = 4… domingo = 64; 127 = todos.
// `Objetivo` es cuántas veces al día cuenta como hecho (1 = un sí o no; 8 = ocho vasos de agua).
public class Habito
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Icono { get; set; } = "check";
    public int Dias { get; set; } = 127;
    public int Objetivo { get; set; } = 1;
    public string Unidad { get; set; } = "";
    public string? HoraAviso { get; set; }          // «21:30»: si a esa hora no está hecho, avisa
    public DateOnly? AvisadoFecha { get; set; }     // el último día en que se avisó (para no repetir)
    public bool Archivado { get; set; }
    public DateOnly Creado { get; set; }
    public int Orden { get; set; }
}

// Cuánto se hizo un día; sin fila = nada
public class RegistroHabito
{
    public int Id { get; set; }
    public int HabitoId { get; set; }
    public DateOnly Fecha { get; set; }
    public int Cantidad { get; set; }
}

public record HabitoEntrada(string? Nombre, string? Icono, int? Dias, int? Objetivo, string? Unidad, string? HoraAviso, bool? Archivado);
public record Marca(string? Fecha, int? Cantidad);
public record Estadisticas(int Actual, int Mejor, int Cumplimiento30, string Mapa);

// Lo que se manda de cada hábito: el hábito, si toca hoy y cuánto lleva, y sus rachas
public record HabitoDto(int Id, string Nombre, string Icono, int Dias, int Objetivo, string Unidad, string? HoraAviso, bool Archivado, bool TocaHoy, int Hoy, bool Hecho, int Actual, int Mejor, int Cumplimiento30, string Mapa);

// Rachas y cumplimiento. Solo cuentan los días que tocaban: un hábito de lunes a viernes no se rompe el sábado.
// Hoy sin hacer todavía no rompe la racha (aún queda el día). Un día que no tocaba pero se hizo suma igualmente.
public static class Rachas
{
    public const int DiasMapa = 91;   // 13 semanas
    public static readonly string[] Letras = ["L", "M", "X", "J", "V", "S", "D"];

    public static bool Toca(int dias, DateOnly f) => (dias & (1 << (((int)f.DayOfWeek + 6) % 7))) != 0;

    public static Estadisticas Calcular(int dias, int objetivo, IReadOnlyDictionary<DateOnly, int> registros, DateOnly creado, DateOnly hoy)
    {
        int Hecho(DateOnly f) => registros.TryGetValue(f, out var c) ? c : 0;
        bool Cumple(DateOnly f) => Hecho(f) >= objetivo;

        // racha actual: hacia atrás desde hoy
        int actual = 0;
        for (var f = hoy; f >= creado; f = f.AddDays(-1))
        {
            if (Cumple(f)) { actual++; continue; }
            if (!Toca(dias, f) || f == hoy) continue;   // un día libre o el de hoy (aún sin hacer) no la rompe
            break;
        }

        // mejor racha: hacia delante desde que se creó
        int mejor = 0, corrida = 0;
        for (var f = creado; f <= hoy; f = f.AddDays(1))
        {
            if (Cumple(f)) { corrida++; mejor = Math.Max(mejor, corrida); }
            else if (Toca(dias, f) && f != hoy) corrida = 0;
        }

        // cumplimiento de los últimos 30 días que tocaban (sin contar hoy si aún no está hecho)
        int tocaron = 0, hechos = 0;
        for (var f = hoy.AddDays(-29); f <= hoy; f = f.AddDays(1))
        {
            if (f < creado || !Toca(dias, f) || (f == hoy && !Cumple(f))) continue;
            tocaron++;
            if (Cumple(f)) hechos++;
        }

        // el mapa: una letra por día, de más antiguo a hoy: h hecho · p a medias · f fallado · o pendiente (hoy) · l libre (no tocaba) · - antes de crearlo
        var mapa = new char[DiasMapa];
        for (var i = 0; i < DiasMapa; i++)
        {
            var f = hoy.AddDays(i - (DiasMapa - 1));
            mapa[i] = f < creado ? '-' : Cumple(f) ? 'h' : Hecho(f) > 0 ? 'p' : !Toca(dias, f) ? 'l' : f == hoy ? 'o' : 'f';
        }
        return new Estadisticas(actual, mejor, tocaron == 0 ? 0 : (int)Math.Round(100.0 * hechos / tocaron), new string(mapa));
    }

    public static bool HoraValida(string? h) => h is null || TimeOnly.TryParseExact(h, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

public static class ServicioHabitos
{
    public const int Maximo = 20;
    public const int HistoriaDias = 400;

    public static async Task<List<HabitoDto>> Resumen(AppDbContext db, DateOnly hoy, bool archivados, CancellationToken ct)
    {
        var habitos = await db.Habitos.AsNoTracking().Where(h => h.Archivado == archivados).OrderBy(h => h.Orden).ThenBy(h => h.Id).ToListAsync(ct);
        var desde = hoy.AddDays(-HistoriaDias);
        var registros = (await db.RegistrosHabito.AsNoTracking().Where(r => r.Fecha >= desde).ToListAsync(ct)).GroupBy(r => r.HabitoId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Fecha, r => r.Cantidad));
        return habitos.Select(h => Dto(h, registros.GetValueOrDefault(h.Id) ?? [], hoy)).ToList();
    }

    // Desde cuándo cuenta el hábito: el día que se creó o, si se apuntaron días anteriores (para ponerse al día), el primero de ellos
    public static DateOnly Inicio(Habito h, IReadOnlyDictionary<DateOnly, int> registros, DateOnly hoy)
    {
        var inicio = registros.Count > 0 && registros.Keys.Min() < h.Creado ? registros.Keys.Min() : h.Creado;
        return inicio < hoy.AddDays(-HistoriaDias) ? hoy.AddDays(-HistoriaDias) : inicio;
    }

    public static HabitoDto Dto(Habito h, IReadOnlyDictionary<DateOnly, int> registros, DateOnly hoy)
    {
        var e = Rachas.Calcular(h.Dias, h.Objetivo, registros, Inicio(h, registros, hoy), hoy);
        var cantidad = registros.GetValueOrDefault(hoy);
        return new HabitoDto(h.Id, h.Nombre, h.Icono, h.Dias, h.Objetivo, h.Unidad, h.HoraAviso, h.Archivado, Rachas.Toca(h.Dias, hoy), cantidad, cantidad >= h.Objetivo, e.Actual, e.Mejor, e.Cumplimiento30, e.Mapa);
    }

    public static Dictionary<string, string[]> Validar(HabitoEntrada e, bool nuevo)
    {
        var mal = new Dictionary<string, string[]>();
        if (nuevo || e.Nombre is not null) { if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 40) mal["nombre"] = ["Ponle un nombre (máximo 40 letras)."]; }
        if (e.Dias is { } d && d is < 1 or > 127) mal["dias"] = ["Elige al menos un día."];
        if (e.Objetivo is { } o && o is < 1 or > 99) mal["objetivo"] = ["El objetivo diario va de 1 a 99."];
        if (e.Unidad is { Length: > 12 }) mal["unidad"] = ["Máximo 12 letras."];
        if (e.Icono is { Length: > 20 }) mal["icono"] = ["Icono no válido."];
        if (!Rachas.HoraValida(e.HoraAviso == "" ? null : e.HoraAviso)) mal["horaAviso"] = ["La hora va como 21:30."];
        return mal;
    }

    // Pone la cantidad de un día (0 la quita). Devuelve null si el hábito no existe.
    public static async Task<Habito?> Marcar(AppDbContext db, int id, DateOnly fecha, int cantidad, CancellationToken ct)
    {
        var h = await db.Habitos.FindAsync([id], ct);
        if (h is null) return null;
        var r = await db.RegistrosHabito.FirstOrDefaultAsync(x => x.HabitoId == id && x.Fecha == fecha, ct);
        if (cantidad <= 0) { if (r is not null) db.RegistrosHabito.Remove(r); }
        else if (r is null) db.RegistrosHabito.Add(new RegistroHabito { HabitoId = id, Fecha = fecha, Cantidad = Math.Min(cantidad, 9999) });
        else r.Cantidad = Math.Min(cantidad, 9999);
        await db.SaveChangesAsync(ct);
        return h;
    }
}

public static class HabitosEndpoints
{
    public static void MapHabitos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/habitos").RequireAuthorization();
        DateOnly Hoy(TimeProvider reloj, Zona zona) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).Date);

        g.MapGet("/", async (bool? archivados, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
            Results.Ok(await ServicioHabitos.Resumen(db, Hoy(reloj, zona), archivados ?? false, ct)));

        g.MapPost("/", async (HabitoEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (ServicioHabitos.Validar(e, true) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.Habitos.CountAsync(h => !h.Archivado, ct) >= ServicioHabitos.Maximo)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Como mucho {ServicioHabitos.Maximo} hábitos a la vez: archiva alguno."] });
            var orden = await db.Habitos.Select(h => (int?)h.Orden).MaxAsync(ct) ?? 0;
            var h = new Habito
            {
                Nombre = e.Nombre!.Trim(), Icono = string.IsNullOrWhiteSpace(e.Icono) ? "check" : e.Icono, Dias = e.Dias ?? 127, Objetivo = e.Objetivo ?? 1,
                Unidad = (e.Unidad ?? "").Trim(), HoraAviso = string.IsNullOrWhiteSpace(e.HoraAviso) ? null : e.HoraAviso, Creado = Hoy(reloj, zona), Orden = orden + 1,
            };
            db.Habitos.Add(h);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/habitos/{h.Id}", ServicioHabitos.Dto(h, new Dictionary<DateOnly, int>(), Hoy(reloj, zona)));
        });

        g.MapPut("/{id:int}", async (int id, HabitoEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (ServicioHabitos.Validar(e, false) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var h = await db.Habitos.FindAsync([id], ct);
            if (h is null) return Results.NotFound();
            if (e.Nombre is not null) h.Nombre = e.Nombre.Trim();
            if (e.Icono is not null) h.Icono = e.Icono;
            if (e.Dias is { } d) h.Dias = d;
            if (e.Objetivo is { } o) h.Objetivo = o;
            if (e.Unidad is not null) h.Unidad = e.Unidad.Trim();
            if (e.HoraAviso is not null) h.HoraAviso = e.HoraAviso == "" ? null : e.HoraAviso;
            if (e.Archivado is { } a) h.Archivado = a;
            await db.SaveChangesAsync(ct);
            var hoy = Hoy(reloj, zona);
            var regs = await db.RegistrosHabito.AsNoTracking().Where(r => r.HabitoId == id && r.Fecha >= hoy.AddDays(-ServicioHabitos.HistoriaDias)).ToDictionaryAsync(r => r.Fecha, r => r.Cantidad, ct);
            return Results.Ok(ServicioHabitos.Dto(h, regs, hoy));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var h = await db.Habitos.FindAsync([id], ct);
            if (h is null) return Results.NotFound();
            db.RegistrosHabito.RemoveRange(db.RegistrosHabito.Where(r => r.HabitoId == id));
            db.Habitos.Remove(h);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Pone la cantidad de un día (por defecto hoy y el objetivo entero; 0 la quita)
        g.MapPut("/{id:int}/marcar", async (int id, Marca m, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona);
            var fecha = hoy;
            if (m.Fecha is not null && !DateOnly.TryParseExact(m.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["fecha"] = ["La fecha va como 2026-11-05."] });
            if (fecha > hoy || fecha < hoy.AddDays(-ServicioHabitos.HistoriaDias))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["fecha"] = ["Solo se puede marcar hasta hoy y como mucho hace un año."] });
            var h0 = await db.Habitos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (h0 is null) return Results.NotFound();
            if (m.Cantidad is { } c0 && c0 is < 0 or > 9999) return Results.ValidationProblem(new Dictionary<string, string[]> { ["cantidad"] = ["Cantidad no válida."] });
            var h = await ServicioHabitos.Marcar(db, id, fecha, m.Cantidad ?? h0.Objetivo, ct);
            var regs = await db.RegistrosHabito.AsNoTracking().Where(r => r.HabitoId == id && r.Fecha >= hoy.AddDays(-ServicioHabitos.HistoriaDias)).ToDictionaryAsync(r => r.Fecha, r => r.Cantidad, ct);
            return Results.Ok(ServicioHabitos.Dto(h!, regs, hoy));
        });
    }
}
