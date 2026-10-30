namespace Mda.Api.Gastos;

// Respuestas de la API de gastos
public record TotalCategoria(string Categoria, decimal Total, int Cantidad);
public record Totales(decimal Ingresos, decimal Gastos, decimal Ahorro);
public record Pendiente(int RecurrenteId, string Nombre, TipoMovimiento Tipo, string Categoria, decimal Importe, bool Variable, DateOnly Fecha);
public record Suscripciones(decimal Mensual, decimal Anual, int Cantidad);
public record ResumenMes(
    int Anio, int Mes, decimal Ingresos, decimal Gastos, decimal Ahorro, double? TasaAhorro,
    Totales Anterior, IReadOnlyList<TotalCategoria> PorCategoria, IReadOnlyList<decimal> GastoPorDia,
    decimal FijosDelMes, IReadOnlyList<Pendiente> Pendientes, Suscripciones Suscripciones,
    IReadOnlyList<Movimiento> Movimientos);
public record PuntoMes(int Anio, int Mes, decimal Ingresos, decimal Gastos, decimal Ahorro, decimal AhorroAcumulado);
public record Estadisticas(
    IReadOnlyList<PuntoMes> Serie, decimal Ingresos, decimal Gastos, decimal Ahorro, double? TasaAhorro,
    decimal MediaGastos, decimal MediaIngresos, PuntoMes? MejorMes, PuntoMes? PeorMes,
    IReadOnlyList<TotalCategoria> PorCategoria);
public record Repostaje(int Id, DateOnly Fecha, decimal Importe, decimal Litros, decimal PrecioLitro, int? Kilometros, double? Consumo, string Concepto);
public record Gasolina(
    IReadOnlyList<Repostaje> Repostajes, decimal Gastado, decimal Litros, decimal? PrecioMedio, decimal? UltimoPrecio,
    double? ConsumoMedio, int? KmRecorridos, decimal? CostePorKm, decimal GastoEsteAnio);

public static class Calculos
{
    private static decimal Suma(IEnumerable<Movimiento> m, TipoMovimiento t) => m.Where(x => x.Tipo == t).Sum(x => x.Importe);

    public static Totales TotalesDe(IEnumerable<Movimiento> movimientos)
    {
        var lista = movimientos as IReadOnlyCollection<Movimiento> ?? movimientos.ToList();
        var ingresos = Suma(lista, TipoMovimiento.Ingreso);
        var gastos = Suma(lista, TipoMovimiento.Gasto);
        return new Totales(ingresos, gastos, ingresos - gastos);
    }

    public static double? Tasa(decimal ingresos, decimal ahorro) =>
        ingresos > 0 ? Math.Round((double)(ahorro / ingresos) * 100, 1) : null;

    public static IReadOnlyList<TotalCategoria> PorCategoria(IEnumerable<Movimiento> movimientos, TipoMovimiento tipo = TipoMovimiento.Gasto) =>
        movimientos.Where(m => m.Tipo == tipo)
            .GroupBy(m => m.Categoria)
            .Select(g => new TotalCategoria(g.Key, g.Sum(m => m.Importe), g.Count()))
            .OrderByDescending(t => t.Total)
            .ToList();

