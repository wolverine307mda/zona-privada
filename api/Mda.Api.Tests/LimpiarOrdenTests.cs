using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mda.Api.Tests;

// «Limpiar temporales» borra cosas: pide Face ID y no lleva ninguna ruta
public class LimpiarOrdenTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private async Task AgenteConectado() =>
        await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/salud")
        {
            Headers = { { "X-Agente-Token", ApiFactory.Token } },
            Content = JsonContent.Create(new { hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>() }),
        });

    [Fact]
    public async Task Limpiar_pide_confirmar_y_despues_llega_al_agente_sin_parametro()
    {
        var c = await factory.Dentro();
        await AgenteConectado();
        var sin = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "limpiar" });
        Assert.Equal(HttpStatusCode.Forbidden, sin.StatusCode);
        Assert.True((await sin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reautenticar").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);
        // aunque se intente colar una ruta, la orden no la lleva
        var r = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "limpiar", parametro = @"C:\Windows" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var o = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("limpiar", o.GetProperty("tipo").GetString());
        Assert.Equal("", o.GetProperty("parametro").GetString());
    }
}
