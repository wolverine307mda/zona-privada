using Mda.Api.Asistente;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Gastos;

// «Mercadona → supermercado»: cada vez que corriges la categoría de un gasto se guarda una regla por comercio.
// Tiene más peso que la costumbre (la categoría que más has usado) y la usan los pagos de Apple Pay, la importación de extractos y el asistente.
public class ReglaCategoria
{
    public string Comercio { get; set; } = "";   // aplanado (sin tildes ni mayúsculas)
    public string Categoria { get; set; } = "";
    public string Nombre { get; set; } = "";     // como lo escribiste, para enseñarlo
    public int Veces { get; set; }
    public DateTimeOffset Actualizado { get; set; } = DateTimeOffset.UtcNow;
}

public static class Reglas
{
    public const int MaximoReglas = 500;

    public static string Clave(string concepto) => Interprete.Aplanar((concepto ?? "").Trim());

    // se aprende de gastos con un comercio de verdad (3 letras o más) y una categoría de gasto que existe
    public static bool Aprendible(string concepto, string categoria, TipoMovimiento tipo) =>
        tipo == TipoMovimiento.Gasto && Clave(concepto).Length >= 3 && Categorias.Valida(categoria, TipoMovimiento.Gasto);

    public static async Task Aprender(AppDbContext db, string concepto, string categoria, TipoMovimiento tipo, CancellationToken ct)
    {
        if (!Aprendible(concepto, categoria, tipo)) return;
        var clave = Clave(concepto);
        var r = await db.ReglasCategoria.FindAsync([clave], ct);
        if (r is null)
        {
            if (await db.ReglasCategoria.CountAsync(ct) >= MaximoReglas) return;
            db.ReglasCategoria.Add(new ReglaCategoria { Comercio = clave, Categoria = categoria, Nombre = concepto.Trim(), Veces = 1 });
        }
        else if (r.Categoria == categoria) { r.Veces++; r.Actualizado = DateTimeOffset.UtcNow; }
        else { r.Categoria = categoria; r.Veces = 1; r.Nombre = concepto.Trim(); r.Actualizado = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(ct);
    }

    // las reglas pisan lo aprendido por costumbre
    public static Dictionary<string, string> Mezclar(IReadOnlyDictionary<string, string> costumbre, IEnumerable<ReglaCategoria> reglas)
    {
        var d = new Dictionary<string, string>(costumbre);
        foreach (var r in reglas) d[r.Comercio] = r.Categoria;
        return d;
    }

    public static void MapReglas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos/reglas").RequireAuthorization();
        g.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(await db.ReglasCategoria.AsNoTracking().OrderByDescending(r => r.Actualizado).ToListAsync(ct)));
        g.MapDelete("/{comercio}", async (string comercio, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.ReglasCategoria.FindAsync([comercio], ct) is not { } r) return Results.NotFound();
            db.ReglasCategoria.Remove(r);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
