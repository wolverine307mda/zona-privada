using Mda.Api.Agenda;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Gastos;

public record ZonaGasto(double Lat, double Lon, decimal Total, int Pagos, IReadOnlyList<ComercioZona> Comercios);
public record ComercioZona(string Nombre, decimal Total, int Pagos);
public record ComercioTotal(string Nombre, decimal Total, int Pagos, int Zonas);

// Dónde gastas: los pagos de Apple Pay pueden traer la ubicación (la guarda el atajo del iPhone) y aquí se juntan por zonas de ~1 km y por comercio.
public static class Lugares
{
    // las coordenadas se guardan con 3 decimales (~100 m): suficiente para «dónde» sin guardar el portal exacto
    public static double Redondear(double v) => Math.Round(v, 3);
    public static bool Valida(double? lat, double? lon) => lat is >= -90 and <= 90 && lon is >= -180 and <= 180 && !(lat == 0 && lon == 0);

    // zonas de 0,01° (≈1,1 km de lado) con lo gastado, los pagos y los comercios que más pesan
    public static (List<ZonaGasto> Zonas, List<ComercioTotal> Comercios) Agrupar(IEnumerable<Movimiento> movs)
    {
        var con = movs.Where(m => m.Tipo == TipoMovimiento.Gasto && m.Lat is not null && m.Lon is not null).ToList();
        var zonas = con.GroupBy(m => (Math.Round(m.Lat!.Value, 2), Math.Round(m.Lon!.Value, 2))).Select(g => new ZonaGasto(
            g.Key.Item1, g.Key.Item2, g.Sum(m => m.Importe), g.Count(),
            g.GroupBy(m => m.Concepto).Select(c => new ComercioZona(c.Key, c.Sum(m => m.Importe), c.Count())).OrderByDescending(c => c.Total).Take(3).ToList()))
            .OrderByDescending(z => z.Total).ToList();
        var comercios = con.GroupBy(m => m.Concepto).Select(g => new ComercioTotal(g.Key, g.Sum(m => m.Importe), g.Count(),
            g.Select(m => (Math.Round(m.Lat!.Value, 2), Math.Round(m.Lon!.Value, 2))).Distinct().Count())).OrderByDescending(c => c.Total).Take(10).ToList();
        return (zonas, comercios);
    }

    public static void MapLugares(this IEndpointRouteBuilder app)
    {
        // el mapa de calor de un mes (o de todo el año con mes = 0)
        app.MapGet("/api/gastos/lugares", async (int? anio, int? mes, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Cartera.Hoy(reloj, zona);
            var y = anio ?? hoy.Year;
            var desde = mes == 0 ? new DateOnly(y, 1, 1) : new DateOnly(y, Math.Clamp(mes ?? hoy.Month, 1, 12), 1);
            var hasta = mes == 0 ? desde.AddYears(1) : desde.AddMonths(1);
            var movs = await db.Movimientos.AsNoTracking().Where(m => m.Fecha >= desde && m.Fecha < hasta && m.Lat != null).ToListAsync(ct);
            var (zonas, comercios) = Agrupar(movs);
            return Results.Ok(new { desde, hasta, zonas, comercios, conUbicacion = movs.Count });
        }).RequireAuthorization();
    }
}
