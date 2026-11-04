using Mda.Lanzador;

namespace Mda.Lanzador.Tests;

public class CopiasTests
{
    [Fact]
    public void Los_nombres_son_los_mismos_que_los_del_contenedor_de_copias()
    {
        var t = new DateTime(2026, 11, 5, 3, 7, 0);
        Assert.Equal("zonaprivada-20261105-0307.dump", Copia.Nombre(t));
        Assert.Equal("zonaprivada-20261105.dump.age", Copia.CifradoDump(t));
        Assert.Equal("zonaprivada-20261105-claves.tar.age", Copia.CifradoClaves(t));
    }

    [Fact]
    public void El_dump_toca_pasadas_las_tres_y_solo_una_vez_al_dia()
    {
        Assert.False(Copia.TocaDump(new DateTime(2026, 11, 5, 2, 59, 0), []));
        Assert.True(Copia.TocaDump(new DateTime(2026, 11, 5, 3, 0, 0), []));
        Assert.True(Copia.TocaDump(new DateTime(2026, 11, 5, 18, 0, 0), ["zonaprivada-20261104-0301.dump"]));   // si el equipo estaba apagado, al encenderse
        Assert.False(Copia.TocaDump(new DateTime(2026, 11, 5, 18, 0, 0), ["zonaprivada-20261105-0301.dump"]));
    }

    [Fact]
    public void Se_guardan_las_ultimas_y_se_borran_las_mas_viejas()
    {
        var nombres = Enumerable.Range(1, 16).Select(d => $"zonaprivada-202610{d:00}-0300.dump").ToList();
        var sobran = Copia.Sobrantes(nombres, 14);
        Assert.Equal(["zonaprivada-20261002-0300.dump", "zonaprivada-20261001-0300.dump"], sobran);
        Assert.Empty(Copia.Sobrantes(nombres.Take(14), 14));
    }

    [Fact]
    public void El_simulacro_toca_una_vez_por_semana_iso_pasadas_las_cuatro()
    {
        var jueves = new DateTime(2026, 11, 5, 4, 30, 0);
        Assert.Equal("202645", SimulacroCopia.Semana(jueves));
        Assert.Equal(".simulacro-202645", SimulacroCopia.Marca(jueves));
        Assert.False(SimulacroCopia.Toca(new DateTime(2026, 11, 5, 3, 59, 0), _ => false));
        Assert.True(SimulacroCopia.Toca(jueves, _ => false));
        Assert.False(SimulacroCopia.Toca(jueves, m => m == ".simulacro-202645"));
        // el 29 de diciembre de 2025 ya es la semana 1 de 2026 (año ISO, no el natural)
        Assert.Equal("202601", SimulacroCopia.Semana(new DateTime(2025, 12, 29)));
    }

    [Fact]
    public void La_linea_del_simulacro_tiene_el_formato_que_lee_la_api()
    {
        var l = SimulacroCopia.Linea(true, 1, 47, "zonaprivada-20261105-0307.dump", new DateTime(2026, 11, 5, 4, 10, 5, DateTimeKind.Utc));
        Assert.Equal("ok|1|47|zonaprivada-20261105-0307.dump|2026-11-05T04:10:05Z", l);
        Assert.StartsWith("fallo|0|0|", SimulacroCopia.Linea(false, 0, 0, "x.dump", DateTime.UtcNow));
        Assert.True(SimulacroCopia.Vale(1, 10));
        Assert.False(SimulacroCopia.Vale(0, 47));
        Assert.False(SimulacroCopia.Vale(1, 9));
    }
}
