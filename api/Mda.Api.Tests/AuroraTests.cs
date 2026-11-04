using System.Text.Json;
using Mda.Api.Luz;

namespace Mda.Api.Tests;

public class AuroraTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(70, 1)] [InlineData(60, 3)] [InlineData(55, 4)] [InlineData(50, 6)] [InlineData(40, 9)]
    public void Cuanto_mas_al_norte_menos_Kp_hace_falta(double lat, double esperado) => Assert.Equal(esperado, ServicioAurora.KpNecesario(lat));

    [Theory] [InlineData(30)] [InlineData(-45)]
    public void Demasiado_al_sur_no_hay_aurora_posible(double lat) => Assert.Null(ServicioAurora.KpNecesario(lat));

    [Fact]
    public void Lee_las_dos_formas_de_la_respuesta_del_NOAA()
    {
        var objetos = ServicioAurora.Parsear(JsonDocument.Parse("""[{"time_tag":"2026-11-04T21:00:00","kp":5.33,"observed":"predicted","noaa_scale":"G1"}]""").RootElement);
        var filas = ServicioAurora.Parsear(JsonDocument.Parse("""[["time_tag","kp","observed","noaa_scale"],["2026-11-04 21:00:00","5.33","predicted","G1"]]""").RootElement);
        Assert.Equal(5.33, Assert.Single(objetos).Kp);
        Assert.Equal(5.33, Assert.Single(filas).Kp);                 // la cabecera se ignora
        Assert.Empty(ServicioAurora.Parsear(JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void Es_posible_si_el_Kp_maximo_alcanza_el_necesario_y_cuenta_solo_lo_que_viene()
    {
        var kps = new List<KpPrevisto> { new(Ahora.AddDays(-2), 9), new(Ahora.AddHours(2), 3), new(Ahora.AddHours(5), 6.33) };
        var norte = ServicioAurora.Evaluar(kps, 58, Ahora);
        Assert.True(norte.Posible); Assert.Equal(6.33, norte.KpMaximo);   // el 9 de hace dos días no cuenta
        var sur = ServicioAurora.Evaluar(kps, 40.4, Ahora);
        Assert.False(sur.Posible); Assert.Contains("hace falta 9", sur.Resumen);
        Assert.False(ServicioAurora.Evaluar([], 58, Ahora).Posible);
    }
}
