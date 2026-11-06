using System.Text.Json;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Domotica;

// Una escena de casa es una foto de cómo están tus aparatos (luces, aire, tele…) que se vuelve a poner con un toque: «Cine», «Dormir»…
public class EscenaCasa
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Icono { get; set; } = "✨";
    public string Acciones { get; set; } = "[]";   // JSON: [{aparatoId, clave, valor}]
}

public record AccionEscena(int AparatoId, string Clave, string Valor);
public record EscenaEntrada(string? Nombre, string? Icono, int[]? AparatoIds);

public static class EscenasCasa
{
    public const int Maximo = 12;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static List<AccionEscena> Leer(string json)
    {
        try { return JsonSerializer.Deserialize<List<AccionEscena>>(json, Web) ?? []; } catch (JsonException) { return []; }
    }

    // Lo que hay que volver a poner: los mandos de cada aparato con su valor actual (sin «reproduciendo»: una escena no pone música sola)
    public static List<AccionEscena> Foto(IEnumerable<Aparato> aparatos, IEnumerable<Entidad> entidades)
    {
        var l = new List<AccionEscena>();
        foreach (var a in aparatos)
            foreach (var c in Catalogo.Campos(a.Tipo).Where(c => c.Escribible && c.Clave != "reproduciendo"))
                if (entidades.FirstOrDefault(e => e.AparatoId == a.Id && e.Clave == c.Clave)?.Valor is { } v) l.Add(new AccionEscena(a.Id, c.Clave, v));
        return l;
    }

    // Vuelve a poner cada aparato como estaba; los que fallan se cuentan y el resto sigue (lo usan la pantalla y las escenas por contexto)
    public static async Task<(int Hechas, List<string> Fallos)> Aplicar(EscenaCasa x, AppDbContext db, ServicioCasa casa, CancellationToken ct)
    {
        var aparatos = await db.Aparatos.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
        int hechas = 0; var fallos = new List<string>();
        foreach (var a in Leer(x.Acciones))
        {
            if (!aparatos.TryGetValue(a.AparatoId, out var ap)) { fallos.Add("un aparato ya no existe"); continue; }
            var (ok, mensaje, _) = await casa.Ejecutar(db, ap, a.Clave, a.Valor, ct);
            if (ok) hechas++; else fallos.Add($"{ap.Nombre}: {mensaje}");
        }
        return (hechas, fallos);
    }

    public static void MapEscenasCasa(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/domotica/escenas").RequireAuthorization();
        static IResult Mal(string campo, string texto) => Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [texto] });

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok((await db.EscenasCasa.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => new { x.Id, x.Nombre, x.Icono, acciones = Leer(x.Acciones).Count })));

        // guarda cómo están ahora los aparatos elegidos (o todos) como una escena
        g.MapPost("/", async (EscenaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 30) return Mal("nombre", "Ponle un nombre (hasta 30 letras).");
            if ((e.Icono ?? "").Length > 16) return Mal("icono", "El icono es un emoji.");
            if (await db.EscenasCasa.CountAsync(ct) >= Maximo) return Mal("nombre", $"Máximo {Maximo} escenas.");
            if ((await db.EscenasCasa.AsNoTracking().Select(x => x.Nombre).ToListAsync(ct)).Any(n => string.Equals(n, e.Nombre.Trim(), StringComparison.OrdinalIgnoreCase))) return Mal("nombre", "Ya hay una escena con ese nombre.");
            var aparatos = await db.Aparatos.AsNoTracking().Where(a => e.AparatoIds == null || e.AparatoIds.Contains(a.Id)).ToListAsync(ct);
            var acciones = Foto(aparatos, await db.Entidades.AsNoTracking().ToListAsync(ct));
            if (acciones.Count == 0) return Mal("nombre", "No hay nada que guardar: aún no se han leído los aparatos.");
            var x = new EscenaCasa { Nombre = e.Nombre.Trim(), Icono = string.IsNullOrWhiteSpace(e.Icono) ? "✨" : e.Icono.Trim(), Acciones = JsonSerializer.Serialize(acciones, Web) };
            db.EscenasCasa.Add(x);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { x.Id, x.Nombre, x.Icono, acciones = acciones.Count });
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.EscenasCasa.FindAsync([id], ct) is not { } x) return Results.NotFound();
            db.EscenasCasa.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // vuelve a poner cada aparato como estaba; los que fallan se cuentan y el resto sigue
        g.MapPost("/{id:int}/aplicar", async (int id, AppDbContext db, ServicioCasa casa, CancellationToken ct) =>
        {
            if (await db.EscenasCasa.FindAsync([id], ct) is not { } x) return Results.NotFound();
            var (hechas, fallos) = await Aplicar(x, db, casa, ct);
            return Results.Ok(new { hechas, fallos });
        });
    }
}
