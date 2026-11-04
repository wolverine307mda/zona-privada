using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

// «Abrir en el PC»: una web o una app de la lista, siempre con Face ID
public class AbrirOrdenTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
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
    [InlineData("https://ejemplo.com/pagina?x=1", true)] [InlineData("http://192.168.1.5:8080", true)] [InlineData("calculadora", true)] [InlineData("Bloc", true)]
    [InlineData("javascript:alert(1)", false)] [InlineData("file:///C:/Windows/win.ini", false)] [InlineData("cmd.exe", false)] [InlineData("https://ejemplo.com/a b", false)]
    [InlineData("https://u:p@ejemplo.com", false)] [InlineData("", false)] [InlineData(null, false)]
    public void Valida_el_destino(string? destino, bool valido) => Assert.Equal(valido, OrdenesPc.DestinoAbrirValido(destino));

    [Fact]
    public void Hasta_300_letras_y_ni_una_mas()
    {
        Assert.True(OrdenesPc.DestinoAbrirValido("https://e.com/" + new string('a', OrdenesPc.MaxAbrir - 14)));
        Assert.False(OrdenesPc.DestinoAbrirValido("https://e.com/" + new string('a', OrdenesPc.MaxAbrir)));
    }

    [Fact]
    public async Task Sin_confirmar_no_se_abre_nada()
    {
        var c = await Confirmado(confirmar: false);
        var r = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "abrir", parametro = "https://ejemplo.com" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reautenticar").GetBoolean());
    }

    [Fact]
    public async Task Con_confirmar_la_orden_lleva_la_direccion_entera_y_lo_raro_se_rechaza()
    {
        var c = await Confirmado();
        var url = "https://ejemplo.com/un/camino/largo?busqueda=" + new string('x', 150);
        var o = await (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "abrir", parametro = url })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("abrir", o.GetProperty("tipo").GetString());
        Assert.Equal(url, o.GetProperty("parametro").GetString());
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "abrir", parametro = "calculadora" })).StatusCode);
        foreach (var malo in new[] { "javascript:alert(1)", "file:///C:/Windows/win.ini", "cmd.exe /c calc", "" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "abrir", parametro = malo })).StatusCode);
    }
}
