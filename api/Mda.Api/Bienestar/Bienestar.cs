using System.Globalization;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Bienestar;

// Los datos de salud van CIFRADOS en la base (un campo JSON cifrado por fila: la fecha queda en claro solo para poder ordenar).
public class PerfilBienestar { public int Id { get; set; } public string Datos { get; set; } = ""; }
public class PlanEntreno { public int Id { get; set; } public DateOnly Inicio { get; set; } public bool Activo { get; set; } = true; public string Datos { get; set; } = ""; }
public class MedidaCuerpo { public int Id { get; set; } public DateOnly Fecha { get; set; } public string Datos { get; set; } = ""; }
public class EntrenoHecho { public int Id { get; set; } public DateOnly Fecha { get; set; } public string Datos { get; set; } = ""; }
public class RegistroDia { public int Id { get; set; } public DateOnly Fecha { get; set; } public string Datos { get; set; } = ""; }

public record MedidaDatos(double? Peso, double? Cintura, double? Pecho, double? Brazo, double? Grasa);
public record EntrenoDatos(string Tipo, int Minutos, int? Esfuerzo, string Notas, List<SerieHecha>? Series);
public record SerieHecha(string Ejercicio, int Serie, int Reps, double Peso);
public record DiaDatos(int Agua, double? Sueño, int? Pasos, int? Animo);
public record MedidaEntrada(string? Fecha, double? Peso, double? Cintura, double? Pecho, double? Brazo, double? Grasa);
public record EntrenoEntrada(string? Fecha, string? Tipo, int? Minutos, int? Esfuerzo, string? Notas, List<SerieHecha>? Series);
public record DiaEntrada(string? Fecha, int? Agua, int? SumarAgua, double? Sueño, int? Pasos, int? Animo);
public record SaludAtajo(double? Peso, double? Sueño, int? Pasos, List<EntrenoAtajo>? Entrenos);
public record EntrenoAtajo(string? Tipo, int? Minutos);

