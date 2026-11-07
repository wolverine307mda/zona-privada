using System.Globalization;
using Mda.Api.Agenda;
using Mda.Api.Compra;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Cocina;

public static class CocinaEndpoints
{
    public static void MapCocina(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cocina").RequireAuthorization();
        static DateOnly Hoy(TimeProvider r, Zona z) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.GetUtcNow(), z.Info).Date);
        static DateOnly? Fecha(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        // el lunes de la semana de esa fecha
        static DateOnly Lunes(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

        // ---- recetas ----
        g.MapGet("/recetas", async (AppDbContext db, CancellationToken ct) =>
        {
            var l = await db.Recetas.AsNoTracking().OrderBy(r => r.Nombre).ToListAsync(ct);
            var ing = (await db.IngredientesReceta.AsNoTracking().ToListAsync(ct)).ToLookup(i => i.RecetaId);
            return Results.Ok(l.Select(r => CocinaServicio.Dto(r, ing[r.Id])));
        });

        g.MapPost("/recetas", async (RecetaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (CocinaServicio.Validar(e, true) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.Recetas.CountAsync(ct) >= CocinaServicio.MaxRecetas) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Como mucho {CocinaServicio.MaxRecetas} recetas."] });
            var r = new Receta(); db.Recetas.Add(r);
            await CocinaServicio.Aplicar(db, r, e with { Ingredientes = null }, ct);
            await db.SaveChangesAsync(ct);
            await CocinaServicio.Aplicar(db, r, new RecetaEntrada(null, null, null, null, null, null, e.Ingredientes ?? []), ct);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/cocina/recetas/{r.Id}", CocinaServicio.Dto(r, await db.IngredientesReceta.AsNoTracking().Where(i => i.RecetaId == r.Id).ToListAsync(ct)));
        });

        g.MapPut("/recetas/{id:int}", async (int id, RecetaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (CocinaServicio.Validar(e, false) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var r = await db.Recetas.FindAsync([id], ct);
            if (r is null) return Results.NotFound();
            await CocinaServicio.Aplicar(db, r, e, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(CocinaServicio.Dto(r, await db.IngredientesReceta.AsNoTracking().Where(i => i.RecetaId == id).ToListAsync(ct)));
        });

        g.MapDelete("/recetas/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var r = await db.Recetas.FindAsync([id], ct);
            if (r is null) return Results.NotFound();
            db.IngredientesReceta.RemoveRange(await db.IngredientesReceta.Where(i => i.RecetaId == id).ToListAsync(ct));
            foreach (var m in await db.MenuDias.Where(m => m.RecetaId == id).ToListAsync(ct)) { m.RecetaId = null; m.Texto = r.Nombre; }       // el menú no se queda cojo: pasa a texto
            db.Recetas.Remove(r);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // leer una receta de una página web (JSON-LD de schema.org) y guardarla; se puede corregir después
        g.MapPost("/recetas/desde-web", async (DesdeWeb e, ServicioRecetas servicio, AppDbContext db, CancellationToken ct) =>
        {
            var (leida, error) = await servicio.Leer(e.Url ?? "", ct);
            if (leida is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = [error ?? "No se pudo leer."] });
            if (await db.Recetas.CountAsync(ct) >= CocinaServicio.MaxRecetas) return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["Ya hay demasiadas recetas."] });
            var r = new Receta { Nombre = leida.Nombre, Raciones = leida.Raciones, Minutos = leida.Minutos, Etiquetas = CocinaServicio.Etiquetas(string.Join(',', leida.Etiquetas)), Pasos = string.Join('\n', leida.Pasos), Origen = (e.Url ?? "").Trim()[..Math.Min((e.Url ?? "").Trim().Length, 300)] };
            db.Recetas.Add(r);
            await db.SaveChangesAsync(ct);
            db.IngredientesReceta.AddRange(leida.Ingredientes.Select(i => new IngredienteReceta { RecetaId = r.Id, Nombre = i.Nombre[..Math.Min(i.Nombre.Length, 120)], Cantidad = i.Cantidad, Unidad = i.Unidad, Texto = i.Texto[..Math.Min(i.Texto.Length, 200)] }));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/cocina/recetas/{r.Id}", CocinaServicio.Dto(r, await db.IngredientesReceta.AsNoTracking().Where(i => i.RecetaId == r.Id).ToListAsync(ct)));
        }).RequireRateLimiting(Mda.Api.Seguridad.Limites.Sensible);

        // ---- ajustes: alergias y hora del aviso ----
        g.MapGet("/ajustes", async (AppDbContext db, CancellationToken ct) => Results.Ok(await CocinaServicio.LeerAjustes(db, ct)));
        g.MapPut("/ajustes", async (AjustesCocina e, AppDbContext db, CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(e.HoraAviso) && !TimeOnly.TryParseExact(e.HoraAviso, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["horaAviso"] = ["La hora va como 09:00."] });
            var a = new AjustesCocina((e.Alergias ?? []).Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length is > 0 and <= 40).Distinct().Take(20).ToList(), e.HoraAviso ?? "");
            await CocinaServicio.GuardarAjustes(db, a, ct);
            return Results.Ok(a);
        });

        // ---- menú ----
        g.MapGet("/menu", async (string? desde, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var lunes = Lunes(Fecha(desde) ?? Hoy(reloj, zona));
            var fin = lunes.AddDays(6);
            var dias = await db.MenuDias.AsNoTracking().Where(m => m.Fecha >= lunes && m.Fecha <= fin).ToListAsync(ct);
            var nombres = await db.Recetas.AsNoTracking().Where(r => dias.Select(d => d.RecetaId).Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Nombre, ct);
            return Results.Ok(new
            {
                desde = lunes.ToString("yyyy-MM-dd"),
                celdas = dias.Select(m => new { fecha = m.Fecha.ToString("yyyy-MM-dd"), m.Momento, m.RecetaId, receta = m.RecetaId is { } id && nombres.TryGetValue(id, out var n) ? n : null, m.Texto, m.Sobras }),
            });
        });

        g.MapPut("/menu/{fecha}/{momento}", async (string fecha, string momento, CeldaMenu e, AppDbContext db, CancellationToken ct) =>
        {
            if (Fecha(fecha) is not { } f || momento is not ("comida" or "cena")) return Results.ValidationProblem(new Dictionary<string, string[]> { ["fecha"] = ["Fecha o momento no válidos."] });
            if (e.RecetaId is { } rid && !await db.Recetas.AnyAsync(r => r.Id == rid, ct)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["recetaId"] = ["Esa receta no existe."] });
            var m = await db.MenuDias.FirstOrDefaultAsync(x => x.Fecha == f && x.Momento == momento, ct);
            if (e.RecetaId is null && string.IsNullOrWhiteSpace(e.Texto)) { if (m is not null) db.MenuDias.Remove(m); await db.SaveChangesAsync(ct); return Results.NoContent(); }
            if (m is null) db.MenuDias.Add(m = new MenuDia { Fecha = f, Momento = momento });
            m.RecetaId = e.RecetaId; m.Texto = (e.Texto ?? "").Trim()[..Math.Min((e.Texto ?? "").Trim().Length, 120)]; m.Sobras = e.Sobras ?? false;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { m.Id });
        });

        // genera la semana por reglas (sin IA): etiquetas pedidas, alergias, gustos, días de sobras; no pisa lo ya puesto salvo que se pida
        g.MapPost("/menu/generar", async (GenerarEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var lunes = Lunes(Fecha(e.Desde) ?? Hoy(reloj, zona));
            var ajustes = await CocinaServicio.LeerAjustes(db, ct);
            var recetas = await db.Recetas.AsNoTracking().ToListAsync(ct);
            var ing = (await db.IngredientesReceta.AsNoTracking().ToListAsync(ct)).ToLookup(i => i.RecetaId);
            var ultima = (await db.MenuDias.AsNoTracking().Where(m => m.RecetaId != null && m.Fecha < lunes && m.Fecha >= lunes.AddDays(-MenuSemanal.DiasSinRepetir)).ToListAsync(ct))
                .GroupBy(m => m.RecetaId!.Value).ToDictionary(x => x.Key, x => x.Max(m => m.Fecha));
            var gustos = (await db.Personas.AsNoTracking().Select(p => p.Gustos).ToListAsync(ct)).SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries)).Distinct().Take(30).ToList();
            var generado = MenuSemanal.Generar(recetas.Select(r => new RecetaMenu(r.Id, r.Nombre, r.Etiquetas.Split(',', StringSplitOptions.RemoveEmptyEntries), ing[r.Id].Select(i => i.Nombre).ToArray())).ToList(),
                ultima, lunes, e.Etiquetas ?? [], [.. ajustes.Alergias ?? [], .. e.Alergias ?? []], [], Math.Clamp(e.Sobras ?? 0, 0, 3), lunes.DayNumber);
            if (generado.Count == 0) return Results.Conflict(new { detalle = "No hay recetas que cumplan eso (etiquetas, alergias…). Añade más recetas o relaja los filtros." });
            var existentes = await db.MenuDias.Where(m => m.Fecha >= lunes && m.Fecha <= lunes.AddDays(6)).ToListAsync(ct);
            if (e.Reemplazar == true) db.MenuDias.RemoveRange(existentes);
            var puestas = 0;
            foreach (var c in generado)
            {
                if (e.Reemplazar != true && existentes.Any(x => x.Fecha == c.Fecha && x.Momento == c.Momento)) continue;
                db.MenuDias.Add(new MenuDia { Fecha = c.Fecha, Momento = c.Momento, RecetaId = c.RecetaId, Sobras = c.Sobras }); puestas++;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { puestas, desde = lunes.ToString("yyyy-MM-dd") });
        });

        // la lista de la compra de la semana: suma los ingredientes (ajustados a las raciones), y lo manda a Compra sin repetir lo que ya está pendiente
        g.MapPost("/menu/compra", async (string? desde, int? personas, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var lunes = Lunes(Fecha(desde) ?? Hoy(reloj, zona));
            var dias = await db.MenuDias.AsNoTracking().Where(m => m.Fecha >= lunes && m.Fecha <= lunes.AddDays(6) && m.RecetaId != null && !m.Sobras).ToListAsync(ct);
            if (dias.Count == 0) return Results.Conflict(new { detalle = "El menú de esa semana no tiene recetas." });
            var recetas = await db.Recetas.AsNoTracking().ToDictionaryAsync(r => r.Id, ct);
            var ing = (await db.IngredientesReceta.AsNoTracking().ToListAsync(ct)).ToLookup(i => i.RecetaId);
            var objetivo = Math.Clamp(personas ?? 0, 0, 20);
            var todos = new List<(IngredienteLeido, double)>();
            foreach (var d in dias)
            {
                if (!recetas.TryGetValue(d.RecetaId!.Value, out var r)) continue;
                var factor = objetivo > 0 ? (double)objetivo / Math.Max(1, r.Raciones) : 1;
                todos.AddRange(ing[r.Id].Select(i => (new IngredienteLeido(i.Nombre, i.Cantidad, i.Unidad, i.Texto), factor)));
            }
            var pendientes = (await db.ArticulosCompra.AsNoTracking().Where(a => !a.Comprado).ToListAsync(ct)).Select(a => Ingrediente.Clave(a.Nombre)).ToHashSet();
            int añadidos = 0, yaEstaban = 0;
            foreach (var (nombre, cantidad) in Ingrediente.Sumar(todos))
            {
                if (pendientes.Contains(Ingrediente.Clave(nombre))) { yaEstaban++; continue; }
                if (await ServicioCompra.Añadir(db, nombre, cantidad.Length > 0 ? cantidad : "al gusto", reloj, ct) is not null) añadidos++;
            }
            return Results.Ok(new { añadidos, yaEstaban });
        });
    }
}
