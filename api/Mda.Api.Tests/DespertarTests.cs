using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

// Wake-on-LAN: la MAC se guarda normalizada y despertar pide el agente en marcha y Face ID
public class DespertarTests(RedFactory factory) : IClassFixture<RedFactory>
{
    [Theory]
    [InlineData("aa-bb-cc-00-11-22", "AA:BB:CC:00:11:22")]
    [InlineData(" aa:bb:cc:00:11:22 ", "AA:BB:CC:00:11:22")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void La_mac_se_normaliza(string? entrada, string esperado) => Assert.Equal(esperado, RedCasa.NormalizarMac(entrada));

    [Theory]
    [InlineData("AA:BB:CC:00:11")] [InlineData("AA:BB:CC:00:11:ZZ")] [InlineData("no es una mac")]
    public void Una_mac_mal_escrita_es_null(string entrada) => Assert.Null(RedCasa.NormalizarMac(entrada));

    private async Task AgenteConectado() =>
        await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/salud")
        {
            Headers = { { "X-Agente-Token", ApiFactory.Token } },
            Content = JsonContent.Create(new { hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>() }),
        });

    private static async Task<int> Crear(HttpClient c, string? mac)
    {
        var r = await c.PostAsJsonAsync("/api/red", new { nombre = "NAS del salón", host = "192.168.1.50", puerto = 445, icono = "nas", mac });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Una_mac_mala_se_rechaza_y_la_buena_se_guarda_en_mayusculas()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/red", new { nombre = "NAS", host = "192.168.1.50", puerto = 445, mac = "123" })).StatusCode);
        var id = await Crear(c, "aa-bb-cc-00-11-22");
        var d = (await c.GetFromJsonAsync<JsonElement>("/api/red")).EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal("AA:BB:CC:00:11:22", d.GetProperty("mac").GetString());
    }

    [Fact]
    public async Task Despertar_pide_mac_agente_y_confirmacion_y_manda_la_orden_con_la_mac()
    {
        var c = await factory.Dentro();
        var sinMac = await Crear(c, null);
        var conMac = await Crear(c, "AA:BB:CC:00:11:22");

        await AgenteConectado();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/api/red/{sinMac}/despertar", null)).StatusCode);     // sin MAC no hay nada que despertar
        var sin = await c.PostAsync($"/api/red/{conMac}/despertar", null);                                                 // sin confirmar con Face ID
        Assert.Equal(HttpStatusCode.Forbidden, sin.StatusCode);
        Assert.True((await sin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reautenticar").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);
        var r = await c.PostAsync($"/api/red/{conMac}/despertar", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var orden = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("wol", orden.GetProperty("tipo").GetString());
        Assert.Equal("AA:BB:CC:00:11:22", orden.GetProperty("parametro").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/red/9999/despertar", null)).StatusCode);
    }

    [Fact]
    public async Task Por_la_ruta_general_de_ordenes_no_se_puede_pedir_wol()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "wol", parametro = "AA:BB:CC:00:11:22" })).StatusCode);
    }
}
