using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Gastos;

public record MovimientoEntrada(
    TipoMovimiento Tipo, DateOnly Fecha, decimal Importe, string Categoria, string Concepto, string? Notas,
    int? RecurrenteId, decimal? Litros, decimal? PrecioLitro, int? Kilometros,
    // al crear: «se repite» convierte el apunte en el primero de un recurrente (cada mes, trimestre o año)
    Frecuencia? Repite = null, bool Variable = false);

public record RecurrenteEntrada(
    string Nombre, TipoMovimiento Tipo, string Categoria, decimal Importe, bool Variable, int Dia,
    Frecuencia Frecuencia, DateOnly Inicio, DateOnly? Fin, bool Activo = true);

public record Registro(decimal? Importe, DateOnly? Fecha);

// API del módulo de gastos: apuntes, fijos (suscripciones, alquiler, nómina…), resumen del mes,
// estadísticas y gasolina. Todo con sesión iniciada.
public static class GastosEndpoints
{
    public static void MapGastos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos").RequireAuthorization();

        g.MapGet("/categorias", () => Categorias.Todas.Select(c => new { id = c.Key, nombre = c.Value.Nombre, tipo = c.Value.Tipo, grupo = c.Value.Grupo }));

        // ---- Resumen de un mes ----
        g.MapGet("/mes/{anio:int}/{mes:int}", async (int anio, int mes, AppDbContext db) =>
        {
            if (mes is < 1 or > 12 || anio is < 2000 or > 2100) return Results.BadRequest();
            var desde = new DateOnly(anio, mes, 1);
            var ant = desde.AddMonths(-1);
            var movs = await db.Movimientos.AsNoTracking().Where(m => m.Fecha >= ant && m.Fecha < desde.AddMonths(1)).ToListAsync();
            var recurrentes = await db.Recurrentes.AsNoTracking().ToListAsync();
            return Results.Ok(Calculos.Mes(anio, mes,
                movs.Where(m => m.Fecha >= desde).ToList(), movs.Where(m => m.Fecha < desde).ToList(), recurrentes));
        });

        // ---- Estadísticas de los últimos N meses ----
        g.MapGet("/estadisticas", async (int? meses, int? anio, int? mes, AppDbContext db, TimeProvider reloj, Zona zona) =>
        {
            var hoy = Cartera.Hoy(reloj, zona);
            if (anio is < 2000 or > 2100 || mes is < 1 or > 12) return Results.BadRequest();
            var n = Math.Clamp(meses ?? 12, 1, 60);
            var hasta = new DateOnly(anio ?? hoy.Year, mes ?? hoy.Month, 1);
            var desde = hasta.AddMonths(-(n - 1));
            var movs = await db.Movimientos.AsNoTracking().Where(m => m.Fecha >= desde && m.Fecha < hasta.AddMonths(1)).ToListAsync();
            return Results.Ok(Calculos.Periodo(hasta.Year, hasta.Month, n, movs));
        });

        // ---- Gasolina ----
        g.MapGet("/gasolina", async (AppDbContext db, TimeProvider reloj, Zona zona) =>
        {
            var repostajes = await db.Movimientos.AsNoTracking().Where(m => m.Categoria == "gasolina").ToListAsync();
            return Results.Ok(Calculos.GasolinaDe(repostajes, Cartera.Hoy(reloj, zona).Year));
        });

