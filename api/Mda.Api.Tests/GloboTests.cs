using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mda.Api.Tests;

public class GloboTests(GaleriaFactory factory) : IClassFixture<GaleriaFactory>
{
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= Entrar();

    private async Task<HttpClient> Entrar()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).EnsureSuccessStatusCode();
        return c;
    }

    private async Task<string> Foto(HttpClient c)
    {
        var f = await (await c.PostAsJsonAsync("/api/galeria/fotos", new { ruta = "viajes/atardecer.jpg", titulo = "Sierra" })).Content.ReadFromJsonAsync<JsonElement>();
        await c.PutAsJsonAsync("/api/galeria/estado", new { activa = true });
        return f.GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> Poner(HttpClient c, string id, bool globo, double? lat = 40.4168, double? lon = -3.7038) =>
        c.PutAsJsonAsync($"/api/galeria/fotos/{id}", new { titulo = "Sierra", descripcion = "", alt = "", categoria = "naturaleza", publicada = true, enGlobo = globo, lat, lon, lugar = "Madrid" });

    private async Task<string> Publico() => await factory.CreateClient().GetStringAsync("/api/publico/galeria");

    [Fact]
    public async Task Al_publico_la_posicion_le_llega_redondeada_a_unos_10_km()
    {
        var c = await Dentro();
        var id = await Foto(c);
        await c.PutAsJsonAsync("/api/galeria/globo", new { lat = (double?)null, lon = (double?)null, radioKm = 0 });
        Assert.Equal(HttpStatusCode.OK, (await Poner(c, id, true)).StatusCode);
        var texto = await Publico();
        var lugar = JsonDocument.Parse(texto).RootElement.GetProperty("fotos")[0].GetProperty("lugar");
        Assert.Equal(40.4, lugar.GetProperty("lat").GetDouble());
        Assert.Equal(-3.7, lugar.GetProperty("lon").GetDouble());
        Assert.Equal("Madrid", lugar.GetProperty("nombre").GetString());
        Assert.DoesNotContain("40.4168", texto);
        Assert.DoesNotContain("3.7038", texto);
    }

    [Fact]
    public async Task Sin_marcar_en_el_globo_o_sin_posicion_no_sale_nada()
    {
        var c = await Dentro();
        var id = await Foto(c);
        await c.PutAsJsonAsync("/api/galeria/globo", new { lat = (double?)null, lon = (double?)null, radioKm = 0 });
        await Poner(c, id, false);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(await Publico()).RootElement.GetProperty("fotos")[0].GetProperty("lugar").ValueKind);
        await Poner(c, id, true, null, null);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(await Publico()).RootElement.GetProperty("fotos")[0].GetProperty("lugar").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await Poner(c, id, true, 120, 0)).StatusCode);
    }

    [Fact]
    public async Task Una_foto_dentro_de_la_zona_de_tu_casa_nunca_sale()
    {
        var c = await Dentro();
        var id = await Foto(c);
        // casa en Madrid, radio de 25 km: una foto hecha a 3 km sale en blanco; otra en Barcelona, no
        await c.PutAsJsonAsync("/api/galeria/globo", new { lat = 40.4168, lon = -3.7038, radioKm = 25 });
        await Poner(c, id, true, 40.43, -3.68);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(await Publico()).RootElement.GetProperty("fotos")[0].GetProperty("lugar").ValueKind);
        await Poner(c, id, true, 41.3874, 2.1686);
        Assert.Equal(41.4, JsonDocument.Parse(await Publico()).RootElement.GetProperty("fotos")[0].GetProperty("lugar").GetProperty("lat").GetDouble());
        await c.PutAsJsonAsync("/api/galeria/globo", new { lat = (double?)null, lon = (double?)null, radioKm = 0 });
    }

    [Fact]
    public async Task El_gps_del_archivo_solo_lo_ve_el_dueno_y_una_foto_sin_gps_da_204()
    {
        var c = await Dentro();
        var id = await Foto(c);
        Assert.Equal(HttpStatusCode.NoContent, (await c.GetAsync($"/api/galeria/fotos/{id}/gps")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/galeria/fotos/{id}/gps")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/galeria/globo")).StatusCode);
    }
}
