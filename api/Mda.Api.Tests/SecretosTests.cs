using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class SecretosTests(ModulosFactory f) : IClassFixture<ModulosFactory>
{
    private static async Task Reautenticar(HttpClient c) =>
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);

    [Fact]
    public async Task Sin_sesion_no_hay_nada()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/seguridad/claves")).StatusCode);
    }

    [Fact]
    public async Task La_lista_no_lleva_valores_y_ver_cambiar_o_quitar_piden_confirmar()
    {
        var c = await f.Dentro();
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/claves");
        Assert.False(lista.GetProperty("reautenticado").GetBoolean());
        Assert.Contains(lista.GetProperty("servicios").EnumerateArray(), s => s.GetProperty("id").GetString() == "telegram");
        Assert.DoesNotContain("valor", lista.GetRawText());

        foreach (var r in new[]
                 {
                     await c.GetAsync("/api/seguridad/claves/telegram/Token"),
                     await c.PutAsJsonAsync("/api/seguridad/claves/telegram/Token", new { valor = "x" }),
                     await c.DeleteAsync("/api/seguridad/claves/telegram/Token"),
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reautenticar").GetBoolean());
        }
    }

    [Fact]
    public async Task Una_clave_guardada_gana_al_env_se_ve_con_confirmacion_y_al_quitarla_queda_vacia()
    {
        var c = await f.Dentro();
        await Reautenticar(c);
        var config = f.Services.GetRequiredService<IConfiguration>();

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/seguridad/claves/telegram/Token", new { valor = "  123:token-nuevo  " })).StatusCode);
        Assert.Equal("123:token-nuevo", config["Telegram:Token"]); // el resto del código lee la configuración y ya lo ve
        Assert.Equal("123:token-nuevo", (await c.GetFromJsonAsync<JsonElement>("/api/seguridad/claves/telegram/Token")).GetProperty("valor").GetString());

        var lista = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/claves");
        var campo = lista.GetProperty("servicios").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "telegram").GetProperty("campos").EnumerateArray().Single();
        Assert.True(campo.GetProperty("configurada").GetBoolean());
        Assert.DoesNotContain("token-nuevo", lista.GetRawText());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/seguridad/claves/telegram/Token")).StatusCode);
        Assert.Equal("", config["Telegram:Token"]);
    }

    [Fact]
    public async Task Se_rechaza_lo_que_no_existe_y_lo_que_no_vale()
    {
        var c = await f.Dentro();
        await Reautenticar(c);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/seguridad/claves/inventado/Token", new { valor = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/seguridad/claves/telegram/Otro")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/seguridad/claves/correo/Puerto", new { valor = "abc" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/seguridad/claves/vigilancia/Url", new { valor = "http://inseguro.test/ping" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/seguridad/claves/correo/Puerto", new { valor = "587" })).StatusCode);
    }
}
