using Mda.Api.Luz;

namespace Mda.Api.Tests;

// Madrid: 40,4168 N, 3,7038 O
public class AstroTests
{
    private const double Lat = 40.4168, Lon = -3.7038;
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
    private static DateTimeOffset Local(int a, int m, int d, int h, int min = 0)
    {
        var n = new DateTime(a, m, d, h, min, 0);
        return new DateTimeOffset(n, Madrid.GetUtcOffset(n));
    }

    [Fact]
    public void Al_mediodia_solar_del_solsticio_el_sol_esta_a_73_grados()
    {
        var maxima = Astro.Tramos(_ => true, Local(2026, 6, 21, 0), Local(2026, 6, 22, 0)).Count; // solo para ejercitar Tramos
        Assert.Equal(1, maxima);
        var max = Enumerable.Range(0, 24 * 60).Max(m => Astro.AlturaSol(Local(2026, 6, 21, 0).AddMinutes(m), Lat, Lon));
        Assert.InRange(max, 72.0, 74.0); // 90 - 40,42 + 23,44
    }

    [Fact]
    public void La_puesta_de_sol_del_solsticio_en_madrid_es_hacia_las_21_47()
    {
        var p = Astro.Tramos(t => Astro.AlturaSol(t, Lat, Lon) > -0.833, Local(2026, 6, 21, 12), Local(2026, 6, 22, 0)).Single().Fin;
        var local = TimeZoneInfo.ConvertTime(p, Madrid);
        Assert.InRange(local.TimeOfDay, new TimeSpan(21, 42, 0), new TimeSpan(21, 55, 0)); // mediodía solar a las 14:15 y 15 h de día
    }

    [Fact]
    public void La_hora_dorada_de_la_tarde_acaba_pasada_la_puesta()
    {
        var (manana, tarde) = Astro.HoraDorada(new DateOnly(2026, 6, 21), Madrid, Lat, Lon);
        Assert.NotNull(manana);
        Assert.NotNull(tarde);
        var t = TimeZoneInfo.ConvertTime(tarde!.Inicio, Madrid);
        Assert.InRange(t.TimeOfDay, new TimeSpan(20, 55, 0), new TimeSpan(21, 25, 0)); // sol a 6° unos 35-40 min antes de la puesta
        Assert.InRange(tarde.Duracion.TotalMinutes, 40, 90);
    }

    [Fact]
    public void La_luna_es_nueva_en_la_referencia_y_llena_a_las_dos_semanas()
    {
        Assert.InRange(Astro.IluminacionLuna(new DateTimeOffset(2000, 1, 6, 18, 14, 0, TimeSpan.Zero)), 0, 0.01);
        Assert.InRange(Astro.IluminacionLuna(new DateTimeOffset(2000, 1, 21, 10, 0, 0, TimeSpan.Zero)), 0.97, 1.0);
        Assert.Equal("luna nueva", Astro.NombreFase(new DateTimeOffset(2000, 1, 6, 20, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void En_verano_hay_noches_con_nucleo_galactico_y_en_diciembre_ninguna()
    {
        // sin luna ni nubes, alguna noche de julio tiene ventana; en pleno diciembre el núcleo nunca sale por encima de 10°
        var julio = Enumerable.Range(1, 31).Select(d => Astro.NocheOscura(new DateOnly(2026, 7, d), Madrid, Lat, Lon)).Where(t => t is not null).ToList();
        Assert.NotEmpty(julio);
        Assert.All(julio, t => Assert.True(t!.Duracion >= TimeSpan.FromMinutes(20)));
        var diciembre = Enumerable.Range(1, 31).Select(d => Astro.NocheOscura(new DateOnly(2026, 12, d), Madrid, Lat, Lon));
        Assert.All(diciembre, Assert.Null);
    }

    [Fact]
    public void El_centro_galactico_pasa_hacia_los_20_grados_en_madrid()
    {
        var max = Enumerable.Range(0, 24 * 60).Max(m => Astro.AlturaNucleo(Local(2026, 7, 15, 0).AddMinutes(m), Lat, Lon));
        Assert.InRange(max, 19.5, 22.0); // 90 - 40,42 - 28,94
    }

    private static HoraNubes H(int total, int baja, int media, int alta, int lluvia = 0, double vis = 24) => new(new DateTime(2026, 6, 21, 21, 0, 0), total, baja, media, alta, lluvia, vis, 8);

    [Fact]
    public void Una_tarde_con_nubes_altas_y_sin_nube_baja_puntua_mas_que_una_tapada_o_con_lluvia()
    {
        var bonita = Astro.PuntuarDorada([H(45, 0, 40, 60)]);
        var tapada = Astro.PuntuarDorada([H(95, 90, 50, 20)]);
        var lluvia = Astro.PuntuarDorada([H(100, 70, 70, 70, lluvia: 90)]);
        Assert.True(bonita >= 8, $"bonita {bonita}");
        Assert.True(tapada <= 3, $"tapada {tapada}");
        Assert.True(lluvia < tapada || lluvia <= 2, $"lluvia {lluvia}");
        Assert.Equal(0, Astro.PuntuarDorada([]));
    }

    [Fact]
    public void La_noche_despejada_y_sin_luna_puntua_alto_y_baja_con_nubes_luna_o_ciudad()
    {
        var limpia = Astro.PuntuarNoche([H(0, 0, 0, 0)], 0.02, false, 3);
        Assert.True(limpia >= 9, $"limpia {limpia}");
        Assert.True(Astro.PuntuarNoche([H(80, 40, 40, 40)], 0.02, false, 3) < limpia - 4);
        Assert.True(Astro.PuntuarNoche([H(0, 0, 0, 0)], 0.9, true, 3) < limpia - 2);
        Assert.True(Astro.PuntuarNoche([H(0, 0, 0, 0)], 0.02, false, 9) < limpia - 2);
        Assert.InRange(Astro.PuntuarNoche([H(100, 100, 100, 100, 100)], 1, true, 9), 0, 0);
    }
}