public static class BienestarServicio
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public const string Proposito = "bienestar";
    public const int AguaMaxima = 30;

    public static string Cif<T>(T datos) => Boveda.Cifrar(JsonSerializer.Serialize(datos, Json), Proposito);
    public static T? Des<T>(string v) { try { return JsonSerializer.Deserialize<T>(Boveda.Descifrar(v, Proposito), Json); } catch (JsonException) { return default; } }

    public static Dictionary<string, string[]> ValidarPerfil(PerfilDatos p)
    {
        var mal = new Dictionary<string, string[]>();
        if (!Objetivos.Validos.Contains(p.Objetivo)) mal["objetivo"] = [$"Elige uno: {string.Join(", ", Objetivos.Validos)}."];
        if (!Objetivos.Niveles.Contains(p.Nivel)) mal["nivel"] = ["Principiante, intermedio o avanzado."];
        if (!Objetivos.Equipos.Contains(p.Equipo)) mal["equipo"] = ["Gimnasio, casa con material o nada."];
        if (p.DiasSemana is < 1 or > 6) mal["diasSemana"] = ["De 1 a 6 días."];
        if (p.Minutos is < 10 or > 120) mal["minutos"] = ["De 10 a 120 minutos."];
        if (p.AlturaCm is < 100 or > 250) mal["alturaCm"] = ["Una altura razonable en cm."];
        if (p.AnioNacimiento is { } a && (a < 1900 || a > DateTime.UtcNow.Year - 10)) mal["anioNacimiento"] = ["Año no válido."];
        if (p.Limitaciones.Length > 500) mal["limitaciones"] = ["Hasta 500 letras."];
        return mal;
    }

    public static async Task<PerfilDatos?> Perfil(AppDbContext db, CancellationToken ct)
    {
        var p = await db.PerfilesBienestar.AsNoTracking().FirstOrDefaultAsync(ct);
        return p is null ? null : Des<PerfilDatos>(p.Datos);
    }

    public static async Task<(PlanEntreno Fila, PlanGenerado Plan)?> PlanActivo(AppDbContext db, CancellationToken ct)
    {
        var f = await db.PlanesEntreno.AsNoTracking().Where(p => p.Activo).OrderByDescending(p => p.Id).FirstOrDefaultAsync(ct);
        return f is null || Des<PlanGenerado>(f.Datos) is not { } plan ? null : (f, plan);
    }

    // Lunes de la semana de una fecha, y número de semana del plan (1-based) en esa fecha
    public static DateOnly Lunes(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    public static int SemanaDelPlan(DateOnly inicio, DateOnly hoy) => (Lunes(hoy).DayNumber - Lunes(inicio).DayNumber) / 7 + 1;
    public static int DiaSemana(DateOnly d) => ((int)d.DayOfWeek + 6) % 7 + 1;

    public static SesionPlan? SesionDe(PlanGenerado plan, DateOnly inicio, DateOnly fecha)
    {
        var semana = SemanaDelPlan(inicio, fecha);
        return semana < 1 ? null : plan.Sesiones.FirstOrDefault(s => s.Semana == semana && s.DiaSemana == DiaSemana(fecha));
    }

    public static async Task<DiaDatos> DiaDe(AppDbContext db, DateOnly fecha, CancellationToken ct)
    {
        var f = await db.RegistrosDia.AsNoTracking().FirstOrDefaultAsync(r => r.Fecha == fecha, ct);
        return (f is null ? null : Des<DiaDatos>(f.Datos)) ?? new DiaDatos(0, null, null, null);
    }

    public static async Task GuardarDia(AppDbContext db, DateOnly fecha, DiaDatos d, CancellationToken ct)
    {
        var f = await db.RegistrosDia.FirstOrDefaultAsync(r => r.Fecha == fecha, ct);
        if (f is null) db.RegistrosDia.Add(new RegistroDia { Fecha = fecha, Datos = Cif(d) }); else f.Datos = Cif(d);
        await db.SaveChangesAsync(ct);
    }

    public static async Task<List<PuntoPeso>> Pesos(AppDbContext db, CancellationToken ct) =>
        (await db.Medidas.AsNoTracking().OrderBy(m => m.Fecha).ToListAsync(ct)).Select(m => (m.Fecha, d: Des<MedidaDatos>(m.Datos))).Where(x => x.d?.Peso is not null).Select(x => new PuntoPeso(x.Fecha, x.d!.Peso!.Value)).ToList();

    // El resumen de la semana en curso (lunes a hoy)
    public static async Task<string> ResumenSemanal(AppDbContext db, DateOnly hoy, CancellationToken ct)
    {
        var lunes = Lunes(hoy);
        var entrenos = (await db.Entrenos.AsNoTracking().Where(e => e.Fecha >= lunes && e.Fecha <= hoy).ToListAsync(ct)).Count;
        var dias = (await db.RegistrosDia.AsNoTracking().Where(r => r.Fecha >= lunes && r.Fecha <= hoy).ToListAsync(ct)).Select(r => Des<DiaDatos>(r.Datos)).Where(d => d is not null).Select(d => d!).ToList();
        var previstos = 0;
        if (await PlanActivo(db, ct) is { } pa) previstos = pa.Plan.Sesiones.Count(s => s.Semana == SemanaDelPlan(pa.Fila.Inicio, hoy));
        var sueño = dias.Where(d => d.Sueño is not null).Select(d => d.Sueño!.Value).ToList();
        var agua = dias.Where(d => d.Agua > 0).Select(d => (double)d.Agua).ToList();
        var pasos = dias.Where(d => d.Pasos is not null).Select(d => d.Pasos!.Value).ToList();
        return ResumenBienestar.Texto(entrenos, previstos, sueño.Count > 0 ? sueño.Average() : null, agua.Count > 0 ? agua.Average() : null, pasos.Count > 0 ? (int)pasos.Average() : null);
    }

    // Una vez al día a las 8:00: «hoy toca…» (si hay plan y no lo has hecho); y si llevas 3 días sin registrar nada, un recordatorio suave. Los domingos por la tarde, el resumen.
    public static async Task Avisar(AppDbContext db, IAvisos avisos, DateOnly hoy, DateTime ahoraLocal, CancellationToken ct)
    {
        const string clave = "bienestar-avisado";
        var marca = await db.Ajustes.FirstOrDefaultAsync(a => a.Clave == clave, ct);
        if (marca?.Valor == hoy.ToString("O") || ahoraLocal.Hour < 8) return;
        var textos = new List<string>();
        if (await PlanActivo(db, ct) is { } pa)
        {
            if (SesionDe(pa.Plan, pa.Fila.Inicio, hoy) is { } s && !await db.Entrenos.AnyAsync(e => e.Fecha == hoy, ct)) textos.Add($"💪 Hoy toca: {s.Titulo} ({s.Minutos} min). Si hoy no puedes, no pasa nada.");
            else if (!await db.Entrenos.AnyAsync(e => e.Fecha > hoy.AddDays(-3), ct) && pa.Plan.Sesiones.Any(x => x.Semana == SemanaDelPlan(pa.Fila.Inicio, hoy))) textos.Add("🚶 Llevas unos días sin moverte: un paseo corto ya cuenta.");
        }
        if (ahoraLocal.DayOfWeek == DayOfWeek.Sunday && ahoraLocal.Hour >= 18 && await db.Entrenos.AnyAsync(e => e.Fecha >= Lunes(hoy), ct)) textos.Add("📊 " + await ResumenSemanal(db, hoy, ct));
        if (ahoraLocal.DayOfWeek != DayOfWeek.Sunday || ahoraLocal.Hour >= 18) { if (marca is null) db.Ajustes.Add(new Ajuste { Clave = clave, Valor = hoy.ToString("O") }); else marca.Valor = hoy.ToString("O"); await db.SaveChangesAsync(ct); }
        foreach (var t in textos) { await avisos.PorPush("💪 Bienestar", t, "/bienestar", ct); await avisos.PorTelegram(t, ct); }
    }
}

