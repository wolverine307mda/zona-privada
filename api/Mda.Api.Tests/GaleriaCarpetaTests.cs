using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SkiaSharp;

namespace Mda.Api.Tests;

// las miniaturas de «tu carpeta de imágenes»: pequeñas, guardadas para siempre y solo con sesión
public class GaleriaCarpetaTests(GaleriaFactory factory) : IClassFixture<GaleriaFactory>
{
    private async Task<HttpClient> Dentro()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).EnsureSuccessStatusCode();
        return c;
    }

    [Fact]
    public async Task La_miniatura_es_un_webp_pequeno_de_360_px_y_se_guarda_un_año_con_la_version()
    {
        var c = await Dentro();
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/galeria/carpeta?ruta=viajes");
        var foto = lista.GetProperty("elementos").EnumerateArray().Single(e => e.GetProperty("nombre").GetString() == "atardecer.jpg");
        var v = foto.GetProperty("v").GetString();
        Assert.False(string.IsNullOrEmpty(v));

        var r = await c.GetAsync($"/api/galeria/carpeta/mini?ruta={Uri.EscapeDataString("viajes/atardecer.jpg")}&v={v}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/webp", r.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", r.Headers.CacheControl?.ToString());
        var bytes = await r.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length < 40_000, $"la miniatura pesa {bytes.Length} bytes");
        using var img = SKBitmap.Decode(bytes);
        Assert.Equal(360, img.Width);   // la foto original es de 3000 × 2000: sale de 360 × 240
        Assert.Equal(240, img.Height);
    }

    [Fact]
    public async Task Solo_con_sesion_y_solo_de_imagenes_de_la_carpeta()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/galeria/carpeta/mini?ruta=viajes/atardecer.jpg")).StatusCode);
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/galeria/carpeta/mini?ruta=notas.txt")).StatusCode);              // no es una imagen
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/galeria/carpeta/mini?ruta=..%2Fsecreto.jpg")).StatusCode);          // fuera de la carpeta
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/galeria/carpeta/mini?ruta=viajes/no-existe.jpg")).StatusCode);
    }
}
