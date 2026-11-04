using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

public class VelocidadApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private HttpRequestMessage Agente(object cuerpo, string? token = ApiFactory.Token)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/velocidad") { Content = JsonContent.Create(cuerpo) };
        if (token is not null) r.Headers.Add("X-Agente-Token", token);
        return r;
    }

    [Fact]
    public void Solo_mide_de_madrugada_y_una_vez_al_dia()
    {
        var madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        var noche = new DateTimeOffset(2026, 11, 4, 3, 30, 0, TimeSpan.FromHours(1));
        Assert.True(VelocidadNocturna.EsHoraDeMedir(noche, madrid, null));                                    // nunca se ha medido
        Assert.True(VelocidadNocturna.EsHoraDeMedir(noche, madrid, noche.AddHours(-24)));                       // la última fue ayer
        Assert.False(VelocidadNocturna.EsHoraDeMedir(noche, madrid, noche.AddHours(-5)));                       // hace poco
        Assert.False(VelocidadNocturna.EsHoraDeMedir(noche.AddHours(8), madrid, null));                         // de día no
        Assert.False(VelocidadNocturna.EsHoraDeMedir(noche.AddHours(-4), madrid, null));                        // a las 23:30 tampoco
    }

    [Fact]
    public async Task El_agente_manda_la_medida_con_su_token_y_el_historial_la_enseña()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Agente(new { bajadaMbps = 200.4, subidaMbps = 40.1, pingMs = 12 }, token: "otro-token-que-no-vale-0123456789abc"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/red/velocidad")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anon.SendAsync(Agente(new { bajadaMbps = 200.44, subidaMbps = 40.1, pingMs = 12 }))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anon.SendAsync(Agente(new { bajadaMbps = 100.0, subidaMbps = 20.0 }))).StatusCode);

        var c = await factory.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/red/velocidad");
        Assert.Equal(100.0, j.GetProperty("actual").GetProperty("bajadaMbps").GetDouble());
        Assert.Equal(2, j.GetProperty("historial").GetArrayLength());
        Assert.Equal(150.2, j.GetProperty("resumen").GetProperty("bajadaMedia").GetDouble());                  // (200,4 + 100) / 2
        Assert.Equal(100.0, j.GetProperty("resumen").GetProperty("bajadaMinima").GetDouble());
    }

    [Theory]
    [InlineData(-1, 10)] [InlineData(10, -5)] [InlineData(999999, 10)] [InlineData(10, 999999)]
    public async Task Velocidades_imposibles_se_rechazan(double bajada, double subida) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().SendAsync(Agente(new { bajadaMbps = bajada, subidaMbps = subida }))).StatusCode);

    [Fact]
    public async Task Pedir_el_test_es_una_orden_inocua_sin_face_id()
    {
        var c = await factory.Dentro();
        await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/salud")
        {
            Headers = { { "X-Agente-Token", ApiFactory.Token } },
            Content = JsonContent.Create(new { hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>() }),
        });
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "velocidad" })).StatusCode);
        Assert.Equal("medir la velocidad de Internet", OrdenesPc.Texto(new OrdenPc { Tipo = "velocidad" }));
    }
}
