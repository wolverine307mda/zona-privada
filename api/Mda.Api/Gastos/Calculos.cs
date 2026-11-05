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

    // Los fijos y nóminas del mes que aún no tienen su apunte, por orden de fecha
    public static List<Pendiente> PendientesDe(int anio, int mes, IReadOnlyList<Recurrente> tocan, IReadOnlySet<int> registrados)
    {
        var dias = DateTime.DaysInMonth(anio, mes);
        return tocan.Where(r => !registrados.Contains(r.Id))
            .Select(r => new Pendiente(r.Id, r.Nombre, r.Tipo, r.Categoria, r.Importe, r.Variable, new DateOnly(anio, mes, Math.Min(r.Dia, dias))))
            .OrderBy(p => p.Fecha).ThenBy(p => p.Nombre)
            .ToList();
    }

    // Resumen de un mes: totales, comparación con el anterior, reparto, gasto por día y fijos pendientes
    public static ResumenMes Mes(int anio, int mes, IReadOnlyList<Movimiento> delMes, IReadOnlyList<Movimiento> delAnterior, IReadOnlyList<Recurrente> recurrentes, IReadOnlySet<int>? cubiertos = null)
    {
        var t = TotalesDe(delMes);
        var dias = DateTime.DaysInMonth(anio, mes);
        var porDia = new decimal[dias];
        foreach (var m in delMes.Where(m => m.Tipo == TipoMovimiento.Gasto)) porDia[m.Fecha.Day - 1] += m.Importe;

        var tocan = recurrentes.Where(r => r.TocaEn(anio, mes)).ToList();
        // un fijo está cubierto si hay un apunte de ESTE mes, aunque esté fechado en otro (cobrado antes de su día)
        var clave = Movimiento.ClavePeriodo(anio, mes);
        var registrados = cubiertos ?? delMes.Where(m => m.RecurrenteId is not null && m.PeriodoEfectivo == clave).Select(m => m.RecurrenteId!.Value).ToHashSet();
        var pendientes = PendientesDe(anio, mes, tocan, registrados);
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
            Math.Round(conDatos.Sum(p => p.Gastos) / n, 2, MidpointRounding.AwayFromZero), Math.Round(conDatos.Sum(p => p.Ingresos) / n, 2, MidpointRounding.AwayFromZero),
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

public record PagoDeuda(int Id, DateOnly Fecha, decimal Importe, string Concepto, string? Notas);
public record MesPagado(int Anio, int Mes, decimal Total);
public record EstadoDeuda(
    int Id, string Nombre, decimal Total, string Categoria, DateOnly Inicio, string? Notas,
    decimal Pagado, decimal Restante, double Porcentaje, int NumPagos, decimal? MediaMensual, DateOnly? FinEstimado,
    IReadOnlyList<MesPagado> PorMes, IReadOnlyList<PagoDeuda> Pagos);

public static class CalculosDeuda
{
    // Lo pagado, lo que falta y, al ritmo de pago medio desde el primer pago, cuándo se acaba
    public static EstadoDeuda De(Deuda d, IReadOnlyList<Movimiento> pagos, DateOnly hoy)
    {
        var lista = pagos.OrderBy(p => p.Fecha).ThenBy(p => p.Id).ToList();
        var pagado = lista.Sum(p => p.Importe);
        var restante = Math.Max(0, d.Total - pagado);
        decimal? media = null;
        DateOnly? fin = null;
        var porMes = new List<MesPagado>();
        if (lista.Count > 0)
        {
            var primero = new DateOnly(lista[0].Fecha.Year, lista[0].Fecha.Month, 1);
            var ultimo = new DateOnly(hoy.Year, hoy.Month, 1);
            if (ultimo < primero) ultimo = primero;
            for (var m = primero; m <= ultimo; m = m.AddMonths(1))
                porMes.Add(new MesPagado(m.Year, m.Month, lista.Where(p => p.Fecha.Year == m.Year && p.Fecha.Month == m.Month).Sum(p => p.Importe)));
            media = Math.Round(pagado / porMes.Count, 2, MidpointRounding.AwayFromZero);
            if (restante == 0) fin = lista[^1].Fecha;
            else if (media > 0 && Math.Ceiling(restante / media.Value) is <= 1200 and var meses) fin = hoy.AddMonths((int)meses); // más de 100 años = sin fecha estimada
        }
        return new EstadoDeuda(d.Id, d.Nombre, d.Total, d.Categoria, d.Inicio, d.Notas,
            pagado, restante, d.Total > 0 ? Math.Round((double)(pagado / d.Total) * 100, 1) : 0, lista.Count, media, fin,
            porMes, lista.OrderByDescending(p => p.Fecha).ThenByDescending(p => p.Id).Select(p => new PagoDeuda(p.Id, p.Fecha, p.Importe, p.Concepto, p.Notas)).ToList());
    }
}
