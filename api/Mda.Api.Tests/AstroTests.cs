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

    // ---- acimut, efemérides y sombra (para la pantalla «Cuándo salir a hacer fotografía») ----

    [Fact]
    public void En_el_solsticio_el_sol_sale_por_el_nordeste_y_se_pone_por_el_noroeste()
    {
        var e = Astro.Dia(new DateOnly(2026, 6, 21), Madrid, Lat, Lon);
        Assert.InRange(e.AzAmanecer!.Value, 55, 62);   // ~58°
        Assert.InRange(e.AzAtardecer!.Value, 298, 305); // ~302°
        Assert.InRange(TimeZoneInfo.ConvertTime(e.Amanecer!.Value, Madrid).TimeOfDay, new TimeSpan(6, 40, 0), new TimeSpan(7, 5, 0));
        Assert.InRange(TimeZoneInfo.ConvertTime(e.Atardecer!.Value, Madrid).TimeOfDay, new TimeSpan(21, 42, 0), new TimeSpan(21, 55, 0));
        Assert.InRange(e.AlturaMediodia, 72, 74);
    }

    [Fact]
    public void Al_mediodia_solar_el_sol_esta_al_sur_y_a_media_noche_al_norte()
    {
        var e = Astro.Dia(new DateOnly(2026, 6, 21), Madrid, Lat, Lon);
        Assert.InRange(Astro.PosSol(e.Mediodia, Lat, Lon).Az, 178, 182);
        var norte = Astro.PosSol(e.Mediodia.AddHours(12), Lat, Lon);
        Assert.True(norte.Alt < 0);
        Assert.True(norte.Az is < 10 or > 350);
    }

    [Fact]
    public void Los_crepusculos_van_en_orden_y_en_el_norte_en_verano_no_hay_noche_astronomica()
    {
        var invierno = Astro.Dia(new DateOnly(2026, 12, 21), Madrid, Lat, Lon);
        Assert.True(invierno.AstroManana < invierno.NauticoManana && invierno.NauticoManana < invierno.CivilManana && invierno.CivilManana < invierno.Amanecer);
        Assert.True(invierno.Atardecer < invierno.CivilTarde && invierno.CivilTarde < invierno.NauticoTarde && invierno.NauticoTarde < invierno.AstroTarde);
        // en el solsticio de verano el sol solo baja a −26° en Madrid, así que sí hay noche astronómica; en latitudes altas no
        Assert.NotNull(Astro.Dia(new DateOnly(2026, 6, 21), Madrid, Lat, Lon).AstroManana);
        Assert.Null(Astro.Dia(new DateOnly(2026, 6, 21), Madrid, 62, 10).AstroManana);
    }

    [Fact]
    public void La_luna_sale_por_el_este_y_se_pone_por_el_oeste()
    {
        var e = Astro.Dia(new DateOnly(2026, 11, 10), Madrid, Lat, Lon);
        if (e.LunaSale is { } sale) Assert.InRange(Astro.PosLuna(sale, Lat, Lon).Az, 40, 140);
        if (e.LunaPone is { } pone) Assert.InRange(Astro.PosLuna(pone, Lat, Lon).Az, 220, 320);
        Assert.True(e.LunaSale is not null || e.LunaPone is not null);
    }

    [Fact]
    public void La_sombra_mide_lo_que_dice_la_trigonometria()
    {
        Assert.Equal(1.0, Astro.Sombra(45)!.Value, 3);
        Assert.Equal(1 / Math.Tan(10 * Math.PI / 180), Astro.Sombra(10)!.Value, 3);
        Assert.Null(Astro.Sombra(0.5));  // sol en el horizonte: sombra infinita
        Assert.Null(Astro.Sombra(-20)); // de noche no hay
        Assert.Equal(40, Astro.Sombra(1.1)!.Value); // se corta para no salirse de la pantalla
    }

    [Theory]
    [InlineData(-30, "noche")] [InlineData(-15, "astronomico")] [InlineData(-8, "nautico")] [InlineData(-5, "azul")] [InlineData(0, "dorada")] [InlineData(30, "dia")]
    public void La_etapa_depende_de_la_altura_del_sol(double alt, string etapa) => Assert.Equal(etapa, Astro.Etapa(alt));

    [Fact]
    public void Un_momento_dorado_con_nube_alta_puntua_mas_que_con_nube_baja_y_sin_prevision_no_hay_nota()
    {
        var alta = new HoraNubes(DateTime.Today, 50, 5, 30, 60, 0, 24, 5);
        var baja = new HoraNubes(DateTime.Today, 90, 85, 10, 10, 20, 24, 5);
        Assert.True(Astro.PuntuarMomento(2, -40, -30, 0.5, alta, 5) > Astro.PuntuarMomento(2, -40, -30, 0.5, baja, 5));
        Assert.Null(Astro.PuntuarMomento(2, -40, -30, 0.5, null, 5));
        // el día pleno es luz dura: nunca pasa de 6
        Assert.True(Astro.PuntuarMomento(60, -40, -30, 0.5, new HoraNubes(DateTime.Today, 0, 0, 0, 0, 0, 30, 0), 5) <= 6);
        // de noche con el núcleo alto y sin luna se puntúa mejor que sin núcleo
        var despejado = new HoraNubes(DateTime.Today, 0, 0, 0, 0, 0, 30, 0);
        Assert.True(Astro.PuntuarMomento(-30, 30, -20, 0.05, despejado, 3) > Astro.PuntuarMomento(-30, -20, -20, 0.05, despejado, 3));
    }

    [Fact]
    public void El_porque_resume_las_nubes()
    {
        HoraNubes[] Una(int total, int baja, int media, int alta, int lluvia) => [new HoraNubes(DateTime.Today, total, baja, media, alta, lluvia, 20, 5)];
        Assert.Equal("riesgo de lluvia", Astro.Porque(Una(80, 40, 40, 40, 70)));
        Assert.Equal("nube baja que tapa el sol", Astro.Porque(Una(80, 70, 10, 10, 0)));
        Assert.Equal("cielo cubierto", Astro.Porque(Una(95, 30, 80, 80, 0)));
        Assert.Equal("cielo despejado", Astro.Porque(Una(3, 0, 0, 3, 0)));
        Assert.Contains("nubes altas", Astro.Porque(Una(50, 5, 30, 50, 0)));
        Assert.Equal("", Astro.Porque([]));
    }
}
