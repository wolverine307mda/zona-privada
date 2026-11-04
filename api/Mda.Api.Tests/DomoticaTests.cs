using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Domotica;

namespace Mda.Api.Tests;

public class CatalogoCasaTests
{
    [Theory]
    [InlineData("luz", "brillo", "50", "50")]
    [InlineData("luz", "encendido", "true", "true")]
    [InlineData("luz", "color", "#FFB347", "#ffb347")]
    [InlineData("clima", "objetivo", "23.50", "23.5")]
    [InlineData("clima", "modo", "frio", "frio")]
    public void Acepta_y_normaliza_valores_validos(string tipo, string clave, string bruto, string esperado)
    {
        var (v, e) = Catalogo.Normalizar(Catalogo.Buscar(tipo, clave)!, bruto);
        Assert.Null(e);
        Assert.Equal(esperado, v);
    }

    [Theory]
    [InlineData("luz", "brillo", "150")]
    [InlineData("luz", "brillo", "mucho")]
    [InlineData("luz", "color", "rojo")]
    [InlineData("clima", "objetivo", "5")]
    [InlineData("clima", "modo", "turbo")]
    [InlineData("luz", "encendido", "quizá")]
    public void Rechaza_valores_fuera_de_rango_o_de_formato(string tipo, string clave, string bruto) =>
        Assert.NotNull(Catalogo.Normalizar(Catalogo.Buscar(tipo, clave)!, bruto).Error);

    [Fact]
    public void Un_dato_de_solo_lectura_no_se_puede_ordenar() =>
        Assert.NotNull(Catalogo.Normalizar(Catalogo.Buscar("sensor", "temperatura")!, "20").Error);
}

public class DomoticaTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Un_termometro_lee_su_temperatura_y_el_ejemplo_se_carga_solo_con_la_casa_vacia()
    {
        var c = await factory.Dentro();
        var id = (await Json(await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre = "Termómetro", tipo = "sensor", driver = "demo" }))).GetProperty("id").GetInt32();
        var casa = await c.GetFromJsonAsync<JsonElement>("/api/domotica");
        var termo = casa.GetProperty("aparatos").EnumerateArray().First(a => a.GetProperty("id").GetInt32() == id);
        var temperatura = termo.GetProperty("entidades").EnumerateArray().First(e => e.GetProperty("clave").GetString() == "temperatura");
        Assert.True(double.Parse(temperatura.GetProperty("valor").GetString()!, System.Globalization.CultureInfo.InvariantCulture) is > 15 and < 35);
        // ya hay un aparato: el ejemplo se niega a mezclarse
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/domotica/ejemplo", null)).StatusCode);
    }

    [Fact]
    public async Task Una_orden_a_una_lampara_cambia_su_estado_y_valida_el_rango()
    {
        var c = await factory.Dentro();
        var alta = await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre = "Flexo", tipo = "luz", driver = "demo" });
        Assert.Equal(HttpStatusCode.OK, alta.StatusCode);
        var id = (await Json(alta)).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/domotica/aparatos/{id}/ejecutar", new { clave = "encendido", valor = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/domotica/aparatos/{id}/ejecutar", new { clave = "brillo", valor = 35 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/domotica/aparatos/{id}/ejecutar", new { clave = "brillo", valor = 500 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/domotica/aparatos/{id}/ejecutar", new { clave = "volumen", valor = 5 })).StatusCode);

        var casa = await c.GetFromJsonAsync<JsonElement>("/api/domotica");
        var flexo = casa.GetProperty("aparatos").EnumerateArray().First(a => a.GetProperty("id").GetInt32() == id);
        string? Valor(string clave) => flexo.GetProperty("entidades").EnumerateArray().First(e => e.GetProperty("clave").GetString() == clave).GetProperty("valor").GetString();
        Assert.Equal("true", Valor("encendido"));
        Assert.Equal("35", Valor("brillo"));
    }

    [Fact]
    public async Task Un_aparato_con_un_conector_sin_activar_avisa_en_vez_de_fallar()
    {
        var c = await factory.Dentro();
        var id = (await Json(await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre = "Bombilla Tuya", tipo = "luz", driver = "tuya" }))).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/domotica/aparatos/{id}/ejecutar", new { clave = "encendido", valor = true })).StatusCode);
    }

    [Fact]
    public async Task Las_habitaciones_se_crean_ordenan_y_borrar_una_deja_los_aparatos_sin_habitacion()
    {
        var c = await factory.Dentro();
        var estancia = await Json(await c.PostAsJsonAsync("/api/domotica/estancias", new { nombre = "Despacho", icono = "💻" }));
        var eid = estancia.GetProperty("id").GetInt32();
        var aid = (await Json(await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre = "Enchufe PC", tipo = "enchufe", estanciaId = eid }))).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/domotica/estancias/{eid}")).StatusCode);
        var casa = await c.GetFromJsonAsync<JsonElement>("/api/domotica");
        var enchufe = casa.GetProperty("aparatos").EnumerateArray().First(a => a.GetProperty("id").GetInt32() == aid);
        Assert.Equal(JsonValueKind.Null, enchufe.GetProperty("estanciaId").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/domotica/estancias", new { nombre = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre = "X", tipo = "nave-espacial" })).StatusCode);
    }

    [Fact]
    public async Task El_panel_se_guarda_y_vuelve_igual()
    {
        var c = await factory.Dentro();
        var panel = new { tarjetas = new[] { new { id = "a", tipo = "sensor", aparatoId = 1 } } };
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/domotica/panel", new { panel })).StatusCode);
        var casa = await c.GetFromJsonAsync<JsonElement>("/api/domotica");
        Assert.Contains("\"tarjetas\"", casa.GetProperty("panel").GetString());
    }

    [Fact]
    public async Task Sin_sesion_no_se_ve_nada_de_la_casa()
    {
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/domotica")).StatusCode);
    }
}
