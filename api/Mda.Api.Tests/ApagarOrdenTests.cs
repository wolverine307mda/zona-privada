using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

// apagar con temporizador: sigue pidiendo Face ID y solo acepta de 1 a 720 minutos
public class ApagarOrdenTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private async Task AgenteConectado() =>
        await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/salud")
        {
            Headers = { { "X-Agente-Token", ApiFactory.Token } },
            Content = JsonContent.Create(new { hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>() }),
        });

    private async Task<HttpClient> Confirmado()
    {
        var c = await factory.Dentro();
        await AgenteConectado();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);
        return c;
    }

    [Fact]
    public async Task Sin_confirmar_no_se_apaga_ni_con_temporizador()
    {
        var c = await factory.Dentro();
        await AgenteConectado();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "apagar", parametro = "30" })).StatusCode);
    }

    [Fact]
    public async Task Con_minutos_la_orden_los_lleva_y_sin_ellos_son_los_30_segundos_de_siempre()
    {
        var c = await Confirmado();
        var con = await (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "apagar", parametro = "45" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("45", con.GetProperty("parametro").GetString());
        var sin = await (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "apagar" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("", sin.GetProperty("parametro").GetString());
    }

    [Theory]
    [InlineData("0")] [InlineData("721")] [InlineData("abc")] [InlineData("-3")]
    public async Task Un_tiempo_fuera_de_rango_se_rechaza(string minutos)
    {
        var c = await Confirmado();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "apagar", parametro = minutos })).StatusCode);
    }

    [Fact]
    public void El_texto_del_aviso_dice_cuanto_falta()
    {
        Assert.Equal("apagar en 45 min", OrdenesPc.Texto(new OrdenPc { Tipo = "apagar", Parametro = "45" }));
        Assert.Equal("apagar (en 30 s)", OrdenesPc.Texto(new OrdenPc { Tipo = "apagar" }));
    }
}
