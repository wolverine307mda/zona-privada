using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SkiaSharp;

namespace Mda.Api.Tests;

// la API con una carpeta «imagenes» de verdad: una foto grande con un EXIF falso que lleva un «GPS» reconocible
public class GaleriaFactory : ApiFactory
{
    public string Base { get; } = Directory.CreateTempSubdirectory("mda-galeria-").FullName;
    public const string Secreto = "GPS-SECRETO-40.1234N";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        var fotos = Path.Combine(Base, "imagenes");
        Directory.CreateDirectory(Path.Combine(fotos, "viajes"));
        File.WriteAllBytes(Path.Combine(fotos, "viajes", "atardecer.jpg"), JpegConExif(3000, 2000));
        File.WriteAllText(Path.Combine(fotos, "notas.txt"), "no es una imagen");
        File.WriteAllText(Path.Combine(Base, "secreto.jpg"), "fuera de las carpetas");
        builder.UseSetting("Archivos:Base", Base);
        builder.UseSetting("Galeria:Derivados", Path.Combine(Base, "derivados"));
    }

    // un JPEG de verdad con un segmento APP1 «Exif» que contiene el texto del secreto
    private static byte[] JpegConExif(int ancho, int alto)
    {
        using var bmp = new SKBitmap(ancho, alto);
        using (var c = new SKCanvas(bmp)) { c.Clear(new SKColor(200, 80, 20)); }
        using var img = SKImage.FromBitmap(bmp);
        var jpeg = img.Encode(SKEncodedImageFormat.Jpeg, 85).ToArray();
        var carga = Encoding.ASCII.GetBytes("Exif\0\0" + Secreto);
        var len = carga.Length + 2;
        return [.. jpeg[..2], 0xFF, 0xE1, (byte)(len >> 8), (byte)(len & 0xFF), .. carga, .. jpeg[2..]];
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(Base, true); } catch { /* temporal */ }
    }
}

public class GaleriaTests(GaleriaFactory factory) : IClassFixture<GaleriaFactory>
{
    private const string Web = "https://wolverine307mda.github.io";
    private static Task<HttpClient>? _entrada;

    private Task<HttpClient> Dentro() => _entrada ??= Entrar();

