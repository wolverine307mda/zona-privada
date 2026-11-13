using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mda.Api.Tests;

public class TemaWebTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Web = "https://wolvem.dev";
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= Entrar();

    private async Task<HttpClient> Entrar()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).EnsureSuccessStatusCode();
        return c;
    }

    private static HttpRequestMessage Publica()
    {
        var r = new HttpRequestMessage(HttpMethod.Get, "/api/publico/tema");
        r.Headers.Add("Origin", Web);
        return r;
    }

    [Fact]
    public async Task Gestionar_el_tema_pide_sesion()
    {
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/tema-web")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync("/api/tema-web", new { modo = "dark", color = "rosa" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.DeleteAsync("/api/tema-web")).StatusCode);
    }

    [Fact]
    public async Task Fijar_el_tema_lo_publica_y_quitarlo_lo_retira()
    {
        var c = await Dentro();
        var anonimo = factory.CreateClient();

        // sin fijar: la web no recibe nada y cada navegador usa el suyo
        await c.DeleteAsync("/api/tema-web");
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.SendAsync(Publica())).StatusCode);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/tema-web")).GetProperty("configurado").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/tema-web", new { modo = "light", color = "cobalto" })).StatusCode);
        var res = await anonimo.SendAsync(Publica());
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(Web, res.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var j = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("light", j.GetProperty("modo").GetString());
        Assert.Equal("cobalto", j.GetProperty("color").GetString());
        Assert.Equal(2, j.EnumerateObject().Count());                    // nada más que modo y color
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/tema-web")).GetProperty("configurado").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/tema-web")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.SendAsync(Publica())).StatusCode);
    }

    [Theory]
    [InlineData("morado", "naranja")]
    [InlineData("dark", "<script>")]
    [InlineData("", "")]
    public async Task Solo_se_aceptan_valores_de_la_lista(string modo, string color)
    {
        var c = await Dentro();
        await c.PutAsJsonAsync("/api/tema-web", new { modo = "dark", color = "lima" });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/tema-web", new { modo, color })).StatusCode);
        // lo anterior sigue como estaba
        var j = JsonDocument.Parse(await (await factory.CreateClient().SendAsync(Publica())).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("lima", j.GetProperty("color").GetString());
    }
}
