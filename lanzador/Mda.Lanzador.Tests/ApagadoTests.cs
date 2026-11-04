namespace Mda.Lanzador.Tests;

public class ApagadoTests
{
    [Theory]
    [InlineData(null, 30)] [InlineData("", 30)] [InlineData("  ", 30)]            // sin tiempo, los 30 s de siempre
    [InlineData("1", 60)] [InlineData("15", 900)] [InlineData("60", 3600)] [InlineData("720", 43200)]
    public void Convierte_los_minutos_a_segundos(string? param, int segundos) => Assert.Equal(segundos, Apagado.Segundos(param));

    [Theory]
    [InlineData("0")] [InlineData("-5")] [InlineData("721")] [InlineData("abc")] [InlineData("10; shutdown /r")] [InlineData("1.5")]
    public void Un_tiempo_fuera_de_rango_o_con_basura_se_rechaza(string param) => Assert.Null(Apagado.Segundos(param));

    [Theory]
    [InlineData(30, "30 s")] [InlineData(900, "15 min")] [InlineData(3600, "1 h")] [InlineData(5400, "1 h 30 min")] [InlineData(43200, "12 h")]
    public void Dice_el_tiempo_en_claro(int segundos, string texto) => Assert.Equal(texto, Apagado.Texto(segundos));
}
