using Mda.Api.Gastos;

namespace Mda.Api.Tests;

// La previsión de fin de mes con los fijos y nóminas que aún no tienen apunte
public class PrevisionFijosTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 10);   // quedan 20 días de noviembre

    private static Movimiento G(DateOnly f, decimal i) => new() { Tipo = TipoMovimiento.Gasto, Fecha = f, Importe = i };
    private static Pendiente P(string n, TipoMovimiento t, decimal i, int dia) => new(1, n, t, "otros", i, false, new DateOnly(2026, 11, dia));
    private static List<Movimiento> Diez() => Enumerable.Range(0, 60).Select(i => G(Hoy.AddDays(-i), 10)).ToList();

    [Fact]
    public void Un_fijo_pendiente_resta_y_una_nomina_pendiente_suma()
    {
        var sin = Prevision.Cierre(Hoy, Diez(), 1000);
        var con = Prevision.Cierre(Hoy, Diez(), 1000, [P("Alquiler", TipoMovimiento.Gasto, 400, 28), P("Nómina", TipoMovimiento.Ingreso, 1500, 30)]);
        Assert.Equal(400, con.FijosPendientes);
        Assert.Equal(1500, con.IngresosPendientes);
        Assert.Equal(sin.Central - 400 + 1500, con.Central);
    }

    [Fact]
    public void Sin_pendientes_la_serie_acaba_donde_dice_el_cierre()
    {
        var p = Prevision.Cierre(Hoy, Diez(), 1000);
        Assert.Equal(0, p.FijosPendientes);
        Assert.Equal(p.Central, p.Serie![^1].Central);
        Assert.Equal(p.Bajo, p.Serie[^1].Bajo);
        Assert.Equal(p.Alto, p.Serie[^1].Alto);
    }

    [Fact]
    public void Un_fijo_que_ya_paso_y_sigue_sin_apunte_se_cuenta_hoy_y_no_se_pierde()
    {
        var p = Prevision.Cierre(Hoy, Diez(), 1000, [P("Seguro", TipoMovimiento.Gasto, 100, 3)]);   // su día era el 3 y hoy es el 10
        Assert.Equal(100, p.FijosPendientes);
        Assert.Equal(900, p.Serie![0].Central);
    }

    [Fact]
    public void La_serie_va_de_hoy_a_fin_de_mes_y_los_fijos_caen_en_su_dia()
    {
        var p = Prevision.Cierre(Hoy, Diez(), 1000, [P("Alquiler", TipoMovimiento.Gasto, 400, 20)]);
        Assert.Equal(21, p.Serie!.Count);                   // del 10 al 30 de noviembre
        Assert.Equal(10, p.Serie[0].Dia);
        Assert.Equal(1000, p.Serie[0].Central);
        Assert.Equal(990, p.Serie[1].Central);              // un día después: −10 de gasto diario
        Assert.Equal(p.Serie.First(x => x.Dia == 19).Central - 10 - 400, p.Serie.First(x => x.Dia == 20).Central);   // el salto del alquiler
    }

    [Fact]
    public void El_alto_no_supera_lo_que_hay_menos_lo_fijo()
    {
        var movs = Enumerable.Range(0, 60).Select(i => G(Hoy.AddDays(-i), i % 2 == 0 ? 0 : 40)).ToList();
        var p = Prevision.Cierre(Hoy, movs, 1000, [P("Alquiler", TipoMovimiento.Gasto, 400, 28)]);
        Assert.True(p.Alto <= 600);
    }

    [Fact]
    public void El_ultimo_dia_la_serie_es_un_solo_punto()
    {
        var fin = new DateOnly(2026, 11, 30);
        var p = Prevision.Cierre(fin, [G(fin, 10)], 500, [P("Nómina", TipoMovimiento.Ingreso, 1500, 30)]);
        Assert.Single(p.Serie!);
        Assert.Equal(2000, p.Central);
    }
}
