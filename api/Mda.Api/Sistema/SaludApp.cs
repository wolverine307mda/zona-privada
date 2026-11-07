using System.Diagnostics;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Domotica;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Sistema;

// Salud de la propia app: cuánto pesa la base y qué tablas crecen más, la última copia, los errores recientes y la memoria; con una foto diaria
// de los tamaños para ver qué crece y a qué ritmo, y acciones de mantenimiento con vista previa y confirmación. Sirve igual con Docker que en la versión de escritorio.
public class TamanoTabla
{
    public DateOnly Dia { get; set; }
    public string Tabla { get; set; } = "";
    public long Bytes { get; set; }
}

public record FilaTabla(string Tabla, long Bytes, long Filas);
public record CrecimientoTabla(string Tabla, long Bytes, long BytesPorDia, int DiasMedidos);
public record Recomendacion(string Id, string Texto);
public record AccionMantenimiento(string Id, string Nombre, string Descripcion, long Afecta, string Unidad, bool Disponible);

public static class SaludApp
{
    public const int DiasFotos = 120, UmbralCrecimientoMb = 20, UmbralTablaMb = 200;
    public const string Reautenticada = "reauth";

    // Pura: bytes que gana (o pierde) cada tabla al día entre su foto más vieja y la más nueva. Con una sola foto no hay tendencia.
    public static List<CrecimientoTabla> Crecimiento(IEnumerable<TamanoTabla> fotos)
    {
        var l = new List<CrecimientoTabla>();
        foreach (var g in fotos.GroupBy(f => f.Tabla))
        {
            var o = g.OrderBy(f => f.Dia).ToList();
            var dias = o[^1].Dia.DayNumber - o[0].Dia.DayNumber;
            l.Add(new CrecimientoTabla(g.Key, o[^1].Bytes, dias > 0 ? (o[^1].Bytes - o[0].Bytes) / dias : 0, dias));
        }
        return l.OrderByDescending(c => c.BytesPorDia).ThenByDescending(c => c.Bytes).ToList();
    }

