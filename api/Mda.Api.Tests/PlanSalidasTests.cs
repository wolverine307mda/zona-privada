using Mda.Api.Luz;

namespace Mda.Api.Tests;

public class PlanSalidasTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 6, 17, 0, 0, TimeSpan.Zero);
    private static VentanaLuz V(string tipo, DateTimeOffset inicio, int? nota = 7) => new(tipo, tipo, inicio, inicio.AddHours(1), nota, "");
    private static SalidaPlan S(int? punto, string nombre, VentanaLuz v, int nota) => new(punto, nombre, "", v, nota, null);

    [Theory]
    [InlineData("O", 270, 7, 8)]       // mira al oeste y el sol se pone al oeste: +1
    [InlineData("E", 270, 7, 6)]       // mira al este: lado contrario, −1
    [InlineData("N", 270, 7, 7)]       // de lado: igual
    [InlineData("", 270, 7, 7)]        // sin orientación: igual
    [InlineData("O", 270, 10, 10)]     // nunca pasa de 10
    [InlineData("E", 270, 1, 1)]       // ni baja de 1
    public void La_orientacion_sube_o_baja_la_nota_una_unidad(string mira, double sol, int nota, int esperada)
    {
        Assert.Equal(esperada, PlanSalidas.Ajustar("atardecer", nota, mira, sol).Nota);
    }

    [Fact]
    public void La_diferencia_de_azimut_da_la_vuelta_por_el_norte()
    {
        Assert.Equal(20, PlanSalidas.Diferencia(350, 10));
        Assert.Equal(180, PlanSalidas.Diferencia(0, 180));
        Assert.Equal(0, PlanSalidas.Diferencia(90, 90));
        Assert.Equal(6, PlanSalidas.Ajustar("noche", 5, "S", 170).Nota);              // el núcleo sale por el sur-sureste
        Assert.Contains("Vía Láctea", PlanSalidas.Ajustar("noche", 5, "S", 170).Orienta);
        Assert.Contains("se pone", PlanSalidas.Ajustar("atardecer", 5, "O", 260).Orienta);
        Assert.Contains("sale", PlanSalidas.Ajustar("amanecer", 5, "E", 100).Orienta);
    }

    [Fact]
    public void Quedan_las_mejores_con_una_sola_por_punto_y_dia()
    {
        var m = new[]
        {
            S(1, "Mirador", V("atardecer", T), 7), S(1, "Mirador", V("noche", T.AddHours(4)), 9),      // el mismo día y punto: gana la 9
            S(1, "Mirador", V("atardecer", T.AddDays(1)), 6),
            S(2, "Pantano", V("atardecer", T), 8),
            S(2, "Pantano", V("amanecer", T.AddDays(2)), 5),
        };
        var r = PlanSalidas.Mejores(m, 3);
        Assert.Equal(3, r.Count);
        Assert.Equal([9, 8, 6], r.Select(x => x.Nota).ToArray());
        Assert.Equal("Mirador", r[0].Punto);
    }

    [Fact]
    public void A_igualdad_de_nota_va_primero_la_mas_cercana()
    {
        var m = new[] { S(1, "Lejana", V("atardecer", T.AddDays(3)), 8), S(2, "Cercana", V("atardecer", T), 8) };
        Assert.Equal("Cercana", PlanSalidas.Mejores(m, 2)[0].Punto);
    }
}
