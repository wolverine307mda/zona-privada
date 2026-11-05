using Mda.Api.Data;
using Mda.Api.Gastos;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Asistente;

// Buscar en tu contenido desde el buscador (Ctrl K): eventos de la agenda y movimientos de la cartera, cinco de cada, los más recientes primero.
// Las notas ya se buscan en /api/notas (van cifradas: se descifran en la API y no se pueden filtrar en la base).
public static class BuscarEndpoints
{
    public const int PorTipo = 5, MinimoLetras = 2, MaximoLetras = 60;

    public static void MapBuscar(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/buscar", async (string? q, AppDbContext db, CancellationToken ct) =>
        {
            var t = (q ?? "").Trim().ToLowerInvariant();
            if (t.Length < MinimoLetras || t.Length > MaximoLetras) return Results.Ok(new { agenda = Array.Empty<object>(), movimientos = Array.Empty<object>() });
            var agenda = await db.Eventos.AsNoTracking()
                .Where(e => e.Titulo.ToLower().Contains(t) || (e.Lugar != null && e.Lugar.ToLower().Contains(t)))
                .OrderByDescending(e => e.Inicio).Take(PorTipo)
                .Select(e => new { e.Id, e.Titulo, e.Inicio, e.Lugar, e.TodoElDia, Tipo = e.Tipo.ToString() }).ToListAsync(ct);
            var movimientos = await db.Movimientos.AsNoTracking()
                .Where(m => m.Concepto.ToLower().Contains(t))
                .OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id).Take(PorTipo)
                .Select(m => new { m.Id, m.Concepto, m.Importe, m.Fecha, m.Categoria, Tipo = m.Tipo.ToString() }).ToListAsync(ct);
            return Results.Ok(new { agenda, movimientos });
        }).RequireAuthorization();
    }
}