    // Resumen de un mes: totales, comparación con el anterior, reparto, gasto por día y fijos pendientes
    public static ResumenMes Mes(int anio, int mes, IReadOnlyList<Movimiento> delMes, IReadOnlyList<Movimiento> delAnterior, IReadOnlyList<Recurrente> recurrentes)
    {
        var t = TotalesDe(delMes);
        var dias = DateTime.DaysInMonth(anio, mes);
        var porDia = new decimal[dias];
        foreach (var m in delMes.Where(m => m.Tipo == TipoMovimiento.Gasto)) porDia[m.Fecha.Day - 1] += m.Importe;

        var tocan = recurrentes.Where(r => r.TocaEn(anio, mes)).ToList();
        var registrados = delMes.Where(m => m.RecurrenteId is not null).Select(m => m.RecurrenteId!.Value).ToHashSet();
        var pendientes = tocan.Where(r => !registrados.Contains(r.Id))
            .Select(r => new Pendiente(r.Id, r.Nombre, r.Tipo, r.Categoria, r.Importe, r.Variable, new DateOnly(anio, mes, Math.Min(r.Dia, dias))))
            .OrderBy(p => p.Fecha).ThenBy(p => p.Nombre)
            .ToList();
        var fijos = tocan.Where(r => r.Tipo == TipoMovimiento.Gasto).Sum(r => r.Importe);

        return new ResumenMes(anio, mes, t.Ingresos, t.Gastos, t.Ahorro, Tasa(t.Ingresos, t.Ahorro),
            TotalesDe(delAnterior), PorCategoria(delMes), porDia, fijos, pendientes, SuscripcionesDe(recurrentes),
            delMes.OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id).ToList());
    }

    public static Suscripciones SuscripcionesDe(IEnumerable<Recurrente> recurrentes)
    {
        var activas = recurrentes.Where(r => r.Activo && r.Categoria == "suscripciones" && r.Tipo == TipoMovimiento.Gasto).ToList();
        var mensual = activas.Sum(r => r.Mensual);
        return new Suscripciones(mensual, mensual * 12, activas.Count);
    }

    // Los últimos N meses hasta (anio, mes), con el ahorro acumulado
    public static Estadisticas Periodo(int anio, int mes, int meses, IReadOnlyList<Movimiento> movimientos)
    {
        var serie = new List<PuntoMes>();
        decimal acumulado = 0;
        var inicio = new DateOnly(anio, mes, 1).AddMonths(-(meses - 1));
        for (var i = 0; i < meses; i++)
        {
            var d = inicio.AddMonths(i);
            var t = TotalesDe(movimientos.Where(m => m.Fecha.Year == d.Year && m.Fecha.Month == d.Month));
            acumulado += t.Ahorro;
            serie.Add(new PuntoMes(d.Year, d.Month, t.Ingresos, t.Gastos, t.Ahorro, acumulado));
        }
        var total = TotalesDe(movimientos);
        var conDatos = serie.Where(p => p.Ingresos > 0 || p.Gastos > 0).ToList();
        var n = Math.Max(1, conDatos.Count);
        return new Estadisticas(serie, total.Ingresos, total.Gastos, total.Ahorro, Tasa(total.Ingresos, total.Ahorro),
            Math.Round(conDatos.Sum(p => p.Gastos) / n, 2), Math.Round(conDatos.Sum(p => p.Ingresos) / n, 2),
            conDatos.MaxBy(p => p.Ahorro), conDatos.MinBy(p => p.Ahorro), PorCategoria(movimientos));
    }

    // Repostajes y consumo. El consumo de un repostaje (L/100 km) se calcula con el método del depósito lleno:
    // los litros que entran ahora son los que se gastaron desde el repostaje anterior.
    public static Gasolina GasolinaDe(IReadOnlyList<Movimiento> repostajes, int anioActual)
    {
        var lista = repostajes.Where(r => r.Litros is > 0).OrderBy(r => r.Fecha).ThenBy(r => r.Id).ToList();
        var salida = new List<Repostaje>();
        Movimiento? anterior = null;
        double litrosConsumo = 0;
        int kmConsumo = 0;
        foreach (var r in lista)
        {
            double? consumo = null;
            if (anterior?.Kilometros is { } kmA && r.Kilometros is { } kmB && kmB > kmA)
            {
                consumo = Math.Round((double)r.Litros!.Value / (kmB - kmA) * 100, 2);
                litrosConsumo += (double)r.Litros!.Value;
                kmConsumo += kmB - kmA;
            }
            var precio = r.PrecioLitro ?? (r.Litros > 0 ? Math.Round(r.Importe / r.Litros!.Value, 3) : 0);
            salida.Add(new Repostaje(r.Id, r.Fecha, r.Importe, r.Litros!.Value, precio, r.Kilometros, consumo, r.Concepto));
            if (r.Kilometros is not null) anterior = r;
        }

        var gastado = lista.Sum(r => r.Importe);
        var litros = lista.Sum(r => r.Litros!.Value);
        var conKm = lista.Where(r => r.Kilometros is not null).ToList();
        int? km = conKm.Count >= 2 ? conKm[^1].Kilometros - conKm[0].Kilometros : null;
        return new Gasolina(
            salida.OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id).ToList(),
            gastado, litros,
            litros > 0 ? Math.Round(gastado / litros, 3) : null,
            salida.Count > 0 ? salida.MaxBy(r => (r.Fecha, r.Id))!.PrecioLitro : null,
            kmConsumo > 0 ? Math.Round(litrosConsumo / kmConsumo * 100, 2) : null,
            km,
            km is > 0 ? Math.Round(conKm.Skip(1).Sum(r => r.Importe) / km.Value, 3) : null,
            lista.Where(r => r.Fecha.Year == anioActual).Sum(r => r.Importe));
    }
}
