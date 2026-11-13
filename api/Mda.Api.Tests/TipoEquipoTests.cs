using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mda.Api.Tests;

public class TipoEquipoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= Entrar();

    private async Task<HttpClient> Entrar()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).EnsureSuccessStatusCode();
        return c;
    }

    [Fact]
    public async Task Pide_sesion()
    {
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/equipo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync("/api/equipo", new { tipo = "minipc" })).StatusCode);
    }

    [Fact]
    public async Task Por_defecto_es_un_portatil_y_se_puede_cambiar_a_mini_pc_y_volver()
    {
        var c = await Dentro();
        Assert.Equal("portatil", (await c.GetFromJsonAsync<JsonElement>("/api/equipo")).GetProperty("tipo").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/equipo", new { tipo = "minipc" })).StatusCode);
        Assert.Equal("minipc", (await c.GetFromJsonAsync<JsonElement>("/api/equipo")).GetProperty("tipo").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/equipo", new { tipo = "portatil" })).StatusCode);
        Assert.Equal("portatil", (await c.GetFromJsonAsync<JsonElement>("/api/equipo")).GetProperty("tipo").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("servidor")]
    [InlineData("MINIPC")]
    public async Task Un_tipo_que_no_existe_se_rechaza_y_no_cambia_nada(string tipo)
    {
        var c = await Dentro();
        await c.PutAsJsonAsync("/api/equipo", new { tipo = "minipc" });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/equipo", new { tipo })).StatusCode);
        Assert.Equal("minipc", (await c.GetFromJsonAsync<JsonElement>("/api/equipo")).GetProperty("tipo").GetString());
        await c.PutAsJsonAsync("/api/equipo", new { tipo = "portatil" });
    }
}
