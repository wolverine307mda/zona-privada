namespace Mda.Api.Gastos;

// Un punto de la curva de saldo previsto: el día del mes, el caso central y la banda de «peor» y «mejor».
public record PuntoPrevision(int Dia, decimal Central, decimal Bajo, decimal Alto);

// FijosPendientes / IngresosPendientes: lo que aún no tiene apunte este mes y se espera (su suma ya está incluida en Central, Bajo y Alto).
public record PrevisionCierre(decimal SaldoActual, decimal Central, decimal Bajo, decimal Alto, decimal RitmoDia, int DiasRestantes, int DiasDeDatos, bool Fiable,
    decimal FijosPendientes = 0, decimal IngresosPendientes = 0, IReadOnlyList<PuntoPrevision>? Serie = null);

// Con cuánto cerrarás el mes: tu saldo de hoy menos lo variable que sueles gastar cada día (media de los últimos 60 días) durante los días que quedan,
// menos los fijos que aún no se han apuntado y más las nóminas que faltan, con una banda que crece con la raíz de los días que faltan según lo
// irregular que eres. La serie da el saldo previsto día a día (los fijos caen en su día; si ya pasó y sigue sin apunte, se cuenta hoy).
public static class Prevision
{
    public const int Ventana = 60;
    private const int MinimoDias = 14;

    public static PrevisionCierre Cierre(DateOnly hoy, IReadOnlyList<Movimiento> movs, decimal saldo, IReadOnlyList<Pendiente>? pendientes = null)
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
        decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        var esperados = (pendientes ?? []).Select(p => (Dia: Math.Max(p.Fecha.Day, hoy.Day), Importe: p.Tipo == TipoMovimiento.Gasto ? -p.Importe : p.Importe)).ToList();
        var fijos = esperados.Where(e => e.Importe < 0).Sum(e => -e.Importe);
        var ingresos = esperados.Where(e => e.Importe > 0).Sum(e => e.Importe);

        // (central, bajo, alto) del saldo al final del día `dia`
        (decimal Central, decimal Bajo, decimal Alto) En(int dia)
        {
            var n = dia - hoy.Day;
            var banda = (decimal)(desv * Math.Sqrt(n));
            var neto = esperados.Where(e => e.Dia <= dia).Sum(e => e.Importe);
            var central = saldo - media * n + neto;
            // por arriba, como mucho no gastar nada variable: nunca por encima de lo que hay más lo que ya entró o salió de fijo
            return (central, central - banda, Math.Min(saldo + neto, central + banda));
        }
        var serie = Enumerable.Range(hoy.Day, restantes + 1).Select(d => { var p = En(d); return new PuntoPrevision(d, R(p.Central), R(p.Bajo), R(p.Alto)); }).ToList();
        var fin = serie[^1];
        return new PrevisionCierre(R(saldo), fin.Central, fin.Bajo, fin.Alto, R(media), restantes, dias, dias >= MinimoDias, R(fijos), R(ingresos), serie);
    }
}
