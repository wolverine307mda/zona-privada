using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;
using Microsoft.Extensions.Configuration;

namespace Mda.Api.Tests;

public class AdGuardClienteTests
{
    private sealed class Fabrica(Func<HttpRequestMessage, HttpResponseMessage> responder, List<(HttpMethod, string, string?, string?)> vistas) : IHttpClientFactory
    {
        private sealed class Manejador(Func<HttpRequestMessage, HttpResponseMessage> f, List<(HttpMethod, string, string?, string?)> vistas) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                vistas.Add((r.Method, r.RequestUri!.ToString(), r.Headers.Authorization?.ToString(), r.Content is null ? null : await r.Content.ReadAsStringAsync(ct)));
                return f(r);
            }
        }
        public HttpClient CreateClient(string name) => new(new Manejador(responder, vistas));
    }

    private static IConfiguration Config(string? url = "http://adguard:3000/", string? usuario = "mario", string? clave = "clave-larga") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AdGuard:Url"] = url, ["AdGuard:Usuario"] = usuario, ["AdGuard:Clave"] = clave }).Build();

    private const string Estadisticas = """
        {"num_dns_queries": 2000, "num_blocked_filtering": 460, "avg_processing_time": 0.0123,
         "top_queried_domains": [{"apple.com": 300}, {"google.com": 250}],
         "top_blocked_domains": [{"ads.tracker.net": 90}, {"metrics.example.com": 40}]}
        """;

    [Fact]
    public void Calcula_el_porcentaje_bloqueado_la_latencia_y_los_dominios_de_arriba()
    {
        var r = ClienteAdGuard.Parsear(JsonDocument.Parse(Estadisticas).RootElement, JsonDocument.Parse("{\"protection_enabled\": true}").RootElement);
        Assert.Equal(2000, r.Consultas); Assert.Equal(460, r.Bloqueadas);
        Assert.Equal(23.0, r.PorcentajeBloqueado);
        Assert.Equal(12, r.MsMedio);                                     // 0,0123 s
        Assert.True(r.Protegido);
        Assert.Equal([new DominioAdGuard("ads.tracker.net", 90), new DominioAdGuard("metrics.example.com", 40)], r.TopBloqueados);
        Assert.Equal("apple.com", r.TopConsultados[0].Dominio);
    }

    [Fact]
    public void Sin_datos_no_divide_entre_cero_y_la_proteccion_apagada_se_nota()
    {
        var r = ClienteAdGuard.Parsear(JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{\"protection_enabled\": false}").RootElement);
        Assert.Equal(0, r.PorcentajeBloqueado); Assert.Null(r.MsMedio); Assert.False(r.Protegido); Assert.Empty(r.TopBloqueados);
        Assert.False(ClienteAdGuard.Parsear(JsonDocument.Parse("[]").RootElement, JsonDocument.Parse("null").RootElement).Protegido);
    }

    [Fact]
    public async Task Pide_las_estadisticas_y_el_estado_con_usuario_y_contrasena_y_pausa_con_la_duracion_en_milisegundos()
    {
        var vistas = new List<(HttpMethod, string, string?, string?)>();
        var cliente = new ClienteAdGuard(new Fabrica(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(r.RequestUri!.AbsolutePath.EndsWith("stats") ? Estadisticas : "{\"protection_enabled\": true}") }, vistas), Config());
        Assert.True(cliente.Configurado);
        var resumen = await cliente.Resumen(default);
        Assert.Equal(23.0, resumen.PorcentajeBloqueado);
        Assert.Equal("http://adguard:3000/control/stats", vistas[0].Item2);
        Assert.Equal("Basic " + Convert.ToBase64String("mario:clave-larga"u8.ToArray()), vistas[0].Item3);

        Assert.True(await cliente.Proteccion(false, 10, default));
        Assert.Equal("{\"enabled\":false,\"duration\":600000}", vistas[^1].Item4);
        Assert.True(await cliente.Proteccion(true, null, default));
        Assert.Equal("{\"enabled\":true}", vistas[^1].Item4);
    }

    [Theory]
    [InlineData(null, "u", "c")] [InlineData("http://adguard:3000", null, "c")] [InlineData("http://adguard:3000", "u", null)] [InlineData("", "", "")]
    public void Sin_direccion_usuario_o_clave_no_esta_configurado(string? url, string? usuario, string? clave) =>
        Assert.False(new ClienteAdGuard(new Fabrica(_ => new HttpResponseMessage(), []), Config(url, usuario, clave)).Configurado);

    [Fact]
    public async Task Si_AdGuard_responde_con_error_falla()
    {
        var cliente = new ClienteAdGuard(new Fabrica(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized), []), Config());
        await Assert.ThrowsAsync<HttpRequestException>(() => cliente.Resumen(default));
    }
}

public class AdGuardEndpointsTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Sin_configurar_avisa_y_valida_los_minutos()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/hogar/adguard")).StatusCode);
        var c = await factory.Dentro();
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/hogar/estado")).GetProperty("adguard").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync("/api/hogar/adguard")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/hogar/adguard/proteccion", new { activa = false, minutos = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/hogar/adguard/proteccion", new { activa = false, minutos = 5000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/hogar/adguard/proteccion", new { activa = false, minutos = 10 })).StatusCode);
    }

    [Fact]
    public async Task Las_claves_de_AdGuard_se_guardan_con_su_direccion_solo_de_tu_red()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/seguridad/claves/adguard/Url", new { valor = "http://1.1.1.1:3000" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/seguridad/claves/adguard/Url", new { valor = "http://192.168.1.30:3000" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/seguridad/claves/adguard/Usuario", new { valor = "mario" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/seguridad/claves/adguard/Clave", new { valor = "una-clave-larga" })).StatusCode);
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/hogar/estado")).GetProperty("adguard").GetBoolean());
    }
}