public static class BienestarEndpoints
{
    public static void MapBienestar(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/bienestar").RequireAuthorization();
        static DateOnly Hoy(TimeProvider r, Zona z) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.GetUtcNow(), z.Info).Date);
        static DateOnly? Fecha(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        // lo de hoy: la sesión que toca, el agua, lo hecho y el resumen de la semana
        g.MapGet("/hoy", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona);
            var perfil = await BienestarServicio.Perfil(db, ct);
            var pa = await BienestarServicio.PlanActivo(db, ct);
            var sesion = pa is { } p ? BienestarServicio.SesionDe(p.Plan, p.Fila.Inicio, hoy) : null;
            var hechos = await db.Entrenos.AsNoTracking().Where(e => e.Fecha == hoy).ToListAsync(ct);
            var pesos = await BienestarServicio.Pesos(db, ct);
            var tendencia = SeguimientoPeso.TendenciaSemanal(pesos, hoy);
            return Results.Ok(new
            {
                tienePerfil = perfil is not null, tienePlan = pa is not null, hoy = hoy.ToString("yyyy-MM-dd"), sesion,
                entrenadoHoy = hechos.Count > 0, dia = await BienestarServicio.DiaDe(db, hoy, ct), semana = await BienestarServicio.ResumenSemanal(db, hoy, ct),
                semanaDelPlan = pa is { } q ? BienestarServicio.SemanaDelPlan(q.Fila.Inicio, hoy) : (int?)null,
                tendenciaKgSemana = tendencia, avisoRitmo = SeguimientoPeso.AvisoRitmo(tendencia, pesos.Count > 0 ? pesos[^1].Valor : 0),
                aviso = "Esto es una ayuda personal, no consejo médico. Ante dolor, mareos, enfermedad, lesión o embarazo, consulta a un profesional.",
            });
        });

        g.MapGet("/perfil", async (AppDbContext db, CancellationToken ct) => Results.Ok(await BienestarServicio.Perfil(db, ct)));

        // el cuestionario inicial: guarda el perfil y genera el plan (el anterior se archiva)
        g.MapPut("/perfil", async (PerfilDatos p, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (BienestarServicio.ValidarPerfil(p) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var fila = await db.PerfilesBienestar.FirstOrDefaultAsync(ct);
            if (fila is null) db.PerfilesBienestar.Add(new PerfilBienestar { Datos = BienestarServicio.Cif(p) }); else fila.Datos = BienestarServicio.Cif(p);
            foreach (var viejo in await db.PlanesEntreno.Where(x => x.Activo).ToListAsync(ct)) viejo.Activo = false;
            var plan = GeneradorPlan.Generar(p);
            db.PlanesEntreno.Add(new PlanEntreno { Inicio = BienestarServicio.Lunes(Hoy(reloj, zona)), Datos = BienestarServicio.Cif(plan) });
            await db.SaveChangesAsync(ct);
            return Results.Ok(plan);
        });

        g.MapGet("/plan", async (AppDbContext db, CancellationToken ct) =>
            await BienestarServicio.PlanActivo(db, ct) is { } p ? Results.Ok(new { inicio = p.Fila.Inicio.ToString("yyyy-MM-dd"), plan = p.Plan }) : Results.NotFound());

        // «llevo poca asistencia»: simplifica lo que queda del plan (un día menos y una serie menos) sin culpa
        g.MapPost("/plan/simplificar", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (await BienestarServicio.PlanActivo(db, ct) is not { } p) return Results.NotFound();
            var hoy = Hoy(reloj, zona); var semana = BienestarServicio.SemanaDelPlan(p.Fila.Inicio, hoy);
            var fila = await db.PlanesEntreno.FindAsync([p.Fila.Id], ct);
            fila!.Datos = BienestarServicio.Cif(GeneradorPlan.Simplificar(p.Plan, Math.Max(1, semana)));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { simplificado = true });
        });

        // la asistencia de las dos últimas semanas (y si conviene simplificar)
        g.MapGet("/asistencia", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (await BienestarServicio.PlanActivo(db, ct) is not { } p) return Results.Ok(new { hayPlan = false });
            var hoy = Hoy(reloj, zona); var semana = BienestarServicio.SemanaDelPlan(p.Fila.Inicio, hoy);
            var previstas = p.Plan.Sesiones.Count(s => s.Semana >= semana - 2 && s.Semana < semana);
            var desde = BienestarServicio.Lunes(hoy).AddDays(-14);
            var hechas = await db.Entrenos.CountAsync(e => e.Fecha >= desde && e.Fecha < BienestarServicio.Lunes(hoy), ct);
            var (simplificar, asistencia) = GeneradorPlan.Asistencia(previstas, hechas);
            return Results.Ok(new { hayPlan = true, previstas, hechas, asistencia, simplificar });
        });

        g.MapGet("/ejercicios", () => Results.Ok(Ejercicios.Todos));

        // ---- registro ----
        g.MapPost("/entrenos", async (EntrenoEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (e.Esfuerzo is < 1 or > 10 || e.Minutos is < 0 or > 600 || (e.Notas?.Length ?? 0) > 500 || (e.Series?.Count ?? 0) > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["entreno"] = ["Esfuerzo de 1 a 10, hasta 600 minutos y 500 letras de notas."] });
            var fecha = Fecha(e.Fecha) ?? Hoy(reloj, zona);
            var datos = new EntrenoDatos((e.Tipo ?? "general").Trim()[..Math.Min((e.Tipo ?? "general").Trim().Length, 40)], e.Minutos ?? 0, e.Esfuerzo, (e.Notas ?? "").Trim(), e.Series);
            var fila = new EntrenoHecho { Fecha = fecha, Datos = BienestarServicio.Cif(datos) };
            db.Entrenos.Add(fila);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/bienestar/entrenos/{fila.Id}", new { fila.Id });
        });

        g.MapGet("/entrenos", async (int? dias, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var desde = Hoy(reloj, zona).AddDays(-Math.Clamp(dias ?? 60, 1, 400));
            var l = await db.Entrenos.AsNoTracking().Where(e => e.Fecha >= desde).OrderByDescending(e => e.Fecha).ThenByDescending(e => e.Id).ToListAsync(ct);
            return Results.Ok(l.Select(e => new { e.Id, fecha = e.Fecha.ToString("yyyy-MM-dd"), datos = BienestarServicio.Des<EntrenoDatos>(e.Datos) }));
        });

        g.MapDelete("/entrenos/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var e = await db.Entrenos.FindAsync([id], ct);
            if (e is null) return Results.NotFound();
            db.Entrenos.Remove(e); await db.SaveChangesAsync(ct); return Results.NoContent();
        });

        g.MapPost("/medidas", async (MedidaEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (e.Peso is < 25 or > 400 || e.Cintura is < 30 or > 250 || e.Pecho is < 30 or > 250 || e.Brazo is < 10 or > 100 || e.Grasa is < 2 or > 70 || (e.Peso is null && e.Cintura is null && e.Pecho is null && e.Brazo is null && e.Grasa is null))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["medida"] = ["Pon al menos una medida con valores razonables (peso en kg, perímetros en cm)."] });
            var fecha = Fecha(e.Fecha) ?? Hoy(reloj, zona);
            var datos = new MedidaDatos(e.Peso, e.Cintura, e.Pecho, e.Brazo, e.Grasa);
            var fila = await db.Medidas.FirstOrDefaultAsync(m => m.Fecha == fecha, ct);
            if (fila is null) db.Medidas.Add(new MedidaCuerpo { Fecha = fecha, Datos = BienestarServicio.Cif(datos) }); else fila.Datos = BienestarServicio.Cif(datos);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // las medidas con la media móvil de 7 días del peso
        g.MapGet("/medidas", async (int? dias, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona); var desde = hoy.AddDays(-Math.Clamp(dias ?? 120, 7, 730));
            var filas = (await db.Medidas.AsNoTracking().Where(m => m.Fecha >= desde).OrderBy(m => m.Fecha).ToListAsync(ct)).Select(m => (m.Fecha, d: BienestarServicio.Des<MedidaDatos>(m.Datos))).Where(x => x.d is not null).ToList();
            var pesos = SeguimientoPeso.MediaMovil(filas.Where(x => x.d!.Peso is not null).Select(x => new PuntoPeso(x.Fecha, x.d!.Peso!.Value)));
            return Results.Ok(new { puntos = filas.Select(x => new { fecha = x.Fecha.ToString("yyyy-MM-dd"), x.d!.Peso, x.d.Cintura, x.d.Pecho, x.d.Brazo, x.d.Grasa }), peso = pesos.Select(p => new { fecha = p.Fecha.ToString("yyyy-MM-dd"), p.Valor, p.Media }) });
        });

        g.MapPut("/dia", async (DiaEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (e.Agua is < 0 or > BienestarServicio.AguaMaxima || e.SumarAgua is < -10 or > 10 || e.Sueño is < 0 or > 24 || e.Pasos is < 0 or > 100000 || e.Animo is < 1 or > 5)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["dia"] = ["Agua hasta 30 vasos, sueño hasta 24 h, pasos hasta 100.000 y ánimo de 1 a 5."] });
            var fecha = Fecha(e.Fecha) ?? Hoy(reloj, zona);
            var a = await BienestarServicio.DiaDe(db, fecha, ct);
            var nuevo = a with { Agua = Math.Clamp(e.Agua ?? a.Agua + (e.SumarAgua ?? 0), 0, BienestarServicio.AguaMaxima), Sueño = e.Sueño ?? a.Sueño, Pasos = e.Pasos ?? a.Pasos, Animo = e.Animo ?? a.Animo };
            await BienestarServicio.GuardarDia(db, fecha, nuevo, ct);
            return Results.Ok(nuevo);
        });

        // todo lo de bienestar se puede borrar desde aquí
        g.MapDelete("/todo", async (AppDbContext db, CancellationToken ct) =>
        {
            db.PerfilesBienestar.RemoveRange(await db.PerfilesBienestar.ToListAsync(ct)); db.PlanesEntreno.RemoveRange(await db.PlanesEntreno.ToListAsync(ct));
            db.Medidas.RemoveRange(await db.Medidas.ToListAsync(ct)); db.Entrenos.RemoveRange(await db.Entrenos.ToListAsync(ct)); db.RegistrosDia.RemoveRange(await db.RegistrosDia.ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
