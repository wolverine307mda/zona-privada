using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

// los planes de energía: una orden sin Face ID, con los cuatro planes y nada más
public class EnergiaOrdenTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private async Task AgenteConectado() =>
        await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/salud")
        {
            Headers = { { "X-Agente-Token", ApiFactory.Token } },
            Content = JsonContent.Create(new { hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>() }),
        });

    [Fact]
    public void Los_cuatro_planes_y_su_texto() =>
        Assert.Equal(["equilibrado", "rendimiento", "ahorro", "cine"], OrdenesPc.Perfiles);

    [Theory]
    [InlineData("equilibrado")] [InlineData("rendimiento")] [InlineData("ahorro")] [InlineData("cine")]
    public async Task Cambiar_de_plan_no_pide_face_id_y_llega_al_agente(string plan)
    {
        var c = await factory.Dentro();
        await AgenteConectado();
        var r = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "energia", parametro = plan });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var o = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("energia", o.GetProperty("tipo").GetString());
        Assert.Equal(plan, o.GetProperty("parametro").GetString());
    }

    [Theory]
    [InlineData("turbo")] [InlineData("")] [InlineData("381b4222-f694-41f0-9685-ff5bb260df2e")]
    public async Task Un_plan_inventado_se_rechaza(string plan)
    {
        var c = await factory.Dentro();
        await AgenteConectado();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "energia", parametro = plan })).StatusCode);
    }
}
