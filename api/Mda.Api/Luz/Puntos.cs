using Mda.Api.Agenda;
using Mda.Api.Data;
using Mda.Api.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Luz;

// Sitios donde te gusta fotografiar (con hacia dónde miras y tus notas) y el diario de salidas: lo que la previsión prometía y lo que de verdad pasó.
public class PuntoFoto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string Orientacion { get; set; } = ""; // N, NE, E… hacia donde se mira
    public string Notas { get; set; } = "";
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
}

public class SalidaFoto
{
    public int Id { get; set; }
    public DateOnly Fecha { get; set; }
    public int? PuntoId { get; set; }
    public string Tipo { get; set; } = "";     // amanecer, atardecer o noche
    public int? Prevision { get; set; }        // la nota que daba la previsión (1-10)
    public int Resultado { get; set; }         // cómo salió de verdad (1-5)
    public string Nota { get; set; } = "";
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
}

public record PuntoEntrada(string? Nombre, double? Lat, double? Lon, string? Orientacion, string? Notas);
public record SalidaEntrada(string? Fecha, int? PuntoId, string? Tipo, int? Prevision, int? Resultado, string? Nota);
public record CalibracionTipo(string Tipo, int Salidas, double Diferencia, string Texto);

public static class Puntos
{
    public static readonly string[] Orientaciones = ["", "N", "NE", "E", "SE", "S", "SO", "O", "NO"];
    public static readonly string[] Tipos = ["amanecer", "atardecer", "noche"];
    public const int Maximo = 30;

