using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Gastos;

namespace Mda.Api.Tests;

public class ReglasPurasTests
{
    [Fact]
    public void Solo_aprende_de_gastos_con_comercio_y_categoria_valida()
    {
        Assert.True(Reglas.Aprendible("Mercadona", "supermercado", TipoMovimiento.Gasto));
        Assert.False(Reglas.Aprendible("Mercadona", "supermercado", TipoMovimiento.Ingreso));
        Assert.False(Reglas.Aprendible("ab", "supermercado", TipoMovimiento.Gasto));
        Assert.False(Reglas.Aprendible("Mercadona", "no-existe", TipoMovimiento.Gasto));
    }

    [Fact]
    public void La_clave_ignora_mayusculas_y_tildes_y_las_reglas_pisan_la_costumbre()
    {
        Assert.Equal(Reglas.Clave("CAFÉ Central"), Reglas.Clave("cafe central"));
        var d = Reglas.Mezclar(new Dictionary<string, string> { ["mercadona"] = "otros", ["bar pepe"] = "restaurantes" },
            [new ReglaCategoria { Comercio = "mercadona", Categoria = "supermercado" }]);
        Assert.Equal("supermercado", d["mercadona"]);
        Assert.Equal("restaurantes", d["bar pepe"]);
    }
}

public class ReglasApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Corregir_la_categoria_crea_una_regla_que_se_puede_borrar()
    {
        var c = await factory.Dentro();
        var r = await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = "2026-11-02", importe = 12.5, categoria = "otros", concepto = "Panadería Lola" });
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Empty((await c.GetFromJsonAsync<JsonElement>("/api/gastos/reglas")).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/gastos/movimientos/{id}", new { tipo = "Gasto", fecha = "2026-11-02", importe = 12.5, categoria = "supermercado", concepto = "Panadería Lola" })).StatusCode);
        var reglas = (await c.GetFromJsonAsync<JsonElement>("/api/gastos/reglas")).EnumerateArray().ToList();
        var regla = Assert.Single(reglas);
        Assert.Equal("supermercado", regla.GetProperty("categoria").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/gastos/reglas/" + regla.GetProperty("comercio").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/gastos/reglas/panaderia%20lola")).StatusCode);
    }
}
