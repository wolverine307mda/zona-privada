using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Gastos;

namespace Mda.Api.Tests;

public class ExtractosPurosTests
{
    [Theory]
    [InlineData("1.234,56", 1234.56)] [InlineData("-12,50", -12.5)] [InlineData("12.50", 12.5)] [InlineData("1,234.56", 1234.56)]
    [InlineData("1.234", 1234)] [InlineData("(45,00)", -45)] [InlineData("-3,20 €", -3.2)] [InlineData("0,99", 0.99)]
    public void Entiende_los_importes_a_la_espanola_y_a_la_inglesa(string texto, double esperado) => Assert.Equal((decimal)esperado, Extractos.Importe(texto));

    [Theory] [InlineData("")] [InlineData("abc")]
    public void Lo_que_no_es_un_numero_es_nulo(string texto) => Assert.Null(Extractos.Importe(texto));

    [Theory]
    [InlineData("03/11/2026", 2026, 11, 3)] [InlineData("3-11-2026", 2026, 11, 3)] [InlineData("2026-11-03", 2026, 11, 3)] [InlineData("03/11/26", 2026, 11, 3)] [InlineData("03/11/2026 10:15", 2026, 11, 3)]
    public void Entiende_las_fechas_habituales(string texto, int a, int m, int d) => Assert.Equal(new DateOnly(a, m, d), Extractos.Fecha(texto));

    [Fact]
    public void Detecta_el_separador_y_respeta_las_comillas()
    {
        var csv = "Fecha;Concepto;Importe\n03/11/2026;\"Bar; el Pepe\";-12,50\r\n04/11/2026;Nómina;1.500,00\n";
        Assert.Equal(';', Extractos.Separador(csv));
        var f = Extractos.Parsear(csv, ';');
        Assert.Equal(3, f.Count);
        Assert.Equal("Bar; el Pepe", f[1][1]);
        Assert.Equal("1.500,00", f[2][2]);
    }

    [Fact]
    public void Reconoce_las_columnas_por_su_nombre_con_importe_o_con_cargo_y_abono()
    {
        Assert.Equal(new MapeoExtracto(0, 2, 3, null, null), Extractos.Detectar(["Fecha operación", "Fecha valor", "Concepto", "Importe", "Saldo"]));
        Assert.Equal(new MapeoExtracto(0, 1, null, 2, 3), Extractos.Detectar(["Fecha", "Descripción", "Cargo", "Abono"]));
        Assert.Null(Extractos.Detectar(["a", "b", "c"]));
    }

    [Fact]
    public void Lee_las_filas_con_tipo_categoria_duplicados_y_errores()
    {
        var csv = "Fecha;Concepto;Importe\n03/11/2026;Mercadona 0123;-40,10\n04/11/2026;Nómina;1500,00\nmala;fila;x\n05/11/2026;Cafe;-2,00";
        var filas = Extractos.Parsear(csv, ';');
        var (res, err) = Extractos.Leer(filas, new MapeoExtracto(0, 1, 2, null, null), new Dictionary<string, string> { ["mercadona"] = "supermercado" },
            (f, c, i) => c == "Cafe");
        Assert.Equal(3, res.Count);
        Assert.Single(err);
        Assert.Equal(("Gasto", "supermercado", 40.10m), (res[0].Tipo, res[0].Categoria, res[0].Importe));
        Assert.Equal(("Ingreso", "otros-ingresos"), (res[1].Tipo, res[1].Categoria));
        Assert.True(res[2].Duplicado);
        Assert.False(res[0].Duplicado);
    }
}

public class ExtractosApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private const string Csv = "Fecha;Concepto;Importe\n03/11/2026;Ferretería Sol;-18,90\n04/11/2026;Devolución Amazon;25,00\n";

    [Fact]
    public async Task Sin_sesion_no_entra() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/gastos/importar/analizar", new { texto = Csv })).StatusCode);

    [Fact]
    public async Task Analiza_importa_y_la_segunda_vez_marca_duplicados()
    {
        var c = await factory.Dentro();
        var a = await (await c.PostAsJsonAsync("/api/gastos/importar/analizar", new { texto = Csv })).Content.ReadFromJsonAsync<JsonElement>();
        var filas = a.GetProperty("filas").EnumerateArray().ToList();
        Assert.Equal(2, filas.Count);
        Assert.All(filas, f => Assert.False(f.GetProperty("duplicado").GetBoolean()));
        var cuerpo = filas.Select(f => new { fecha = f.GetProperty("fecha").GetString(), concepto = f.GetProperty("concepto").GetString(), importe = f.GetProperty("importe").GetDecimal(), tipo = f.GetProperty("tipo").GetString(), categoria = f.GetProperty("categoria").GetString() }).ToList();
        var r = await c.PostAsJsonAsync("/api/gastos/importar", new { filas = cuerpo });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(2, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("importados").GetInt32());
        var otra = await (await c.PostAsJsonAsync("/api/gastos/importar/analizar", new { texto = Csv })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(otra.GetProperty("filas").EnumerateArray(), f => Assert.True(f.GetProperty("duplicado").GetBoolean()));
    }

    [Fact]
    public async Task Rechaza_archivos_vacios_y_filas_invalidas()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/gastos/importar/analizar", new { texto = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/gastos/importar", new { filas = new[] { new { fecha = "x", concepto = "a", importe = 5, tipo = "Gasto", categoria = "otros" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/gastos/importar", new { filas = Array.Empty<object>() })).StatusCode);
    }
}
