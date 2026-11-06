using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

// La bandeja de entrada del PC: lo que el agente ve caer en su carpeta aparece en la lista
public class BandejaTests
{
    [Fact]
    public void Guarda_los_ultimos_50_el_mas_nuevo_primero()
    {
        var b = new BandejaPc(TimeProvider.System);
        for (var i = 1; i <= 60; i++) b.Añadir($"archivo{i}.pdf");
        var l = b.Lista();
        Assert.Equal(BandejaPc.Maximo, l.Count);
        Assert.Equal("archivo60.pdf", l[0].Texto);
        Assert.Equal("archivo11.pdf", l[^1].Texto);
    }

    [Fact]
    public void Se_puede_quitar_uno_y_vaciar()
    {
        var b = new BandejaPc(TimeProvider.System);
        var a = b.Añadir("a.txt"); b.Añadir("b.txt");
        Assert.True(b.Quitar(a.Id));
        Assert.False(b.Quitar(a.Id));
        Assert.Single(b.Lista());
        b.Vaciar();
        Assert.Empty(b.Lista());
    }

    [Fact]
    public async Task El_evento_del_agente_la_llena_aunque_lleguen_varios_seguidos_y_avisa_una_vez()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        using var agente = f.CreateClient();
        foreach (var n in new[] { "foto.jpg (2 MB)", "nota.txt (1 KB)", "plano.pdf (3 MB)" })
        {
            var p = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/eventos") { Content = JsonContent.Create(new { tipo = "bandeja", texto = n }) };
            p.Headers.Add("X-Agente-Token", ApiFactory.Token);
            Assert.Equal(HttpStatusCode.NoContent, (await agente.SendAsync(p)).StatusCode);
        }
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/pc/bandeja");
        Assert.Equal(3, lista.GetArrayLength());
        Assert.Equal("plano.pdf (3 MB)", lista[0].GetProperty("texto").GetString());
        Assert.Single(f.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("bandeja"));       // el límite de 20 s solo afecta a los avisos

        var id = lista[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/pc/bandeja/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/pc/bandeja/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/pc/bandeja")).StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/pc/bandeja")).GetArrayLength());
    }

    [Fact]
    public async Task Sin_token_del_agente_no_se_apunta_nada()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        using var anonimo = f.CreateClient();
        var r = await anonimo.PostAsJsonAsync("/api/sistema/agente/eventos", new { tipo = "bandeja", texto = "intruso.exe" });
        Assert.NotEqual(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/pc/bandeja")).GetArrayLength());
    }
}
