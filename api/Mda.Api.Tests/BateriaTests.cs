using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

// la salud de la batería: el agente manda tres números, la API guarda uno por día y la pantalla enseña el desgaste
public class BateriaTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private HttpRequestMessage Agente(object cuerpo, string? token = ApiFactory.Token)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/bateria") { Content = JsonContent.Create(cuerpo) };
        if (token is not null) r.Headers.Add("X-Agente-Token", token);
        return r;
    }

    [Theory]
    [InlineData(56000, 47600, 15.0)]
    [InlineData(50000, 50000, 0.0)]
    [InlineData(50000, 52000, 0.0)]      // nunca negativo
    [InlineData(0, 100, 0.0)]
    public void El_desgaste_sale_de_la_capacidad_de_diseno_y_la_actual(long diseno, long completa, double esperado) =>
        Assert.Equal(esperado, BateriaEndpoints.Desgaste(diseno, completa));

    [Fact]
    public async Task El_agente_manda_la_medida_con_su_token_y_la_pantalla_la_lee_con_sesion()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Agente(new { diseno = 56000, completa = 47600, ciclos = 212 }, token: "otro-token-que-no-vale-0123456789abc"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Agente(new { diseno = 56000, completa = 47600 }, token: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/pc/bateria")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await anon.SendAsync(Agente(new { diseno = 56000, completa = 47600, ciclos = 212 }))).StatusCode);
        var c = await factory.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/pc/bateria");
        var actual = j.GetProperty("actual");
        Assert.Equal(15.0, actual.GetProperty("desgastePct").GetDouble());
        Assert.Equal(212, actual.GetProperty("ciclos").GetInt32());
        Assert.Equal(56000, actual.GetProperty("diseno").GetInt64());
    }

    [Fact]
    public async Task Una_segunda_medida_del_mismo_dia_sustituye_a_la_primera()
    {
        var anon = factory.CreateClient();
        await anon.SendAsync(Agente(new { diseno = 60000, completa = 54000, ciclos = 100 }));
        await anon.SendAsync(Agente(new { diseno = 60000, completa = 51000, ciclos = 101 }));
        var c = await factory.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/pc/bateria");
        var hoy = j.GetProperty("historial").EnumerateArray().Last();
        Assert.Equal(51000, hoy.GetProperty("completa").GetInt64());
        Assert.Equal(1, j.GetProperty("historial").EnumerateArray().Count(x => x.GetProperty("fecha").GetString() == hoy.GetProperty("fecha").GetString()));
    }

    [Theory]
    [InlineData(0, 100)] [InlineData(500, 400)] [InlineData(50000, 0)] [InlineData(50000, 200000)] [InlineData(5000000, 100)]
    public async Task Capacidades_que_no_cuadran_se_rechazan(long diseno, long completa) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().SendAsync(Agente(new { diseno, completa }))).StatusCode);

    [Fact]
    public async Task Pedir_el_informe_es_una_orden_inocua_sin_face_id()
    {
        var c = await factory.Dentro();
        await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/salud")
        {
            Headers = { { "X-Agente-Token", ApiFactory.Token } },
            Content = JsonContent.Create(new { hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>() }),
        });
        var r = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "bateria-informe" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("medir el desgaste de la batería", OrdenesPc.Texto(new OrdenPc { Tipo = "bateria-informe" }));
    }
}
