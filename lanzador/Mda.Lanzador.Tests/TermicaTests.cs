namespace Mda.Lanzador.Tests;

public class TermicaTests
{
    [Theory]
    [InlineData(3321, 59.0)]     // 332,1 K
    [InlineData(3000, 26.9)]     // 300 K
    [InlineData(3731, 100.0)]
    public void Convierte_decimas_de_kelvin_a_celsius(long valor, double celsius) => Assert.Equal(celsius, Termica.DeDecimasDeKelvin(valor));

    [Theory]
    [InlineData(0)] [InlineData(2731)] [InlineData(100)] [InlineData(9999)] [InlineData(-5)]
    public void Un_valor_que_no_es_creible_se_descarta(long valor) => Assert.Null(Termica.DeDecimasDeKelvin(valor));

    [Fact]
    public void De_varias_zonas_se_queda_con_la_mas_caliente_ignorando_las_falsas() =>
        Assert.Equal(72.8, Termica.Maxima([3000, 3459, 0, 9999]));

    [Fact]
    public void Sin_ninguna_zona_valida_no_hay_temperatura()
    {
        Assert.Null(Termica.Maxima([]));
        Assert.Null(Termica.Maxima([0, 100]));
    }
}
