using System.Net;
using System.Net.Http.Json;
using Mda.Api.Gastos;

namespace Mda.Api.Tests;

public class InformeTests
{
    private static Movimiento M(TipoMovimiento t, int dia, decimal importe, string cat, string concepto = "x", int mes = 10) =>
        new() { Tipo = t, Fecha = new DateOnly(2026, mes, dia), Importe = importe, Categoria = cat, Concepto = concepto };

    [Fact]
    public void Calcula_totales_porcentajes_mayores_y_presupuestos()
    {
        var mes = new List<Movimiento>
        {
            M(TipoMovimiento.Ingreso, 1, 2000, "nomina"), M(TipoMovimiento.Gasto, 3, 300, "supermercado", "Mercadona"), M(TipoMovimiento.Gasto, 5, 100, "ocio", "Cine"),
        };
        var ant = new List<Movimiento> { M(TipoMovimiento.Ingreso, 1, 1900, "nomina", mes: 9), M(TipoMovimiento.Gasto, 2, 250, "supermercado", mes: 9) };
        var i = Informe.Calcular(2026, 10, mes, ant, [new Presupuesto { Categoria = "supermercado", Limite = 250 }], 1234m);
        Assert.Equal(2000, i.Ingresos); Assert.Equal(400, i.Gastos); Assert.Equal(1600, i.Ahorro);
        Assert.Equal(1900, i.IngresosAnt); Assert.Equal(250, i.GastosAnt);
        Assert.Equal(75, i.Categorias[0].Porcentaje);
        Assert.Equal("Mercadona", i.Mayores[0].Concepto);
        Assert.Equal(120, i.Presupuestos[0].Porcentaje);
        Assert.Equal(1234m, i.SaldoFin);
        Assert.Equal(3, i.Movimientos);
    }

    [Fact]
    public void Un_mes_vacio_tambien_se_calcula_y_se_pinta()
    {
        var i = Informe.Calcular(2026, 10, [], [], [], null);
        Assert.Equal(0, i.Gastos); Assert.Empty(i.Categorias);
        var pdf = Informe.Pdf(i);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void El_pdf_con_datos_es_un_pdf_de_verdad()
    {
        var i = Informe.Calcular(2026, 10, [M(TipoMovimiento.Gasto, 3, 42.5m, "supermercado", "Mercadona"), M(TipoMovimiento.Ingreso, 1, 1500, "nomina")], [], [new Presupuesto { Categoria = "supermercado", Limite = 40 }], 900);
        var pdf = Informe.Pdf(i);
        Assert.True(pdf.Length > 1500);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal("informe-2026-10.pdf", Informe.NombreArchivo(2026, 10));
    }
}

public class InformeApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Sin_sesion_no_hay_informe() => Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/gastos/informe")).StatusCode);

    [Fact]
    public async Task Descarga_el_pdf_y_valida_el_mes()
    {
        var c = await factory.Dentro();
        var r = await c.GetAsync("/api/gastos/informe?anio=2026&mes=10");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString((await r.Content.ReadAsByteArrayAsync())[..4]));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/gastos/informe?anio=2026&mes=13")).StatusCode);
    }
}
