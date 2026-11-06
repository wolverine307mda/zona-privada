using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

// «Descargar en el PC»: una dirección web, siempre con Face ID, y el resultado final llega por la misma orden
public class DescargarOrdenTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private async Task<HttpClient> Confirmado(bool confirmar = true)
    {
        var c = await factory.Dentro();
        await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/salud")
        {
            Headers = { { "X-Agente-Token", ApiFactory.Token } },
            Content = JsonContent.Create(new { hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>() }),
        });
        if (confirmar) Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);
        return c;
    }

    [Theory]
    [InlineData("https://ejemplo.com/archivo.pdf", true)] [InlineData("http://ejemplo.com/a.zip?x=1", true)]
    [InlineData("calculadora", false)] [InlineData("javascript:alert(1)", false)] [InlineData("file:///C:/Windows/win.ini", false)] [InlineData("ftp://ejemplo.com/a", false)]
    [InlineData("https://ejemplo.com/a b", false)] [InlineData("https://u:p@ejemplo.com/a", false)] [InlineData("", false)] [InlineData(null, false)]
    public void Valida_la_direccion(string? url, bool valida) => Assert.Equal(valida, OrdenesPc.UrlDescargaValida(url));

    [Fact]
    public async Task Sin_confirmar_no_se_descarga_nada()
    {
        var c = await Confirmado(confirmar: false);
        var r = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "descargar", parametro = "https://ejemplo.com/a.pdf" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reautenticar").GetBoolean());
    }

    [Fact]
    public async Task Con_confirmar_la_orden_lleva_la_direccion_y_lo_raro_se_rechaza()
    {
        var c = await Confirmado();
        var o = await (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "descargar", parametro = "https://ejemplo.com/a.pdf" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("descargar", o.GetProperty("tipo").GetString());
        Assert.Equal("https://ejemplo.com/a.pdf", o.GetProperty("parametro").GetString());
        foreach (var malo in new[] { "calculadora", "javascript:alert(1)", "file:///C:/x", "" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "descargar", parametro = malo })).StatusCode);
    }

    [Fact]
    public async Task El_resultado_final_avisa_por_telegram_pero_el_inicial_no()
    {
        var c = await Confirmado();
        var id = (await (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "descargar", parametro = "https://ejemplo.com/b.pdf" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        using var agente = factory.CreateClient();
        async Task Informar(bool ok, string texto)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"/api/sistema/agente/ordenes/{id}") { Content = JsonContent.Create(new { ok, resultado = texto }) };
            peticion.Headers.Add("X-Agente-Token", ApiFactory.Token);
            Assert.Equal(HttpStatusCode.NoContent, (await agente.SendAsync(peticion)).StatusCode);
        }
        await Informar(true, "descargando ejemplo.com…");
        Assert.DoesNotContain(factory.Avisos.Enviados, m => m.Contains("b.pdf") && m.StartsWith("telegram:") && m.Contains("⬇️"));
        await Informar(true, "descargado: b.pdf (1,2 MB) en Descargas\\Zona privada");
        await Task.Delay(100);
        Assert.Contains(factory.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("descargado: b.pdf"));
        var orden = await c.GetFromJsonAsync<JsonElement>($"/api/pc/ordenes/{id}");
        Assert.Equal("hecha", orden.GetProperty("estado").GetString());
        Assert.Contains("b.pdf", orden.GetProperty("resultado").GetString());
    }
}
