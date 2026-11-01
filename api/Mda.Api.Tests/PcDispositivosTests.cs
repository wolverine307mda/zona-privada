using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mda.Api.Tests;

// En su propia clase: tiene su propia base y sus propios límites de peticiones, y no mezcla órdenes con las de PcTests
public class PcDispositivosTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private HttpRequestMessage Agente(HttpMethod m, string ruta, object? cuerpo = null)
    {
        var r = new HttpRequestMessage(m, ruta);
        r.Headers.Add("X-Agente-Token", ApiFactory.Token);
        if (cuerpo is not null) r.Content = JsonContent.Create(cuerpo);
        return r;
    }

    private async Task AgenteConectado() =>
        await factory.CreateClient().SendAsync(Agente(HttpMethod.Post, "/api/sistema/agente/salud", new
        {
            hora = DateTimeOffset.UtcNow, reinicioPendiente = false,
            discos = new[] { new { nombre = "0", modelo = "SSD", salud = "Healthy", estado = "OK", temperaturaC = 41.0, desgastePct = 5, erroresLectura = 0 } },
            volumenes = new[] { new { nombre = "C:", libreBytes = 100L << 30, totalBytes = 500L << 30 } },
        }));

    [Fact]
    public async Task El_volumen_y_el_brillo_van_de_0_a_100_y_no_piden_confirmar_con_la_cara()
    {
        var c = await factory.Dentro(); // sesión sin «contraseña otra vez»
        await AgenteConectado();
        var vol = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "volumen", parametro = "35" });
        Assert.Equal(HttpStatusCode.OK, vol.StatusCode);
        var orden = await vol.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("35", orden.GetProperty("parametro").GetString());
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "brillo", parametro = "0" })).StatusCode);
        foreach (var malo in new[] { "101", "-1", "alto", "" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "brillo", parametro = malo })).StatusCode);
        // apagar sigue pidiendo confirmar
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "apagar" })).StatusCode);
        // y la firma que recibe el agente lleva el nivel
        var pendientes = await (await factory.CreateClient().SendAsync(Agente(HttpMethod.Get, "/api/sistema/agente/ordenes"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(pendientes.EnumerateArray(), o => o.GetProperty("tipo").GetString() == "volumen" && o.GetProperty("parametro").GetString() == "35");
    }

    [Fact]
    public async Task La_bateria_el_volumen_y_el_brillo_que_manda_el_agente_se_acotan_y_se_ven()
    {
        var c = await factory.Dentro();
        string? bateria = null;
        for (var i = 0; i < 20 && bateria != "57"; i++)
        {
            var envio = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente") { Content = JsonContent.Create(new
            {
                cpu = 10.0, memoriaUsada = 1L << 30, memoriaTotal = 8L << 30, discoUsado = 1L, discoTotal = 2L, encendidoSegundos = 5, nucleos = 4, equipo = "PORTATIL",
                sistemaOperativo = "Windows", hora = DateTimeOffset.UtcNow, origen = "equipo", bateria = 57, cargando = true, volumen = 250, brillo = 70,
            }) };
            envio.Headers.Add("X-Agente-Token", ApiFactory.Token);
            Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().SendAsync(envio)).StatusCode);
            await Task.Delay(300);
            var sis = await c.GetFromJsonAsync<JsonElement>("/api/sistema");
            if (sis.GetProperty("actual").ValueKind == JsonValueKind.Object && sis.GetProperty("actual").GetProperty("equipo").GetString() == "PORTATIL")
            {
                var a = sis.GetProperty("actual");
                bateria = a.GetProperty("bateria").GetInt32().ToString();
                Assert.True(a.GetProperty("cargando").GetBoolean());
                Assert.Equal(70, a.GetProperty("brillo").GetInt32());
                Assert.Equal(JsonValueKind.Null, a.GetProperty("volumen").ValueKind); // 250 no es un porcentaje: se descarta
            }
        }
        Assert.Equal("57", bateria);
    }
}
