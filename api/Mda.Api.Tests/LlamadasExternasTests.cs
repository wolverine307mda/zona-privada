using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Seguridad;

namespace Mda.Api.Tests;

public class LlamadasExternasUnitTests
{
    [Theory]
    [InlineData("{\"importe\":5,\"password\":\"abc123\"}", "abc123")]
    [InlineData("{\"Token\": \"mda_secreto\",\"x\":1}", "mda_secreto")]
    public void Los_secretos_no_se_guardan(string cuerpo, string secreto)
    {
        var limpio = LlamadasExternas.Limpiar(cuerpo);
        Assert.DoesNotContain(secreto, limpio);
        Assert.Contains("***", limpio);
    }

    [Fact]
    public void El_mensaje_de_error_sale_del_JSON_o_del_estado()
    {
        Assert.Equal("", LlamadasExternas.MensajeDeError(200, "{}"));
        Assert.Equal("No hay ese aparato", LlamadasExternas.MensajeDeError(404, "{\"detalle\":\"No hay ese aparato\"}"));
        Assert.Contains("token", LlamadasExternas.MensajeDeError(401, ""));
    }

    [Theory]
    [InlineData("/api/sistema/agente/salud", 200, true)]
    [InlineData("/api/sistema/agente/salud", 401, false)]
    [InlineData("/api/atajos/nota", 200, false)]
    public void Las_llamadas_buenas_del_agente_son_ruido(string ruta, int estado, bool ruido) => Assert.Equal(ruido, LlamadasExternas.EsRuido(ruta, estado));
}

public class LlamadasExternasTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= factory.Dentro();

    private async Task<HttpClient> ConToken(params string[] alcances)
    {
        var c = await Dentro();
        var r = await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "iPhone de prueba", alcances });
        var token = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var otro = factory.CreateClient();
        otro.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return otro;
    }

    [Fact]
    public async Task Una_llamada_con_token_se_guarda_con_su_estado_su_respuesta_y_su_error()
    {
        var app = await Dentro();
        var antes = (await app.GetFromJsonAsync<JsonElement>("/api/seguridad/llamadas/vivo")).GetProperty("ultimo").GetInt64();

        var iphone = await ConToken("pc");
        var r = await iphone.PostAsJsonAsync("/api/atajos/despertar", new { nombre = "no existe", clave = "secretisima" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);

        var lista = await app.GetFromJsonAsync<JsonElement>("/api/seguridad/llamadas");
        var l = lista.GetProperty("llamadas").EnumerateArray().First(x => x.GetProperty("ruta").GetString() == "/api/atajos/despertar");
        Assert.Equal(404, l.GetProperty("estado").GetInt32());
        Assert.Equal("Atajo · iPhone de prueba", l.GetProperty("origen").GetString());
        Assert.False(string.IsNullOrEmpty(l.GetProperty("error").GetString()));

        var detalle = await app.GetFromJsonAsync<JsonElement>($"/api/seguridad/llamadas/{l.GetProperty("id").GetInt32()}");
        Assert.Contains("no existe", detalle.GetProperty("peticion").GetString());
        Assert.DoesNotContain("secretisima", detalle.GetProperty("peticion").GetString());
        Assert.NotEmpty(detalle.GetProperty("respuesta").GetString()!);

        var vivo = await app.GetFromJsonAsync<JsonElement>($"/api/seguridad/llamadas/vivo?desde={antes}");
        Assert.Contains(vivo.GetProperty("llamadas").EnumerateArray(), x => x.GetProperty("ruta").GetString() == "/api/atajos/despertar");
    }

    [Fact]
    public async Task Lo_que_hace_la_app_con_su_cookie_no_cuenta_como_llamada_externa()
    {
        var app = await Dentro();
        var antes = (await app.GetFromJsonAsync<JsonElement>("/api/seguridad/llamadas/vivo")).GetProperty("ultimo").GetInt64();
        await app.GetAsync("/api/atajos-tokens");
        Assert.Equal(antes, (await app.GetFromJsonAsync<JsonElement>("/api/seguridad/llamadas/vivo")).GetProperty("ultimo").GetInt64());
    }

    [Fact]
    public async Task Sin_sesion_no_se_pueden_ver_las_llamadas()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/seguridad/llamadas")).StatusCode);
    }
}
