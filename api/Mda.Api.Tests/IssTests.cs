using Mda.Api.Luz;

namespace Mda.Api.Tests;

public class IssTests
{
    // El TLE de ejemplo del informe n.º 3 de Spacetrack (ISS, septiembre de 2008): datos reales y conocidos
    private const string L1 = "1 25544U 98067A   08264.51782528 -.00002182  00000-0 -11606-4 0  2927";
    private const string L2 = "2 25544  51.6416 247.4627 0006703 130.5360 325.0288 15.72125391563537";
    private static readonly DateTimeOffset Desde = new(2008, 9, 21, 0, 0, 0, TimeSpan.Zero);
    private const double Madrid = 40.4, MadridLon = -3.7;

    [Fact]
    public void Sin_filtro_de_oscuridad_la_iss_sube_varias_veces_al_dia_sobre_Madrid()
    {
        var todas = Pasadas.Calcular(L1, L2, Madrid, MadridLon, Desde, 3, soloVisibles: false);
        Assert.InRange(todas.Count, 6, 20);       // 15,7 órbitas al día, con una parte que pasa lejos
        Assert.All(todas, p => { Assert.True(p.Inicio < p.Maximo && p.Maximo < p.Fin); Assert.InRange(p.AlturaMax, 10, 90); });
        // una pasada dura como mucho unos 10 minutos
        Assert.All(todas, p => Assert.True(p.Fin - p.Inicio < TimeSpan.FromMinutes(12)));
    }

    [Fact]
    public void Las_visibles_ocurren_con_cielo_oscuro_y_son_menos_que_todas()
    {
        var todas = Pasadas.Calcular(L1, L2, Madrid, MadridLon, Desde, 10, soloVisibles: false);
        var visibles = Pasadas.Calcular(L1, L2, Madrid, MadridLon, Desde, 10);
        Assert.True(visibles.Count < todas.Count);
        Assert.All(visibles, p => Assert.True(Astro.AlturaSol(p.Maximo, Madrid, MadridLon) <= Pasadas.SolMaximo));
    }

    [Fact]
    public void Los_rumbos_se_redondean_a_ocho_direcciones()
    {
        Assert.Equal("N", Pasadas.Rumbo(0));
        Assert.Equal("N", Pasadas.Rumbo(359));
        Assert.Equal("NE", Pasadas.Rumbo(45));
        Assert.Equal("SO", Pasadas.Rumbo(225));
        Assert.Equal("O", Pasadas.Rumbo(271));
    }

    [Fact]
    public void Un_punto_del_lado_de_noche_pero_fuera_del_cilindro_esta_iluminado_y_dentro_no()
    {
        var sol = (X: 1.0, Y: 0.0, Z: 0.0);
        Assert.True(Pasadas.Iluminado(7000, 0, 0, sol));          // del lado del Sol
        Assert.False(Pasadas.Iluminado(-7000, 0, 0, sol));        // justo detrás de la Tierra: sombra
        Assert.True(Pasadas.Iluminado(-7000, 7000, 0, sol));      // detrás, pero lejos del eje: iluminado
    }

    [Fact]
    public void Lee_un_tle_con_o_sin_nombre_y_su_epoca()
    {
        var con = Pasadas.LeerTle($"ISS (ZARYA)\r\n{L1}\r\n{L2}\r\n");
        Assert.NotNull(con);
        Assert.Equal(L1, con!.Value.L1);
        Assert.NotNull(Pasadas.LeerTle($"{L1}\n{L2}"));
        Assert.Null(Pasadas.LeerTle("Not Found"));
        var epoca = Pasadas.Epoca(L1)!.Value;
        Assert.Equal(2008, epoca.Year);
        Assert.Equal(9, epoca.Month);
        Assert.Equal(20, epoca.Day);                               // día 264,5 de 2008 = 20 de septiembre
    }

    [Fact]
    public void La_direccion_del_sol_es_un_vector_unitario_y_cambia_con_la_estacion()
    {
        var (x, y, z) = Pasadas.DireccionSol(new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, Math.Sqrt(x * x + y * y + z * z), 3);
        Assert.True(z > 0.38);                                      // en el solsticio de junio el Sol está unos 23° al norte del ecuador
        Assert.True(Pasadas.DireccionSol(new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc)).Z < -0.38);
    }

    private static PasadaIss P(DateTimeOffset inicio, double altura) => new(inicio, inicio.AddMinutes(3), inicio.AddMinutes(6), altura, 200, 90, 20, "SO", "NE");
    private static readonly TimeZoneInfo Madrid1 = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    [Fact]
    public void Avisa_de_una_buena_que_empieza_en_menos_de_45_minutos_y_no_de_una_floja_ni_de_una_lejana()
    {
        var ahora = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
        Assert.NotNull(Pasadas.ParaAvisar([P(ahora.AddMinutes(30), 62)], ahora, Madrid1, null));
        Assert.Null(Pasadas.ParaAvisar([P(ahora.AddMinutes(30), 25)], ahora, Madrid1, null));              // sube poco
        Assert.Null(Pasadas.ParaAvisar([P(ahora.AddHours(3), 62)], ahora, Madrid1, null));                 // aún falta
        Assert.Null(Pasadas.ParaAvisar([P(ahora.AddMinutes(-5), 62)], ahora, Madrid1, null));              // ya empezó
    }

    [Fact]
    public void No_repite_la_misma_pasada()
    {
        var ahora = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
        var p = P(ahora.AddMinutes(30), 62);
        Assert.Null(Pasadas.ParaAvisar([p], ahora, Madrid1, p.Inicio));
        Assert.NotNull(Pasadas.ParaAvisar([p], ahora, Madrid1, p.Inicio.AddHours(-2)));
    }

    [Fact]
    public void Por_la_noche_avisa_de_la_que_hay_de_madrugada_porque_despues_no_hay_repaso()
    {
        var noche = new DateTimeOffset(2026, 11, 5, 20, 30, 0, TimeSpan.Zero);                // 21:30 en Madrid
        var madrugada = new DateTimeOffset(2026, 11, 6, 5, 40, 0, TimeSpan.Zero);             // 06:40 en Madrid
        Assert.NotNull(Pasadas.ParaAvisar([P(madrugada, 70)], noche, Madrid1, null));
        var tarde = new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);                 // a las 16:00 aún no toca
        Assert.Null(Pasadas.ParaAvisar([P(madrugada, 70)], tarde, Madrid1, null));
        var mediodiaSiguiente = new DateTimeOffset(2026, 11, 6, 11, 0, 0, TimeSpan.Zero);     // una pasada de las 12:00 de mañana no entra
        Assert.Null(Pasadas.ParaAvisar([P(mediodiaSiguiente, 70)], noche, Madrid1, null));
    }
}
