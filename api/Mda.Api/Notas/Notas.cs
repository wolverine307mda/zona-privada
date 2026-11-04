using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Notas;

// Una nota: texto con Markdown (las líneas «- [ ]» son tareas que se marcan desde la tarjeta),
// un color, etiquetas y si va fijada arriba o archivada.
public class Nota
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Contenido { get; set; } = "";
    public string Color { get; set; } = "";
    public string Etiquetas { get; set; } = ""; // separadas por comas
    public bool Fijada { get; set; }
    public bool Archivada { get; set; }
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Modificado { get; set; } = DateTimeOffset.UtcNow;
}

public record NotaEntrada(string? Titulo, string? Contenido, string? Color, string[]? Etiquetas, bool Fijada, bool Archivada);

public static class NotasEndpoints
{
    private static readonly string[] Colores = ["", "amarillo", "verde", "azul", "rosa", "morado", "naranja", "rojo"];

    public static void MapNotas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/notas").RequireAuthorization();

        g.MapGet("/", async (bool? archivadas, string? q, AppDbContext db) =>
        {
            var arch = archivadas ?? false;
            var consulta = db.Notas.AsNoTracking().Where(n => n.Archivada == arch).OrderByDescending(n => n.Fijada).ThenByDescending(n => n.Modificado);
            // título y contenido están cifrados en la base: la búsqueda se hace aquí, ya descifrados (son pocas notas)
            if (!string.IsNullOrWhiteSpace(q))
            {
                var ql = q.Trim().ToLowerInvariant();
                var todas = await consulta.ToListAsync();
                return Results.Ok(todas.Where(n => n.Titulo.Contains(ql, StringComparison.OrdinalIgnoreCase) || n.Contenido.Contains(ql, StringComparison.OrdinalIgnoreCase) || n.Etiquetas.Contains(ql, StringComparison.OrdinalIgnoreCase)).Take(500).Select(Salida));
            }
            var lista = await consulta.Take(500).ToListAsync();
            return Results.Ok(lista.Select(Salida));
        });

        g.MapPost("/", async (NotaEntrada e, AppDbContext db) =>
        {
            if (Validar(e) is { } error) return error;
            var n = new Nota();
            Copiar(e, n);
            db.Notas.Add(n);
            await db.SaveChangesAsync();
            return Results.Created($"/api/notas/{n.Id}", Salida(n));
        });

        g.MapPut("/{id:int}", async (int id, NotaEntrada e, AppDbContext db) =>
        {
            if (Validar(e) is { } error) return error;
            var n = await db.Notas.FindAsync(id);
            if (n is null) return Results.NotFound();
            Copiar(e, n);
            n.Modificado = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(Salida(n));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var n = await db.Notas.FindAsync(id);
            if (n is null) return Results.NotFound();
            db.Notas.Remove(n);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static object Salida(Nota n) => new
    {
        n.Id, n.Titulo, n.Contenido, n.Color, n.Fijada, n.Archivada, n.Creado, n.Modificado,
        Etiquetas = n.Etiquetas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    };

    private static IResult? Validar(NotaEntrada e)
    {
        var errores = new Dictionary<string, string[]>();
        if (e.Titulo?.Length > 200) errores["titulo"] = ["El título no puede pasar de 200 caracteres."];
        if (e.Contenido?.Length > 100_000) errores["contenido"] = ["La nota es demasiado larga."];
        if (!Colores.Contains(e.Color ?? "")) errores["color"] = ["Ese color no existe."];
        if (e.Etiquetas is { } et && (et.Length > 20 || et.Any(x => x.Length > 40 || x.Contains(',')))) errores["etiquetas"] = ["Etiquetas no válidas."];
        if (string.IsNullOrWhiteSpace(e.Titulo) && string.IsNullOrWhiteSpace(e.Contenido)) errores["contenido"] = ["La nota está vacía."];
        return errores.Count > 0 ? Results.ValidationProblem(errores) : null;
    }

    private static void Copiar(NotaEntrada e, Nota n)
    {
        n.Titulo = e.Titulo?.Trim() ?? "";
        n.Contenido = e.Contenido ?? "";
        n.Color = e.Color ?? "";
        n.Etiquetas = string.Join(',', (e.Etiquetas ?? []).Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct());
        n.Fijada = e.Fijada;
        n.Archivada = e.Archivada;
    }
}