    private async Task<HttpClient> Entrar()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).EnsureSuccessStatusCode();
        return c;
    }

    private HttpRequestMessage Publica(string ruta)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, ruta);
        r.Headers.Add("Origin", Web);
        return r;
    }

    [Fact]
    public async Task La_gestion_pide_sesion()
    {
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/galeria/fotos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/galeria/fotos", new { ruta = "viajes/atardecer.jpg" })).StatusCode);
    }

    [Fact]
    public async Task Sin_activar_la_galeria_la_web_publica_no_ve_nada()
    {
        // (este test no activa nada: el interruptor empieza apagado)
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().SendAsync(Publica("/api/publico/galeria"))).StatusCode);
    }

    [Theory]
    [InlineData("../secreto.jpg")]
    [InlineData("viajes/../../secreto.jpg")]
    [InlineData("notas.txt")]
    [InlineData("no-existe.jpg")]
    public async Task No_se_puede_publicar_lo_que_no_es_una_foto_de_imagenes(string ruta)
    {
        var c = await Dentro();
        var res = await c.PostAsJsonAsync("/api/galeria/fotos", new { ruta });
        Assert.True(res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"fue {res.StatusCode}");
    }

    [Fact]
    public async Task Una_foto_publicada_sale_recomprimida_sin_exif_y_con_cache_larga()
    {
        var c = await Dentro();
        var alta = await c.PostAsJsonAsync("/api/galeria/fotos", new { ruta = "viajes/atardecer.jpg", titulo = "Atardecer", descripcion = "Desde la sierra", alt = "Cielo naranja", categoria = "Naturaleza" });
        Assert.Equal(HttpStatusCode.OK, alta.StatusCode);
        var id = (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        Assert.DoesNotContain("atardecer", id); // el id no sale del nombre del archivo
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/galeria/estado", new { activa = true })).StatusCode);

        var anonimo = factory.CreateClient();
        var lista = await anonimo.SendAsync(Publica("/api/publico/galeria"));
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        Assert.Equal(Web, lista.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var texto = await lista.Content.ReadAsStringAsync();
        // nada de rutas, de nombres de archivo ni de carpetas del equipo
        Assert.DoesNotContain("atardecer.jpg", texto);
        Assert.DoesNotContain("viajes", texto);
        Assert.DoesNotContain(factory.Base, texto);
        var foto = JsonDocument.Parse(texto).RootElement.GetProperty("fotos")[0];
        Assert.Equal("Atardecer", foto.GetProperty("titulo").GetString());
        Assert.Equal(3000, foto.GetProperty("ancho").GetInt32());

        var mini = await anonimo.GetAsync(foto.GetProperty("mini").GetString());
        Assert.Equal(HttpStatusCode.OK, mini.StatusCode);
        Assert.Equal("image/webp", mini.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", mini.Headers.CacheControl!.ToString());
        Assert.False(mini.Headers.Contains("Cross-Origin-Resource-Policy")); // se puede usar desde el portfolio
        var bytes = await mini.Content.ReadAsByteArrayAsync();
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.DoesNotContain(GaleriaFactory.Secreto, Encoding.Latin1.GetString(bytes));
        using var decodificada = SKBitmap.Decode(bytes);
        Assert.Equal(480, Math.Max(decodificada.Width, decodificada.Height));

        var grande = await (await anonimo.GetAsync(foto.GetProperty("grande").GetString())).Content.ReadAsByteArrayAsync();
        using var g = SKBitmap.Decode(grande);
        Assert.Equal(1600, Math.Max(g.Width, g.Height));
        Assert.DoesNotContain(GaleriaFactory.Secreto, Encoding.Latin1.GetString(grande));
    }

    [Fact]
    public async Task Despublicar_o_apagar_el_interruptor_la_quita_de_la_web()
    {
        var c = await Dentro();
        var alta = await (await c.PostAsJsonAsync("/api/galeria/fotos", new { ruta = "viajes/atardecer.jpg" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = alta.GetProperty("id").GetString()!;
        await c.PutAsJsonAsync("/api/galeria/estado", new { activa = true });
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync($"/api/publico/galeria/{id}/mini")).StatusCode);

        await c.PutAsJsonAsync($"/api/galeria/fotos/{id}", new { titulo = "x", descripcion = "", alt = "", categoria = "", publicada = false });
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync($"/api/publico/galeria/{id}/mini")).StatusCode);
        var lista = await anonimo.GetFromJsonAsync<JsonElement>("/api/publico/galeria");
        Assert.Equal(0, lista.GetProperty("fotos").GetArrayLength());

        await c.PutAsJsonAsync($"/api/galeria/fotos/{id}", new { titulo = "x", descripcion = "", alt = "", categoria = "", publicada = true });
        await c.PutAsJsonAsync("/api/galeria/estado", new { activa = false });
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync($"/api/publico/galeria/{id}/mini")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync("/api/publico/galeria")).StatusCode);
    }

    [Fact]
    public async Task Un_tamano_inventado_o_un_id_que_no_existe_da_404()
    {
        var c = await Dentro();
        var alta = await (await c.PostAsJsonAsync("/api/galeria/fotos", new { ruta = "viajes/atardecer.jpg" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = alta.GetProperty("id").GetString()!;
        await c.PutAsJsonAsync("/api/galeria/estado", new { activa = true });
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync($"/api/publico/galeria/{id}/original")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync("/api/publico/galeria/ffffffffffff/mini")).StatusCode);
    }

    [Fact]
    public async Task El_navegador_de_carpetas_de_la_privada_solo_ve_imagenes()
    {
        var c = await Dentro();
        var raiz = await c.GetFromJsonAsync<JsonElement>("/api/galeria/carpeta");
        var nombres = raiz.GetProperty("elementos").EnumerateArray().Select(e => e.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("viajes", nombres);
        Assert.DoesNotContain("secreto.jpg", nombres);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/galeria/carpeta?ruta=..")).StatusCode);
    }
}