    public static Dictionary<string, string[]> Validar(PuntoEntrada e)
    {
        var mal = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 60) mal["nombre"] = ["Ponle un nombre de hasta 60 letras."];
        if (e.Lat is null or < -90 or > 90) mal["lat"] = ["La latitud va de -90 a 90."];
        if (e.Lon is null or < -180 or > 180) mal["lon"] = ["La longitud va de -180 a 180."];
        if (!Orientaciones.Contains(e.Orientacion ?? "")) mal["orientacion"] = ["Orientación no válida."];
        if ((e.Notas ?? "").Length > 500) mal["notas"] = ["Las notas, hasta 500 letras."];
        return mal;
    }

    public static Dictionary<string, string[]> Validar(SalidaEntrada e)
    {
        var mal = new Dictionary<string, string[]>();
        if (!DateOnly.TryParseExact(e.Fecha, "yyyy-MM-dd", out _)) mal["fecha"] = ["Fecha no válida (aaaa-mm-dd)."];
        if (!Tipos.Contains(e.Tipo ?? "")) mal["tipo"] = ["Elige amanecer, atardecer o noche."];
        if (e.Resultado is null or < 1 or > 5) mal["resultado"] = ["Cómo salió, de 1 a 5."];
        if (e.Prevision is < 1 or > 10) mal["prevision"] = ["La nota prevista va de 1 a 10."];
        if ((e.Nota ?? "").Length > 500) mal["nota"] = ["La nota, hasta 500 letras."];
        return mal;
    }

    // Compara la nota prevista (1-10) con lo que de verdad salió (1-5 → 2-10). Positivo = la previsión se queda corta; negativo = promete de más.
    // Hacen falta al menos 3 salidas con previsión de cada tipo para opinar.
    public static List<CalibracionTipo> Calibrar(IEnumerable<SalidaFoto> salidas) =>
        salidas.Where(s => s.Prevision is not null).GroupBy(s => s.Tipo).Where(g => g.Count() >= 3).OrderBy(g => g.Key).Select(g =>
        {
            var dif = Math.Round(g.Average(s => s.Resultado * 2 - s.Prevision!.Value), 1);
            var texto = Math.Abs(dif) < 0.8 ? "La previsión acierta bien."
                : dif > 0 ? $"Sueles sacar {Math.Abs(dif):0.#} puntos más de lo que promete la previsión."
                : $"La previsión promete {Math.Abs(dif):0.#} puntos más de los que luego salen.";
            return new CalibracionTipo(g.Key, g.Count(), dif, texto);
        }).ToList();

    // el lugar para calcular: el punto elegido o, si no hay, el de siempre
    public static async Task<Lugar?> LugarDe(int? punto, AppDbContext db, ServicioTiempo tiempo, CancellationToken ct)
    {
        if (punto is { } id && await db.PuntosFoto.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) is { } p) return new Lugar(p.Nombre, p.Lat, p.Lon);
        return await tiempo.LugarGuardado(ct);
    }

    public static void MapPuntos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/luz/puntos").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(await db.PuntosFoto.AsNoTracking().OrderBy(p => p.Nombre).ToListAsync(ct)));

        // «Hoy toca este»: la mejor ventana de hoy y mañana de cada punto, para elegir adónde ir
        g.MapGet("/hoy", async (AppDbContext db, ServicioLuz luz, Zona zona, TimeProvider reloj, CancellationToken ct) =>
        {
            var a = await ServicioLuz.Leer(db, ct) with { Amanecer = true, Astro = true };
            var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).DateTime);
            var lista = new List<object>();
            foreach (var p in await db.PuntosFoto.AsNoTracking().OrderBy(p => p.Nombre).ToListAsync(ct))
            {
                var vs = (await luz.Ventanas(hoy, new Lugar(p.Nombre, p.Lat, p.Lon), a, ct)).Concat(await luz.Ventanas(hoy.AddDays(1), new Lugar(p.Nombre, p.Lat, p.Lon), a, ct))
                    .Where(v => v.Puntuacion is not null && v.Fin > reloj.GetUtcNow()).OrderByDescending(v => v.Puntuacion).FirstOrDefault();
                lista.Add(new { punto = p, mejor = vs });
            }
            return Results.Ok(lista);
        });

        g.MapPost("/", async (PuntoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (Validar(e) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.PuntosFoto.CountAsync(ct) >= Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Como mucho {Maximo} puntos."] });
            var p = new PuntoFoto { Nombre = e.Nombre!.Trim(), Lat = Math.Round(e.Lat!.Value, 5), Lon = Math.Round(e.Lon!.Value, 5), Orientacion = e.Orientacion ?? "", Notas = (e.Notas ?? "").Trim() };
            db.PuntosFoto.Add(p);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/luz/puntos/{p.Id}", p);
        });

        g.MapPut("/{id:int}", async (int id, PuntoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.PuntosFoto.FindAsync([id], ct) is not { } p) return Results.NotFound();
            if (Validar(e) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            p.Nombre = e.Nombre!.Trim(); p.Lat = Math.Round(e.Lat!.Value, 5); p.Lon = Math.Round(e.Lon!.Value, 5); p.Orientacion = e.Orientacion ?? ""; p.Notas = (e.Notas ?? "").Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(p);
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.PuntosFoto.FindAsync([id], ct) is not { } p) return Results.NotFound();
            db.PuntosFoto.Remove(p);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var s = app.MapGroup("/api/luz/salidas").RequireAuthorization();

        s.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var salidas = await db.SalidasFoto.AsNoTracking().OrderByDescending(x => x.Fecha).ThenByDescending(x => x.Id).Take(200).ToListAsync(ct);
            return Results.Ok(new { salidas, calibracion = Calibrar(salidas) });
        });

        s.MapPost("/", async (SalidaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (Validar(e) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (e.PuntoId is { } pid && !await db.PuntosFoto.AnyAsync(p => p.Id == pid, ct)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["puntoId"] = ["Ese punto no existe."] });
            var x = new SalidaFoto { Fecha = DateOnly.ParseExact(e.Fecha!, "yyyy-MM-dd"), PuntoId = e.PuntoId, Tipo = e.Tipo!, Prevision = e.Prevision, Resultado = e.Resultado!.Value, Nota = (e.Nota ?? "").Trim() };
            db.SalidasFoto.Add(x);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/luz/salidas/{x.Id}", x);
        });

        s.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.SalidasFoto.FindAsync([id], ct) is not { } x) return Results.NotFound();
            db.SalidasFoto.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
