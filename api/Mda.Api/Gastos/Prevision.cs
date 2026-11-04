namespace Mda.Api.Gastos;

public record PrevisionCierre(decimal SaldoActual, decimal Central, decimal Bajo, decimal Alto, decimal RitmoDia, int DiasRestantes, int DiasDeDatos, bool Fiable);

// Con cuánto cerrarás el mes: tu saldo de hoy menos lo variable que sueles gastar cada día (media de los últimos 60 días) durante los días que quedan,
// con una banda que crece con la raíz de los días que faltan según lo irregular que eres. No cuenta fijos ni nóminas hasta que los apuntas (como el resto de la Cartera).
public static class Prevision
{
    public const int Ventana = 60;
    private const int MinimoDias = 14;

    public static PrevisionCierre Cierre(DateOnly hoy, IReadOnlyList<Movimiento> movs, decimal saldo)
    {
        var restantes = DateTime.DaysInMonth(hoy.Year, hoy.Month) - hoy.Day;
        var desde = hoy.AddDays(-(Ventana - 1));
        var variables = movs.Where(m => m.Tipo == TipoMovimiento.Gasto && m.RecurrenteId is null && m.DeudaId is null && m.Fecha >= desde && m.Fecha <= hoy).ToList();
        // el histórico empieza el día del primer apunte (si empezaste ayer, no hay 60 días de datos)
        var primero = movs.Count > 0 ? movs.Min(m => m.Fecha) : hoy;
        var dias = Math.Clamp(hoy.DayNumber - (primero > desde ? primero : desde).DayNumber + 1, 1, Ventana);
        var porDia = Enumerable.Range(0, dias).Select(i => variables.Where(m => m.Fecha == hoy.AddDays(-i)).Sum(m => m.Importe)).ToList();
        var media = porDia.Average();
        var desv = dias > 1 ? Math.Sqrt(porDia.Sum(x => Math.Pow((double)(x - media), 2)) / (dias - 1)) : 0;
        var banda = (decimal)(desv * Math.Sqrt(restantes));
        var gasto = media * restantes;
        var central = saldo - gasto;
        decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        // la banda por abajo es «gasto mayor de lo normal», por arriba «menor»; nunca por encima del saldo de hoy (no se gasta negativo)
        return new PrevisionCierre(R(saldo), R(central), R(central - banda), R(Math.Min(saldo, central + banda)), R(media), restantes, dias, dias >= MinimoDias);
    }
}
