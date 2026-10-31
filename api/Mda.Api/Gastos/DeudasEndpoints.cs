using Mda.Api.Agenda;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Gastos;

public record DeudaEntrada(string Nombre, decimal Total, string Categoria, DateOnly Inicio, string? Notas);
public record PagoEntrada(decimal Importe, DateOnly? Fecha, string? Concepto, string? Notas);

// Deudas: el coche, un préstamo… Cada pago es un apunte de gasto enlazado, así que también cuenta en el mes.
public static class DeudasEndpoints
{
    public static void MapDeudas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos/deudas").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, TimeProvider reloj, Zona zona) =>
        {
            var deudas = await db.Deudas.AsNoTracking().OrderBy(d => d.Inicio).ToListAsync();
            var ids = deudas.Select(d => d.Id).ToList();
            var pagos = await db.Movimientos.AsNoTracking().Where(m => m.DeudaId != null && ids.Contains(m.DeudaId.Value)).ToListAsync();
            var hoy = Cartera.Hoy(reloj, zona);
            // lo que se lleva gastado en cada grupo (para «el coche te ha costado…»: pagos, seguro, gasolina, papeleo)
            var grupos = deudas.Select(d => Categorias.Todas.GetValueOrDefault(d.Categoria).Grupo).Where(x => x is not null).Distinct().ToList();
            var cats = Categorias.Todas.Where(c => grupos.Contains(c.Value.Grupo)).Select(c => c.Key).ToList();
            var porCategoria = await db.Movimientos.AsNoTracking().Where(m => m.Tipo == TipoMovimiento.Gasto && cats.Contains(m.Categoria))
                .GroupBy(m => m.Categoria).Select(x => new { Categoria = x.Key, Total = x.Sum(m => m.Importe), Cantidad = x.Count() }).ToListAsync();
            return Results.Ok(new
            {
                deudas = deudas.Select(d => CalculosDeuda.De(d, pagos.Where(p => p.DeudaId == d.Id).ToList(), hoy)),
                grupos = grupos.Select(gr => new
                {
                    grupo = gr,
                    total = porCategoria.Where(c => Categorias.Todas[c.Categoria].Grupo == gr).Sum(c => c.Total),
                    porCategoria = porCategoria.Where(c => Categorias.Todas[c.Categoria].Grupo == gr).OrderByDescending(c => c.Total),
                }),
            });
        });

        g.MapPost("/", async (DeudaEntrada e, AppDbContext db) =>
        {
            if (Validar(e) is { } error) return error;
            var d = new Deuda();
            Copiar(e, d);
            db.Deudas.Add(d);
            await db.SaveChangesAsync();
            return Results.Created($"/api/gastos/deudas/{d.Id}", d);
        });

        g.MapPut("/{id:int}", async (int id, DeudaEntrada e, AppDbContext db) =>
        {
            if (Validar(e) is { } error) return error;
            var d = await db.Deudas.FindAsync(id);
            if (d is null) return Results.NotFound();
            Copiar(e, d);
            await db.SaveChangesAsync();
            return Results.Ok(d);
        });

        // al borrar la deuda los pagos siguen como gastos normales
        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var d = await db.Deudas.FindAsync(id);
            if (d is null) return Results.NotFound();
            await db.Movimientos.Where(m => m.DeudaId == id).ForEachAsync(m => m.DeudaId = null);
            db.Deudas.Remove(d);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPost("/{id:int}/pagos", async (int id, PagoEntrada e, AppDbContext db, TimeProvider reloj, Zona zona) =>
        {
            var d = await db.Deudas.FindAsync(id);
            if (d is null) return Results.NotFound();
            if (Math.Round(e.Importe, 2, MidpointRounding.AwayFromZero) <= 0 || e.Importe > 1_000_000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["importe"] = ["El importe tiene que ser mayor que 0."] });
            if (e.Concepto?.Length > 120 || e.Notas?.Length > 500) return Results.ValidationProblem(new Dictionary<string, string[]> { ["concepto"] = ["Texto demasiado largo."] });
            var m = new Movimiento
            {
                Tipo = TipoMovimiento.Gasto, Fecha = e.Fecha ?? Cartera.Hoy(reloj, zona), Importe = Math.Round(e.Importe, 2, MidpointRounding.AwayFromZero),
                Categoria = d.Categoria, Concepto = string.IsNullOrWhiteSpace(e.Concepto) ? $"Pago: {d.Nombre}" : e.Concepto.Trim(),
                Notas = string.IsNullOrWhiteSpace(e.Notas) ? null : e.Notas.Trim(), DeudaId = d.Id,
            };
            db.Movimientos.Add(m);
            await db.SaveChangesAsync();
            return Results.Created($"/api/gastos/movimientos/{m.Id}", m);
        });
    }

    private static IResult? Validar(DeudaEntrada e)
    {
        var errores = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Length > 80) errores["nombre"] = ["Escribe un nombre (máximo 80 caracteres)."];
        if (e.Total <= 0 || e.Total > 10_000_000) errores["total"] = ["El total tiene que ser mayor que 0."];
        if (!Categorias.Valida(e.Categoria, TipoMovimiento.Gasto)) errores["categoria"] = ["Esa categoría no existe."];
        if (e.Notas?.Length > 500) errores["notas"] = ["Las notas no pueden pasar de 500 caracteres."];
        return errores.Count > 0 ? Results.ValidationProblem(errores) : null;
    }

    private static void Copiar(DeudaEntrada e, Deuda d)
    {
        d.Nombre = e.Nombre.Trim();
        d.Total = e.Total;
        d.Categoria = e.Categoria;
        d.Inicio = e.Inicio;
        d.Notas = string.IsNullOrWhiteSpace(e.Notas) ? null : e.Notas.Trim();
    }
}
