using Mda.Api.Agenda;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Mda.Api.Luz;
using Mda.Api.Pc;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

public record SucesoVida(DateTimeOffset Hora, string Tipo, string Titulo, string Detalle);

// La línea del tiempo de un día: todo lo que pasó, de un vistazo y por orden: gastos, salidas de fotografía, órdenes al PC y accesos a la app.
// No tiene tabla propia: se compone al momento con lo que ya guardan los demás módulos.
public static class LineaVida
{
    private static readonly System.Globalization.CultureInfo Es = new("es-ES");

    public static List<SucesoVida> Construir(IEnumerable<Movimiento> movs, IEnumerable<SalidaFoto> salidas, IEnumerable<OrdenPc> ordenes, IEnumerable<Acceso> accesos, TimeZoneInfo zona)
    {
        var l = new List<SucesoVida>();
        foreach (var m in movs)
            l.Add(new(m.Creado, "dinero", $"{(m.Tipo == TipoMovimiento.Gasto ? "Gasto" : "Ingreso")}: {m.Concepto}",
                $"{(m.Tipo == TipoMovimiento.Gasto ? "−" : "+")}{m.Importe.ToString("0.00", Es)} €{(m.Categoria.Length > 0 && Categorias.Todas.TryGetValue(m.Categoria, out var c) ? " · " + c.Nombre : "")}"));
        foreach (var s in salidas)
            l.Add(new(new DateTimeOffset(s.Fecha.ToDateTime(new TimeOnly(12, 0)), zona.GetUtcOffset(s.Fecha.ToDateTime(new TimeOnly(12, 0)))), "foto", $"Salida de fotografía ({s.Tipo})",
                $"Salió {s.Resultado}/5{(s.Prevision is { } p ? $" · prevista {p}/10" : "")}{(s.Nota.Length > 0 ? " · " + s.Nota : "")}"));
        foreach (var o in ordenes)
            l.Add(new(o.Creada, "pc", $"Orden al PC: {o.Tipo}", $"{(string.IsNullOrEmpty(o.Parametro) ? "" : o.Parametro + " · ")}{o.Estado}"));
        foreach (var a in accesos)
            l.Add(new(a.Fecha, a.Exito ? "acceso" : "alerta", a.Exito ? "Entrada a la app" : "Intento de acceso fallido", $"{a.Ip}{(a.Motivo.Length > 0 ? " · " + a.Motivo : "")}"));
        return l.OrderByDescending(x => x.Hora).ToList();
    }

    public static void MapLinea(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inicio/linea", async (string? fecha, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Cartera.Hoy(reloj, zona);
            var dia = DateOnly.TryParseExact(fecha, "yyyy-MM-dd", out var f) ? f : hoy;
            if (dia > hoy || dia < hoy.AddYears(-3)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["fecha"] = ["Fecha fuera de rango."] });
            var ini = new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), zona.Info.GetUtcOffset(dia.ToDateTime(TimeOnly.MinValue)));
            var fin = ini.AddDays(1);
            var movs = await db.Movimientos.AsNoTracking().Where(m => m.Fecha == dia).ToListAsync(ct);
            var salidas = await db.SalidasFoto.AsNoTracking().Where(s => s.Fecha == dia).ToListAsync(ct);
            var ordenes = await db.OrdenesPc.AsNoTracking().Where(o => o.Creada >= ini && o.Creada < fin).ToListAsync(ct);
            var accesos = await db.Accesos.AsNoTracking().Where(a => a.Fecha >= ini && a.Fecha < fin).ToListAsync(ct);
            return Results.Ok(new { fecha = dia.ToString("yyyy-MM-dd"), sucesos = Construir(movs, salidas, ordenes, accesos, zona.Info) });
        }).RequireAuthorization();
    }
}
