using System.Text.Json;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Domotica;

public record EstanciaEntrada(string? Nombre, string? Icono);
public record AparatoEntrada(string? Nombre, string? Tipo, string? Driver, int? EstanciaId, string? DeviceId = null, string? InfraredId = null, string? RemoteId = null);
public record OrdenCasa(string? Clave, JsonElement Valor);
public record PanelEntrada(JsonElement Panel);

public static class CasaEndpoints
{
    public const int MaxEstancias = 20, MaxAparatos = 60, MaxPanel = 40_000;

    private static IResult Mal(string campo, string texto) => Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [texto] });

    public static void MapDomotica(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/domotica").RequireAuthorization();

        g.MapGet("/catalogo", () => new
        {
            tipos = Catalogo.Tipos.Select(t => new { id = t.Key, nombre = t.Value, campos = Catalogo.Campos(t.Key) }),
            drivers = Catalogo.Drivers,
        });

        // todo lo de la casa de una vez: habitaciones, aparatos con sus datos y cómo está colocado el panel
        g.MapGet("/", async (AppDbContext db, ServicioCasa casa, TimeProvider reloj, CancellationToken ct) =>
        {
            var aparatos = await db.Aparatos.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
            var entidades = await db.Entidades.AsNoTracking().ToListAsync(ct);
            // datos viejos (más de 20 s): se relee al momento, sin esperar al minuto del vigilante
            var viejo = reloj.GetUtcNow().AddSeconds(-20);
            foreach (var a in aparatos.Where(a => entidades.Where(e => e.AparatoId == a.Id).Any(e => e.Actualizado is null || e.Actualizado < viejo) || !entidades.Any(e => e.AparatoId == a.Id)))
                if (await casa.Refrescar(db, a, ct)) entidades = [.. entidades.Where(e => e.AparatoId != a.Id), .. await db.Entidades.AsNoTracking().Where(e => e.AparatoId == a.Id).ToListAsync(ct)];

            return Results.Ok(new
            {
                estancias = await db.Estancias.AsNoTracking().OrderBy(e => e.Orden).ThenBy(e => e.Id).ToListAsync(ct),
                aparatos = aparatos.Select(a => new
                {
                    a.Id, a.Nombre, a.Tipo, a.Driver, a.EstanciaId, tipoNombre = Catalogo.Tipos.GetValueOrDefault(a.Tipo, a.Tipo),
                    entidades = Catalogo.Campos(a.Tipo).Select(c =>
                    {
                        var e = entidades.FirstOrDefault(x => x.AparatoId == a.Id && x.Clave == c.Clave);
                        return new { c.Clave, c.Tipo, c.Etiqueta, c.Unidad, c.Escribible, c.Min, c.Max, c.Opciones, valor = e?.Valor, actualizado = e?.Actualizado };
                    }),
                }),
                panel = (await db.Paneles.AsNoTracking().FirstOrDefaultAsync(p => p.Clave == "principal", ct))?.Json,
            });
        });

        // ---- habitaciones ----
        g.MapPost("/estancias", async (EstanciaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 30) return Mal("nombre", "Ponle un nombre (hasta 30 letras).");
            if ((e.Icono ?? "").Length > 16) return Mal("icono", "El icono es un emoji.");
            if (await db.Estancias.CountAsync(ct) >= MaxEstancias) return Mal("nombre", $"Máximo {MaxEstancias} habitaciones.");
            var x = new Estancia { Nombre = e.Nombre.Trim(), Icono = string.IsNullOrWhiteSpace(e.Icono) ? "🏠" : e.Icono.Trim(), Orden = await db.Estancias.CountAsync(ct) };
            db.Estancias.Add(x);
            await db.SaveChangesAsync(ct);
            return Results.Ok(x);
        });

        g.MapPut("/estancias/{id:int}", async (int id, EstanciaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Estancias.FindAsync([id], ct) is not { } x) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 30) return Mal("nombre", "Ponle un nombre (hasta 30 letras).");
            x.Nombre = e.Nombre.Trim();
            if (!string.IsNullOrWhiteSpace(e.Icono) && e.Icono.Length <= 16) x.Icono = e.Icono.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(x);
        });

        // el orden nuevo de las habitaciones: la lista de ids, de la primera a la última
        g.MapPut("/estancias/orden", async (int[] ids, AppDbContext db, CancellationToken ct) =>
        {
            var todas = await db.Estancias.ToListAsync(ct);
            for (var i = 0; i < ids.Length; i++) if (todas.FirstOrDefault(e => e.Id == ids[i]) is { } x) x.Orden = i;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("/estancias/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Estancias.FindAsync([id], ct) is not { } x) return Results.NotFound();
            foreach (var a in await db.Aparatos.Where(a => a.EstanciaId == id).ToListAsync(ct)) a.EstanciaId = null; // los aparatos se quedan, sin habitación
            db.Estancias.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- aparatos ----
        g.MapPost("/aparatos", async (AparatoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 40) return Mal("nombre", "Ponle un nombre (hasta 40 letras).");
            if (e.Tipo is null || !Catalogo.Tipos.ContainsKey(e.Tipo)) return Mal("tipo", "Tipo de aparato desconocido.");
            if (e.Driver is not null && !Catalogo.Drivers.Contains(e.Driver)) return Mal("driver", "Conector desconocido.");
            if (e.EstanciaId is { } eid && !await db.Estancias.AnyAsync(x => x.Id == eid, ct)) return Mal("estanciaId", "Esa habitación no existe.");
            if (await db.Aparatos.CountAsync(ct) >= MaxAparatos) return Mal("nombre", $"Máximo {MaxAparatos} aparatos.");
            var a = new Aparato { Nombre = e.Nombre.Trim(), Tipo = e.Tipo, Driver = e.Driver ?? "demo", EstanciaId = e.EstanciaId };
            if (a.Driver == "tuya") a.Config = new ConfigTuya(e.DeviceId?.Trim(), e.InfraredId?.Trim(), e.RemoteId?.Trim()).Json();
            db.Aparatos.Add(a);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { a.Id, a.Nombre, a.Tipo, a.Driver, a.EstanciaId });
        });

        g.MapPut("/aparatos/{id:int}", async (int id, AparatoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Aparatos.FindAsync([id], ct) is not { } a) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 40) return Mal("nombre", "Ponle un nombre (hasta 40 letras).");
            if (e.EstanciaId is { } eid && !await db.Estancias.AnyAsync(x => x.Id == eid, ct)) return Mal("estanciaId", "Esa habitación no existe.");
            a.Nombre = e.Nombre.Trim();
            a.EstanciaId = e.EstanciaId;
            if (a.Driver == "tuya" && (e.DeviceId is not null || e.InfraredId is not null || e.RemoteId is not null))
            {
                var antes = ConfigTuya.Leer(a.Config);
                a.Config = new ConfigTuya(e.DeviceId?.Trim() ?? antes.DeviceId, e.InfraredId?.Trim() ?? antes.InfraredId, e.RemoteId?.Trim() ?? antes.RemoteId).Json();
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { a.Id, a.Nombre, a.Tipo, a.Driver, a.EstanciaId });
        });

        g.MapDelete("/aparatos/{id:int}", async (int id, AppDbContext db, IEnumerable<IDriverCasa> drivers, CancellationToken ct) =>
        {
            if (await db.Aparatos.FindAsync([id], ct) is not { } a) return Results.NotFound();
            db.Aparatos.Remove(a);
            await db.SaveChangesAsync(ct);
            foreach (var d in drivers.OfType<DriverDemo>()) d.Olvidar(id);
            return Results.NoContent();
        });

        // una orden a un aparato: {clave: "encendido", valor: true}
        g.MapPost("/aparatos/{id:int}/ejecutar", async (int id, OrdenCasa o, AppDbContext db, ServicioCasa casa, CancellationToken ct) =>
        {
            if (await db.Aparatos.FindAsync([id], ct) is not { } a) return Results.NotFound();
            var bruto = o.Valor.ValueKind switch
            {
                JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Number => o.Valor.GetRawText(),
                JsonValueKind.String => o.Valor.GetString(), _ => null,
            };
            var (ok, mensaje, codigo) = await casa.Ejecutar(db, a, o.Clave ?? "", bruto, ct);
            return ok ? Results.Ok(new { mensaje }) : codigo == 400 ? Mal("valor", mensaje) : Results.Problem(mensaje, statusCode: codigo);
        });

        // las lecturas de un dato numérico de las últimas horas (para la gráfica)
        g.MapGet("/aparatos/{id:int}/historial", async (int id, string clave, int? horas, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            var desde = reloj.GetUtcNow().AddHours(-Math.Clamp(horas ?? 24, 1, 24 * 30));
            var e = await db.Entidades.AsNoTracking().FirstOrDefaultAsync(x => x.AparatoId == id && x.Clave == clave, ct);
            if (e is null) return Results.NotFound();
            var puntos = await db.Lecturas.AsNoTracking().Where(l => l.EntidadId == e.Id && l.Fecha >= desde).OrderBy(l => l.Fecha).Select(l => new { fecha = l.Fecha, valor = l.Valor }).ToListAsync(ct);
            return Results.Ok(puntos);
        });

        // los aparatos de tu cuenta de Tuya, para elegirlos al añadir uno (necesita las claves de Seguridad → Claves)
        g.MapGet("/tuya/dispositivos", async (ClienteTuya tuya, CancellationToken ct) =>
        {
            if (!tuya.Configurado) return Results.Conflict(new { detalle = "Primero pon las claves de Tuya en Seguridad → Claves." });
            try { return Results.Ok(await tuya.Listar(ct)); }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException) { return Results.Problem(e.Message, statusCode: StatusCodes.Status502BadGateway); }
        });

        // ---- cómo está colocado el panel ----
        g.MapPut("/panel", async (PanelEntrada p, AppDbContext db, CancellationToken ct) =>
        {
            var json = p.Panel.GetRawText();
            if (json.Length > MaxPanel) return Mal("panel", "El panel es demasiado grande.");
            var fila = await db.Paneles.FirstOrDefaultAsync(x => x.Clave == "principal", ct);
            if (fila is null) db.Paneles.Add(new PanelCasa { Clave = "principal", Json = json }); else fila.Json = json;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // aparatos de ejemplo para probar la pantalla (Salón y Dormitorio); se pueden quitar uno a uno
        g.MapPost("/ejemplo", async (AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Aparatos.AnyAsync(ct)) return Results.Conflict(new { detalle = "Ya hay aparatos: el ejemplo solo se carga con la casa vacía." });
            var salon = new Estancia { Nombre = "Salón", Icono = "🛋️", Orden = 0 };
            var dormitorio = new Estancia { Nombre = "Dormitorio", Icono = "🛏️", Orden = 1 };
            db.Estancias.AddRange(salon, dormitorio);
            await db.SaveChangesAsync(ct);
            db.Aparatos.AddRange(
                new Aparato { Nombre = "Termómetro", Tipo = "sensor", EstanciaId = salon.Id },
                new Aparato { Nombre = "Puerta de entrada", Tipo = "puerta", EstanciaId = salon.Id },
                new Aparato { Nombre = "Lámpara del salón", Tipo = "luz", EstanciaId = salon.Id },
                new Aparato { Nombre = "Aire acondicionado", Tipo = "clima", EstanciaId = salon.Id },
                new Aparato { Nombre = "Televisión", Tipo = "tv", EstanciaId = salon.Id },
                new Aparato { Nombre = "Altavoz", Tipo = "altavoz", EstanciaId = salon.Id },
                new Aparato { Nombre = "Lámpara de la cama", Tipo = "luz", EstanciaId = dormitorio.Id },
                new Aparato { Nombre = "Ventilador", Tipo = "enchufe", EstanciaId = dormitorio.Id });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
