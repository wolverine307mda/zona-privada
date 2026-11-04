using Mda.Api.Luz;

namespace Mda.Api.Tests;

public class NieblaTests
{
    private static HoraNiebla H(double temp = 10, double rocio = 2, double hum = 60, double viento = 5, double bajas = 10, double? t850 = 5, int hora = 7, int dia = 5) =>
        new(new DateTime(2026, 11, dia, hora, 0, 0), temp, rocio, hum, viento, bajas, t850);

    [Fact]
    public void Aire_saturado_y_sin_viento_es_niebla() => Assert.Equal("niebla", ServicioNiebla.Clasificar(H(temp: 6, rocio: 5.2, hum: 96, viento: 3)).Nivel);

    [Fact]
    public void Inversion_con_nubes_bajas_y_poco_viento_es_mar_de_nubes() =>
        Assert.Equal("mar-de-nubes", ServicioNiebla.Clasificar(H(temp: 4, rocio: 3, hum: 95, viento: 6, bajas: 90, t850: 9)).Nivel);

    [Fact]
    public void Con_viento_la_niebla_se_rompe_y_un_dia_normal_no_da_nada()
    {
        Assert.Equal("nada", ServicioNiebla.Clasificar(H(temp: 6, rocio: 5.5, hum: 96, viento: 20)).Nivel);
        Assert.Equal("nada", ServicioNiebla.Clasificar(H()).Nivel);
    }

    [Fact]
    public void Inversion_sin_nubes_bajas_pero_humeda_avisa_de_jirones() =>
        Assert.Equal("inversion", ServicioNiebla.Clasificar(H(temp: 5, rocio: 3, hum: 85, viento: 4, bajas: 20, t850: 8)).Nivel);

    [Fact]
    public void Por_dias_se_queda_con_la_mejor_hora_del_amanecer_e_ignora_la_tarde()
    {
        var horas = new[] { H(hora: 4, temp: 6, rocio: 5.5, hum: 96, viento: 2), H(hora: 8, temp: 4, rocio: 3, hum: 95, viento: 5, bajas: 90, t850: 9), H(hora: 15, temp: 6, rocio: 5.5, hum: 96, viento: 2), H(dia: 6) };
        var d = ServicioNiebla.PorDias(horas);
        Assert.Equal(2, d.Count);
        Assert.Equal("mar-de-nubes", d[0].Nivel);
        Assert.Equal("08:00", d[0].Hora);
        Assert.Equal("nada", d[1].Nivel);
        Assert.Null(d[1].Hora);
    }
}
