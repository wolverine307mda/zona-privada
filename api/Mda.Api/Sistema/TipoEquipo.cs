using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Sistema;

public record TipoEquipoEntrada(string Tipo);

// Qué equipo es el que hospeda la app: un portátil o un mini PC. Solo cambia el dibujo en 3D de «Tu PC por dentro»
// (el mini PC no lleva batería). Está en el servidor, no en el navegador: es del equipo, no de quien mira, y se va con la app cuando pase al mini PC.
public static class TipoEquipo
{
    public const string Clave = "equipo-tipo";
    public const string Portatil = "portatil", MiniPc = "minipc";
    public static readonly string[] Validos = [Portatil, MiniPc];

    public static async Task<string> Leer(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor;
        return v is not null && Validos.Contains(v) ? v : Portatil;
    }

    public static void MapTipoEquipo(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/equipo").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(new { tipo = await Leer(db, ct) }));

        g.MapPut("/", async (TipoEquipoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (!Validos.Contains(e.Tipo)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["tipo"] = ["Elige portátil o mini PC."] });
            var a = await db.Ajustes.FindAsync([Clave], ct);
            if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = Clave });
            a.Valor = e.Tipo;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
