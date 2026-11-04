namespace Mda.Lanzador.Tests;

public class VelocidadTests
{
    [Theory]
    [InlineData(25_000_000, 2.0, 100.0)]      // 25 MB en 2 s = 100 Mbps
    [InlineData(1_250_000, 1.0, 10.0)]
    [InlineData(8_000_000, 4.0, 16.0)]
    [InlineData(0, 5.0, 0.0)]
    [InlineData(1000, 0.0, 0.0)]              // sin tiempo no hay velocidad (y no se divide entre cero)
    public void Convierte_bytes_y_segundos_en_megabits_por_segundo(long bytes, double segundos, double mbps) => Assert.Equal(mbps, Velocidad.Mbps(bytes, segundos));

    [Fact]
    public void La_latencia_es_la_mediana_y_no_se_deja_engañar_por_un_pico()
    {
        Assert.Equal(12, Velocidad.Mediana([12, 11, 400, 13, 12]));
        Assert.Equal(13, Velocidad.Mediana([10, 12, 14, 16]));   // par: la media de las dos del medio
        Assert.Equal(7, Velocidad.Mediana([7]));
        Assert.Null(Velocidad.Mediana([]));
    }
}
