using Mda.Api.Gastos;

namespace Mda.Api.Tests;

public class PrevisionTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 10);   // quedan 20 días de noviembre

    private static Movimiento G(DateOnly f, decimal i, int? fijo = null) => new() { Tipo = TipoMovimiento.Gasto, Fecha = f, Importe = i, RecurrenteId = fijo };

    [Fact]
    public void Gasto_constante_da_una_prevision_exacta_sin_banda()
    {
        var movs = Enumerable.Range(0, 60).Select(i => G(Hoy.AddDays(-i), 10)).ToList();
        var p = Prevision.Cierre(Hoy, movs, 1000);
        Assert.Equal(10, p.RitmoDia);
        Assert.Equal(800, p.Central);                 // 1000 − 10 × 20 días
        Assert.Equal(p.Central, p.Bajo);
        Assert.Equal(p.Central, p.Alto);
        Assert.True(p.Fiable);
    }

    [Fact]
    public void Un_gasto_irregular_abre_la_banda_y_el_alto_no_pasa_del_saldo()
    {
        var movs = Enumerable.Range(0, 60).Select(i => G(Hoy.AddDays(-i), i % 2 == 0 ? 0 : 40)).ToList();
        var p = Prevision.Cierre(Hoy, movs, 1000);
        Assert.Equal(20, p.RitmoDia);
        Assert.True(p.Bajo < p.Central && p.Central < p.Alto);
        Assert.True(p.Alto <= 1000);
        Assert.Equal(600, p.Central);
    }

    [Fact]
    public void Los_fijos_no_cuentan_como_ritmo_variable()
    {
        var movs = Enumerable.Range(0, 60).Select(i => G(Hoy.AddDays(-i), 10)).Append(G(Hoy.AddDays(-3), 900, fijo: 1)).ToList();
        Assert.Equal(10, Prevision.Cierre(Hoy, movs, 1000).RitmoDia);
    }

    [Fact]
    public void Con_pocos_dias_de_datos_avisa_de_que_no_es_fiable_y_el_ultimo_dia_no_resta_nada()
    {
        var pocos = Prevision.Cierre(Hoy, [G(Hoy.AddDays(-2), 30), G(Hoy, 10)], 500);
        Assert.False(pocos.Fiable);
        Assert.Equal(3, pocos.DiasDeDatos);
        var ultimo = Prevision.Cierre(new DateOnly(2026, 11, 30), [G(new DateOnly(2026, 11, 30), 10)], 500);
        Assert.Equal(0, ultimo.DiasRestantes);
        Assert.Equal(500, ultimo.Central);
        Assert.Equal(500, Prevision.Cierre(Hoy, [], 500).Central);
    }
}
