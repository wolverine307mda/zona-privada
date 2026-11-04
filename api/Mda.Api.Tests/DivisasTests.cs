using Mda.Api.Gastos;

namespace Mda.Api.Tests;

public class DivisasTests
{
    private const string Xml = """
        <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
          <Cube>
            <Cube time="2026-11-03"><Cube currency="USD" rate="1.0800"/><Cube currency="GBP" rate="0.8600"/></Cube>
            <Cube time="2026-10-30"><Cube currency="USD" rate="1.1000"/></Cube>
          </Cube>
        </gesmes:Envelope>
        """;

    [Fact]
    public void Lee_los_dias_y_las_monedas_del_xml_del_BCE()
    {
        var t = ServicioDivisas.Parsear(Xml);
        Assert.Equal(2, t.Count);
        Assert.Equal(1.08m, t[new DateOnly(2026, 11, 3)]["USD"]);
        Assert.Equal(0.86m, t[new DateOnly(2026, 11, 3)]["GBP"]);
    }

    [Fact]
    public void Convierte_con_el_tipo_del_dia_y_si_es_fin_de_semana_con_el_ultimo_anterior()
    {
        var t = ServicioDivisas.Parsear(Xml);
        var dia = ServicioDivisas.Convertir(t, "usd", 108, new DateOnly(2026, 11, 3))!;
        Assert.Equal(100m, dia.Euros);
        Assert.Equal(new DateOnly(2026, 11, 3), dia.FechaTipo);
        var finde = ServicioDivisas.Convertir(t, "USD", 110, new DateOnly(2026, 11, 1))!;      // domingo → viernes 30
        Assert.Equal(100m, finde.Euros);
        Assert.Equal(new DateOnly(2026, 10, 30), finde.FechaTipo);
    }

    [Fact]
    public void Con_fechas_fuera_de_rango_usa_la_mas_cercana_y_lo_imposible_es_nulo()
    {
        var t = ServicioDivisas.Parsear(Xml);
        Assert.Equal(new DateOnly(2026, 11, 3), ServicioDivisas.Convertir(t, "GBP", 86, new DateOnly(2026, 12, 25))!.FechaTipo);
        Assert.Null(ServicioDivisas.Convertir(t, "EUR", 10, new DateOnly(2026, 11, 3)));
        Assert.Null(ServicioDivisas.Convertir(t, "XXX", 10, new DateOnly(2026, 11, 3)));
        Assert.Null(ServicioDivisas.Convertir(t, "USD", 0, new DateOnly(2026, 11, 3)));
    }

    [Theory] [InlineData("USD", true)] [InlineData("usd", false)] [InlineData("US", false)] [InlineData(null, false)]
    public void Valida_el_codigo_de_moneda(string? m, bool ok) => Assert.Equal(ok, ServicioDivisas.MonedaValida(m));
}
