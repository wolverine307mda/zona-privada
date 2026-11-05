using Mda.Api.Gastos;

namespace Mda.Api.Tests;

public class PresupuestoRitmoTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 21);   // quedan 9 días de noviembre

    [Fact]
    public void Dice_cuanto_queda_y_a_que_ritmo()
    {
        var t = Cartera.Ritmo(64, 80, Hoy, Hoy);
        Assert.Contains(Cartera.Euros(16), t);
        Assert.Contains("9 días", t);
        Assert.Contains(Cartera.Euros(16m / 9), t);
    }

    [Fact]
    public void Pasado_del_limite_dice_cuanto()
    {
        Assert.Equal($"Te has pasado en {Cartera.Euros(5)}.", Cartera.Ritmo(85, 80, Hoy, Hoy));
        Assert.Contains("pasado", Cartera.Ritmo(80, 80, Hoy, Hoy));   // justo en el límite ya cuenta como pasado
    }

    [Fact]
    public void El_ultimo_dia_del_mes_no_divide_por_cero()
    {
        var fin = new DateOnly(2026, 11, 30);
        Assert.Contains("último día", Cartera.Ritmo(70, 80, fin, fin));
    }

    [Fact]
    public void De_un_mes_pasado_no_se_dice_ritmo()
    {
        Assert.Equal("", Cartera.Ritmo(60, 80, new DateOnly(2026, 10, 3), Hoy));
    }

    [Fact]
    public void Un_dia_restante_va_en_singular()
    {
        var d = new DateOnly(2026, 11, 29);
        Assert.Contains("1 día:", Cartera.Ritmo(50, 80, d, d));
    }
}