        // ---- Apuntes ----
        g.MapGet("/movimientos", async (DateOnly? desde, DateOnly? hasta, string? categoria, string? q, AppDbContext db) =>
        {
            var consulta = db.Movimientos.AsNoTracking().AsQueryable();
            if (desde is { } d) consulta = consulta.Where(m => m.Fecha >= d);
            if (hasta is { } h) consulta = consulta.Where(m => m.Fecha <= h);
            if (!string.IsNullOrWhiteSpace(categoria)) consulta = consulta.Where(m => m.Categoria == categoria);
            // la búsqueda va en la base de datos (antes se cortaba a 1000 y se filtraba después: no encontraba lo antiguo)
            if (!string.IsNullOrWhiteSpace(q))
            {
                var ql = q.Trim().ToLowerInvariant();
                consulta = consulta.Where(m => m.Concepto.ToLower().Contains(ql) || (m.Notas != null && m.Notas.ToLower().Contains(ql)));
            }
            return Results.Ok(await consulta.OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id).Take(1000).ToListAsync());
        });

        g.MapPost("/movimientos", (MovimientoEntrada e, AppDbContext db, IAvisos avisos, ILogger<Movimiento> log, CancellationToken ct) => Crear(e, db, avisos, log, ct));

        g.MapPut("/movimientos/{id:int}", async (int id, MovimientoEntrada e, AppDbContext db, IAvisos avisos, ILogger<Movimiento> log, CancellationToken ct) =>
        {
            if (Validar(e) is { } error) return error;
            if (await FijoInexistente(e, db, ct) is { } sinFijo) return sinFijo;
            var m = await db.Movimientos.FindAsync([id], ct);
            if (m is null) return Results.NotFound();
            Copiar(e, m);
            await db.SaveChangesAsync(ct);
            if (m.Tipo == TipoMovimiento.Gasto) await Cartera.ComprobarPresupuesto(db, avisos, m.Categoria, m.Fecha, log, ct);
            return Results.Ok(m);
        });

        g.MapDelete("/movimientos/{id:int}", async (int id, AppDbContext db) =>
        {
            var m = await db.Movimientos.FindAsync(id);
            if (m is null) return Results.NotFound();
            db.Movimientos.Remove(m);
            if (await db.Adjuntos.FindAsync($"ticket:{id}") is { } t) db.Adjuntos.Remove(t);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---- Foto del ticket de un apunte ----
        g.MapPut("/movimientos/{id:int}/ticket", async (int id, HttpRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var m = await db.Movimientos.FindAsync([id], ct);
            if (m is null) return Results.NotFound();
            var (datos, tipo, error) = await Adjunto.Leer(req, ct);
            if (error is not null) return error;
            var clave = $"ticket:{id}";
            var a = await db.Adjuntos.FindAsync([clave], ct);
            if (a is null) db.Adjuntos.Add(a = new Adjunto { Clave = clave });
            a.Tipo = tipo;
            a.Datos = datos!;
            a.Creado = DateTimeOffset.UtcNow;
            m.TieneTicket = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).DisableAntiforgery();

        g.MapGet("/movimientos/{id:int}/ticket", async (int id, AppDbContext db, CancellationToken ct) =>
            await db.Adjuntos.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == $"ticket:{id}", ct) is { } a
                ? Results.File(a.Datos, a.Tipo) : Results.NotFound());

        g.MapDelete("/movimientos/{id:int}/ticket", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var m = await db.Movimientos.FindAsync([id], ct);
            if (m is null) return Results.NotFound();
            if (await db.Adjuntos.FindAsync([$"ticket:{id}"], ct) is { } a) db.Adjuntos.Remove(a);
            m.TieneTicket = false;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- Fijos: suscripciones, alquiler, cuota del coche, nómina… ----
        g.MapGet("/recurrentes", async (AppDbContext db) =>
            Results.Ok((await db.Recurrentes.AsNoTracking().ToListAsync())
                .OrderByDescending(r => r.Activo).ThenBy(r => r.Tipo).ThenByDescending(r => r.Mensual)
                .Select(r => new { r.Id, r.Nombre, r.Tipo, r.Categoria, r.Importe, r.Variable, r.Dia, r.Frecuencia, r.Inicio, r.Fin, r.Activo, r.Mensual })));

        g.MapPost("/recurrentes", async (RecurrenteEntrada e, AppDbContext db) =>
        {
            if (Validar(e) is { } error) return error;
            var r = new Recurrente();
            Copiar(e, r);
            db.Recurrentes.Add(r);
            await db.SaveChangesAsync();
            return Results.Created($"/api/gastos/recurrentes/{r.Id}", r);
        });

        g.MapPut("/recurrentes/{id:int}", async (int id, RecurrenteEntrada e, AppDbContext db) =>
        {
            if (Validar(e) is { } error) return error;
            var r = await db.Recurrentes.FindAsync(id);
            if (r is null) return Results.NotFound();
            Copiar(e, r);
            await db.SaveChangesAsync();
            return Results.Ok(r);
        });

        g.MapDelete("/recurrentes/{id:int}", async (int id, AppDbContext db) =>
        {
            var r = await db.Recurrentes.FindAsync(id);
            if (r is null) return Results.NotFound();
            // los apuntes ya registrados se conservan, sin el enlace
            await db.Movimientos.Where(m => m.RecurrenteId == id).ForEachAsync(m => m.RecurrenteId = null);
            db.Recurrentes.Remove(r);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Apunta el fijo en su mes (con el importe real si es variable)
        g.MapPost("/recurrentes/{id:int}/registrar", async (int id, Registro reg, AppDbContext db, TimeProvider reloj, Zona zona) =>
        {
            var r = await db.Recurrentes.FindAsync(id);
            if (r is null) return Results.NotFound();
            var fecha = reg.Fecha ?? Cartera.Hoy(reloj, zona);
            var importe = reg.Importe ?? r.Importe;
            if (importe <= 0 || importe > 1_000_000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["importe"] = ["El importe tiene que ser mayor que 0."] });
            var desde = new DateOnly(fecha.Year, fecha.Month, 1);
            var hasta = desde.AddMonths(1);
            if (await db.Movimientos.AnyAsync(m => m.RecurrenteId == id && m.Fecha >= desde && m.Fecha < hasta))
                return Results.Conflict(new { detail = "Este fijo ya está apuntado este mes." });
            var m = new Movimiento { Tipo = r.Tipo, Fecha = fecha, Importe = importe, Categoria = r.Categoria, Concepto = r.Nombre, RecurrenteId = r.Id };
            db.Movimientos.Add(m);
            await db.SaveChangesAsync();
            return Results.Created($"/api/gastos/movimientos/{m.Id}", m);
        });
    }

    // ---- Validación ----
    // crear un apunte (lo usan la API y el bot de Telegram)
    internal static async Task<IResult> Crear(MovimientoEntrada e, AppDbContext db, IAvisos avisos, ILogger log, CancellationToken ct)
    {
        if (Validar(e) is { } error) return error;
        if (await FijoInexistente(e, db, ct) is { } sinFijo) return sinFijo;
        var m = new Movimiento();
        Copiar(e, m);
        if (e.Repite is { } f && e.RecurrenteId is null)
        {
            var r = new Recurrente
            {
                Nombre = m.Concepto.Length > 80 ? m.Concepto[..80] : m.Concepto, Tipo = m.Tipo, Categoria = m.Categoria, Importe = m.Importe,
                Variable = e.Variable, Dia = m.Fecha.Day, Frecuencia = f, Inicio = m.Fecha,
            };
            db.Recurrentes.Add(r);
            m.Recurrente = r;
        }
        db.Movimientos.Add(m);
        await db.SaveChangesAsync(ct);
        if (m.Tipo == TipoMovimiento.Gasto) await Cartera.ComprobarPresupuesto(db, avisos, m.Categoria, m.Fecha, log, ct);
        return Results.Created($"/api/gastos/movimientos/{m.Id}", m);
    }

    // un RecurrenteId que no existe daría un error de clave foránea (500): se avisa como validación
    private static async Task<IResult?> FijoInexistente(MovimientoEntrada e, AppDbContext db, CancellationToken ct) =>
        e.RecurrenteId is { } rid && !await db.Recurrentes.AnyAsync(r => r.Id == rid, ct)
            ? Results.ValidationProblem(new Dictionary<string, string[]> { ["recurrenteId"] = ["Ese fijo no existe."] })
            : null;

    private static IResult? Validar(MovimientoEntrada e)
    {
        var errores = new Dictionary<string, string[]>();
        var importe = Importe(e);
        if (importe <= 0 || importe > 1_000_000) errores["importe"] = ["El importe tiene que ser mayor que 0."];
        if (!Categorias.Valida(e.Categoria, e.Tipo)) errores["categoria"] = ["Esa categoría no existe para este tipo."];
        if (string.IsNullOrWhiteSpace(e.Concepto) || e.Concepto.Length > 120) errores["concepto"] = ["Escribe un concepto (máximo 120 caracteres)."];
        if (e.Notas?.Length > 500) errores["notas"] = ["Las notas no pueden pasar de 500 caracteres."];
        if (e.Litros is <= 0 or > 500) errores["litros"] = ["Los litros tienen que estar entre 0 y 500."];
        if (e.PrecioLitro is <= 0 or > 10) errores["precioLitro"] = ["El precio por litro no parece correcto."];
        if (e.Kilometros is < 0 or > 5_000_000) errores["kilometros"] = ["Los kilómetros no parecen correctos."];
        return errores.Count > 0 ? Results.ValidationProblem(errores) : null;
    }

    private static IResult? Validar(RecurrenteEntrada e)
    {
        var errores = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Length > 80) errores["nombre"] = ["Escribe un nombre (máximo 80 caracteres)."];
        if (e.Importe <= 0 || e.Importe > 1_000_000) errores["importe"] = ["El importe tiene que ser mayor que 0."];
        if (!Categorias.Valida(e.Categoria, e.Tipo)) errores["categoria"] = ["Esa categoría no existe para este tipo."];
        if (e.Dia is < 1 or > 31) errores["dia"] = ["El día tiene que estar entre 1 y 31."];
        if (e.Fin is { } f && f < e.Inicio) errores["fin"] = ["La fecha de fin es anterior al inicio."];
        return errores.Count > 0 ? Results.ValidationProblem(errores) : null;
    }

    // en la gasolina, si no hay importe se calcula con litros × precio
    // (siempre a céntimos: 0,001 € no es un importe válido)
    private static decimal Importe(MovimientoEntrada e) => Math.Round(
        e.Importe > 0 ? e.Importe : e.Litros is { } l && e.PrecioLitro is { } p ? l * p : e.Importe, 2, MidpointRounding.AwayFromZero);

    private static void Copiar(MovimientoEntrada e, Movimiento m)
    {
        m.Tipo = e.Tipo;
        m.Fecha = e.Fecha;
        m.Importe = Importe(e);
        m.Categoria = e.Categoria;
        m.Concepto = e.Concepto.Trim();
        m.Notas = string.IsNullOrWhiteSpace(e.Notas) ? null : e.Notas.Trim();
        m.RecurrenteId = e.RecurrenteId;
        var gasolina = e.Categoria == "gasolina";
        m.Litros = gasolina ? e.Litros : null;
        m.PrecioLitro = gasolina ? e.PrecioLitro ?? (e.Litros is > 0 ? Math.Round(m.Importe / e.Litros.Value, 3, MidpointRounding.AwayFromZero) : null) : null;
        m.Kilometros = gasolina ? e.Kilometros : null;
    }

    private static void Copiar(RecurrenteEntrada e, Recurrente r)
    {
        r.Nombre = e.Nombre.Trim();
        r.Tipo = e.Tipo;
        r.Categoria = e.Categoria;
        r.Importe = e.Importe;
        r.Variable = e.Variable;
        r.Dia = e.Dia;
        r.Frecuencia = e.Frecuencia;
        r.Inicio = e.Inicio;
        r.Fin = e.Fin;
        r.Activo = e.Activo;
    }
}
