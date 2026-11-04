using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;
using Microsoft.Extensions.Configuration;

namespace Mda.Api.Tests;

public class HogarClienteTests
{
    private sealed class Fabrica(Func<HttpRequestMessage, HttpResponseMessage> responder, List<(HttpMethod, string, string?, string?)>? vistas = null) : IHttpClientFactory
    {
        private sealed class Manejador(Func<HttpRequestMessage, HttpResponseMessage> f, List<(HttpMethod, string, string?, string?)>? vistas) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                vistas?.Add((r.Method, r.RequestUri!.ToString(), r.Headers.Authorization?.ToString(), r.Content is null ? null : await r.Content.ReadAsStringAsync(ct)));
                return f(r);
            }
        }
        public HttpClient CreateClient(string name) => new(new Manejador(responder, vistas));
    }

    private static IConfiguration Config(string? url = "http://192.168.1.20:8123/", string? token = "token-largo") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["HomeAssistant:Url"] = url, ["HomeAssistant:Token"] = token }).Build();

    private const string Estados = """
        [
          {"entity_id":"light.salon","state":"on","attributes":{"friendly_name":"Luz del salón","brightness":128}},
          {"entity_id":"switch.cafetera","state":"off","attributes":{"friendly_name":"Cafetera"}},
          {"entity_id":"sensor.temperatura","state":"21","attributes":{"friendly_name":"Temperatura"}},
          {"entity_id":"lock.puerta","state":"locked","attributes":{"friendly_name":"Puerta"}},
          {"entity_id":"scene.cine","state":"2026-11-01T10:00:00","attributes":{"friendly_name":"Cine"}},
          {"entity_id":"light.sin_nombre","state":"off","attributes":{}},
          {"entity_id":"light.MAL ID","state":"off"}
        ]
        """;

    [Fact]
    public void Solo_se_quedan_las_cosas_que_se_pueden_manejar_con_su_nombre_y_el_brillo_en_porcentaje()
    {
        var l = ClienteHogar.Parsear(JsonDocument.Parse(Estados).RootElement);
        Assert.Equal(["light.salon", "light.sin_nombre", "switch.cafetera", "scene.cine"], l.Select(x => x.Id));        // sin sensores, sin cerraduras, sin ids raros; por dominio
        Assert.Equal("Luz del salón", l[0].Nombre);
        Assert.Equal(50, l[0].Brillo);                                                                                  // 128 de 255
        Assert.Equal("sin nombre", l[1].Nombre);                                                                        // sin nombre amistoso, el del id
        Assert.Null(l[2].Brillo);
    }

    [Fact]
    public void Algo_que_no_es_una_lista_da_una_lista_vacia() => Assert.Empty(ClienteHogar.Parsear(JsonDocument.Parse("{\"x\":1}").RootElement));

    [Theory]
    [InlineData("light.salon", true)] [InlineData("switch.enchufe_1", true)] [InlineData("scene.cine", true)] [InlineData("fan.ventilador", true)] [InlineData("input_boolean.modo", true)]
    [InlineData("lock.puerta", false)] [InlineData("alarm_control_panel.casa", false)] [InlineData("light.", false)] [InlineData("light.a/b", false)] [InlineData("light.Salon", false)] [InlineData("", false)] [InlineData(null, false)]
    public void Solo_esos_dominios_y_ids_en_minuscula(string? id, bool valido) => Assert.Equal(valido, ClienteHogar.IdValido(id));

    [Fact]
    public async Task Listar_pide_los_estados_con_el_token_y_Llamar_manda_la_orden_a_la_entidad()
    {
        var vistas = new List<(HttpMethod, string, string?, string?)>();
        var cliente = new ClienteHogar(new Fabrica(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(r.Method == HttpMethod.Get ? Estados : "[]") }, vistas), Config());

        Assert.True(cliente.Configurado);
        Assert.Equal(4, (await cliente.Listar(default)).Count);
        Assert.Equal((HttpMethod.Get, "http://192.168.1.20:8123/api/states", "Bearer token-largo", null), vistas[0]);

        Assert.True(await cliente.Llamar("light.salon", "alternar", default));
        Assert.Equal("http://192.168.1.20:8123/api/services/light/toggle", vistas[1].Item2);
        Assert.Equal("{\"entity_id\":\"light.salon\"}", vistas[1].Item4);
        Assert.True(await cliente.Llamar("scene.cine", "activar", default));
        Assert.Equal("http://192.168.1.20:8123/api/services/scene/turn_on", vistas[2].Item2);
    }

    [Theory]
    [InlineData("scene.cine", "apagar")]        // una escena solo se activa
    [InlineData("lock.puerta", "encender")]     // las cerraduras no se tocan
    [InlineData("light.salon", "explotar")]
    public async Task Lo_que_no_se_permite_no_sale_de_la_zona_privada(string entidad, string accion)
    {
        var vistas = new List<(HttpMethod, string, string?, string?)>();
        var cliente = new ClienteHogar(new Fabrica(_ => new HttpResponseMessage(HttpStatusCode.OK), vistas), Config());
        Assert.False(await cliente.Llamar(entidad, accion, default));
        Assert.Empty(vistas);
    }

    [Theory]
    [InlineData(null, "token")] [InlineData("http://192.168.1.20:8123", null)] [InlineData("", "")]
    public void Sin_direccion_o_sin_token_no_esta_configurado(string? url, string? token) => Assert.False(new ClienteHogar(new Fabrica(_ => new HttpResponseMessage()), Config(url, token)).Configurado);

    [Fact]
    public async Task Una_respuesta_de_error_hace_fallar_la_lista()
    {
        var cliente = new ClienteHogar(new Fabrica(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)), Config());
        await Assert.ThrowsAsync<HttpRequestException>(() => cliente.Listar(default));
    }

    [Theory]
    [InlineData("http://192.168.1.20:8123", true)] [InlineData("http://10.0.0.5:8123/", true)] [InlineData("https://100.100.1.2:8123", true)]
    [InlineData("http://8.8.8.8:8123", false)] [InlineData("http://127.0.0.1:8123", false)] [InlineData("ftp://192.168.1.20", false)] [InlineData("192.168.1.20", false)] [InlineData("http://u:p@192.168.1.20", false)] [InlineData("", false)]
    public async Task La_direccion_tiene_que_ser_de_tu_red(string url, bool valida) => Assert.Equal(valida, await Hogar.ValidarUrl(url) is null);
}

public class HogarEndpointsTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Sin_configurar_la_pantalla_lo_sabe_y_las_rutas_avisan_en_vez_de_fallar()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/hogar/estado")).StatusCode);
        var c = await factory.Dentro();
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/hogar/estado")).GetProperty("homeAssistant").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync("/api/hogar/entidades")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/hogar/entidades/light.salon/alternar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/hogar/entidades/lock.puerta/encender", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/hogar/entidades/light.salon/explotar", null)).StatusCode);
    }

    [Fact]
    public async Task La_direccion_de_Home_Assistant_se_guarda_cifrada_pero_solo_si_es_de_tu_red()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/seguridad/claves/homeassistant/Url", new { valor = "http://8.8.8.8:8123" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/seguridad/claves/homeassistant/Url", new { valor = "no es una dirección" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/seguridad/claves/homeassistant/Url", new { valor = "http://192.168.1.20:8123" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/seguridad/claves/homeassistant/Token", new { valor = "un-token-largo" })).StatusCode);
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/hogar/estado")).GetProperty("homeAssistant").GetBoolean());   // ya está configurado, sin reiniciar
    }
}