    private static string Mb(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.#} GB" : $"{b / 1048576.0:0.#} MB";

    // Pura: lo que merece la pena mirar, escrito para un humano
    public static List<Recomendacion> Recomendaciones(IReadOnlyList<FilaTabla> tablas, IReadOnlyList<CrecimientoTabla> crecimiento, double? horasCopia, int errores10min)
    {
        var r = new List<Recomendacion>();
        foreach (var t in tablas.Where(t => t.Bytes >= UmbralTablaMb * 1048576L))
            r.Add(new("tabla:" + t.Tabla, t.Tabla switch
            {
                "casa_lecturas" => $"«{t.Tabla}» ocupa {Mb(t.Bytes)}: se resume por días y se borra a los 30 días solo, pero puedes limpiar las viejas ahora.",
                "llamadas_externas" => $"«{t.Tabla}» ocupa {Mb(t.Bytes)}: se pueden purgar las llamadas de hace más de 90 días.",
                _ => $"«{t.Tabla}» ocupa {Mb(t.Bytes)} ({t.Filas:N0} filas): es de las que más pesan.",
            }));
        var c = crecimiento.FirstOrDefault(x => x.DiasMedidos >= 3 && x.BytesPorDia >= UmbralCrecimientoMb * 1048576L);
        if (c is not null) r.Add(new("crece:" + c.Tabla, $"«{c.Tabla}» crece unos {Mb(c.BytesPorDia)} al día: al ritmo actual suma {Mb(c.BytesPorDia * 30)} en un mes."));
        if (horasCopia is null) r.Add(new("copia", "No hay copia de seguridad: haz una cuanto antes."));
        else if (horasCopia > 72) r.Add(new("copia", $"La última copia tiene {(int)(horasCopia / 24)} días: revisa que se esté haciendo."));
        if (errores10min >= 5) r.Add(new("errores", $"{errores10min} errores del servidor en los últimos 10 minutos: mira la vigilancia."));
        return r;
    }

    // Foto del día (una vez al día): el tamaño de cada tabla. Se repite sin duplicar.
    public static async Task Foto(AppDbContext db, DateOnly hoy, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || await db.TamanosTabla.AnyAsync(t => t.Dia == hoy, ct)) return;
        foreach (var f in await Tablas(db, 40, ct)) db.TamanosTabla.Add(new TamanoTabla { Dia = hoy, Tabla = f.Tabla, Bytes = f.Bytes });
        db.TamanosTabla.RemoveRange(await db.TamanosTabla.Where(t => t.Dia < hoy.AddDays(-DiasFotos)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    // Las tablas más grandes con filas aproximadas (solo PostgreSQL; en las pruebas con base en memoria no hay catálogo)
    public static async Task<List<FilaTabla>> Tablas(AppDbContext db, int cuantas, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return [];
        return await db.Database.SqlQuery<FilaTabla>($"""
            SELECT c.relname AS "Tabla", pg_total_relation_size(c.oid) AS "Bytes", GREATEST(c.reltuples, 0)::bigint AS "Filas"
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r' AND n.nspname = 'public'
            ORDER BY 2 DESC LIMIT {cuantas}
            """).ToListAsync(ct);
    }

    public static async Task<long?> TamanoBase(AppDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return null;
        return await db.Database.SqlQuery<long>($"SELECT pg_database_size(current_database()) AS \"Value\"").SingleAsync(ct);
    }

    // Vista previa de cada acción: cuántas filas se borrarían (sin tocar nada)
    public static async Task<List<AccionMantenimiento>> Acciones(AppDbContext db, DateTimeOffset ahora, CancellationToken ct)
    {
        var viejas = await db.LlamadasExternas.CountAsync(l => l.Fecha < ahora.AddDays(-90), ct);
        var lecturas = await db.Lecturas.CountAsync(l => l.Fecha < ahora - ServicioCasa.Historia, ct);
        var pg = db.Database.IsNpgsql();
        return
        [
            new("llamadas", "Purgar llamadas externas antiguas", "Borra el registro de llamadas externas (atajos, agente…) de hace más de 90 días.", viejas, "filas", viejas > 0),
            new("lecturas", "Limpiar lecturas viejas de la casa", "Borra las lecturas de sensores de hace más de 30 días (el resumen por días se conserva).", lecturas, "filas", lecturas > 0),
            new("vacuum", "Ordenar la base (VACUUM ANALYZE)", "Recupera espacio y actualiza las estadísticas. No bloquea: nunca se ofrece VACUUM FULL.", 0, "", pg),
        ];
    }

    // Borra por tandas de 5.000 (sirve con PostgreSQL y con la base en memoria de las pruebas, y no carga millones de filas de golpe)
    private static async Task<int> Borrar<T>(AppDbContext db, IQueryable<T> consulta, CancellationToken ct) where T : class
    {
        var total = 0;
        while (true)
        {
            var tanda = await consulta.Take(5000).ToListAsync(ct);
            if (tanda.Count == 0) return total;
            db.RemoveRange(tanda);
            await db.SaveChangesAsync(ct);
            total += tanda.Count;
        }
    }

    public static async Task<(bool Ok, string Mensaje)> Ejecutar(string id, AppDbContext db, DateTimeOffset ahora, CancellationToken ct)
    {
        switch (id)
        {
            case "llamadas":
                var n = await Borrar(db, db.LlamadasExternas.Where(l => l.Fecha < ahora.AddDays(-90)), ct);
                return (true, $"Borradas {n:N0} llamadas antiguas.");
            case "lecturas":
                var m = await Borrar(db, db.Lecturas.Where(l => l.Fecha < ahora - ServicioCasa.Historia), ct);
                return (true, $"Borradas {m:N0} lecturas viejas.");
            case "vacuum":
                if (!db.Database.IsNpgsql()) return (false, "Solo con PostgreSQL.");
                await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE)", ct);
                return (true, "Base ordenada.");
            default: return (false, "Esa acción no existe.");
        }
    }

    public static void MapSaludApp(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sistema/app").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, Vigilante vigilante, RegistroVigilancia registro, TimeProvider reloj, CancellationToken ct) =>
        {
            var ahora = reloj.GetUtcNow();
            var tablas = await Tablas(db, 10, ct);
            var fotos = await db.TamanosTabla.AsNoTracking().ToListAsync(ct);
            var crecimiento = Crecimiento(fotos).Take(10).ToList();
            var copia = vigilante.UltimaCopia();
            var horasCopia = copia.Fecha is { } f ? (double?)(ahora - f).TotalHours : null;
            var errores = registro.Errores5xx(TimeSpan.FromMinutes(10));
            var proceso = Process.GetCurrentProcess();
            return Results.Ok(new
            {
                baseBytes = await TamanoBase(db, ct),
                tablas, crecimiento,
                copia = new { copia.Vigilada, fecha = copia.Fecha, horas = horasCopia, copia.Tamano },
                errores10min = errores,
                memoriaMb = Math.Round(proceso.WorkingSet64 / 1048576.0),
                enMarcha = ahora - new DateTimeOffset(proceso.StartTime.ToUniversalTime()),
                recomendaciones = Recomendaciones(tablas, crecimiento, horasCopia, errores),
                motor = db.Database.IsNpgsql() ? "postgresql" : "memoria",
            });
        });

        // la vista previa de cada acción de mantenimiento
        g.MapGet("/mantenimiento", async (AppDbContext db, TimeProvider reloj, CancellationToken ct) => Results.Ok(await Acciones(db, reloj.GetUtcNow(), ct)));

        // se ejecuta solo con confirmación explícita y la contraseña otra vez
        g.MapPost("/mantenimiento/{id}", async (string id, bool? confirmar, AppDbContext db, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            if (confirmar != true) return Results.ValidationProblem(new Dictionary<string, string[]> { ["confirmar"] = ["Falta confirmar: mira primero la vista previa."] });
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            var acciones = await Acciones(db, reloj.GetUtcNow(), ct);
            if (acciones.FirstOrDefault(a => a.Id == id) is not { Disponible: true }) return Results.Conflict(new { detalle = "Esa acción no se puede hacer ahora (no hay nada que limpiar o no hay PostgreSQL)." });
            var (ok, mensaje) = await Ejecutar(id, db, reloj.GetUtcNow(), ct);
            return ok ? Results.Ok(new { mensaje }) : Results.Conflict(new { detalle = mensaje });
        }).RequireRateLimiting(Limites.Sensible);
    }
}
