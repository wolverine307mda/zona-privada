using Mda.Api.Luz;

namespace Mda.Api.Tests;

public class EventosTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Las_lluvias_se_repiten_cada_ano_y_salen_por_orden()
    {
        var l = Eventos.Proximos(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero), Utc, 120);
        Assert.Equal(["Táuridas del sur", "Leónidas", "Gemínidas", "Úrsidas", "Cuadrántidas"], l.Where(e => e.Tipo == "lluvia").Select(e => e.Nombre));
        Assert.Equal(l.OrderBy(e => e.Pico).Select(e => e.Nombre), l.Select(e => e.Nombre));
    }

    [Fact]
    public void Cuenta_los_eclipses_que_caen_en_la_ventana_y_no_los_pasados()
    {
        var l = Eventos.Proximos(new DateTimeOffset(2027, 7, 1, 0, 0, 0, TimeSpan.Zero), Utc, 60);
        Assert.Contains(l, e => e.Tipo == "eclipse-solar" && e.Pico.Year == 2027);
        Assert.DoesNotContain(l, e => e.Pico.Year == 2026);
    }

    [Fact]
    public void La_luna_del_pico_se_calcula_y_clasifica_el_cielo()
    {
        var l = Eventos.Proximos(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), Utc, 30);
        var perseidas = Assert.Single(l, e => e.Nombre == "Perseidas");
        Assert.Equal(100, perseidas.Zhr);
        Assert.InRange(perseidas.LunaPct, 0, 100);
        Assert.Contains(perseidas.Cielo, new[] { "cielo oscuro", "luna discreta", "luna molesta", "luna que borra casi todo" });
    }
}
