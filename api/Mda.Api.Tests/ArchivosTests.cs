using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Archivos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mda.Api.Tests;

// la API con «Documentos» apuntando a una carpeta temporal con documentos/ y musica/
public class ArchivosFactory : ApiFactory
{
    public string Base { get; } = Directory.CreateTempSubdirectory("mda-archivos-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        Directory.CreateDirectory(Path.Combine(Base, "documentos", "Facturas"));
        Directory.CreateDirectory(Path.Combine(Base, "musica"));
        File.WriteAllText(Path.Combine(Base, "documentos", "nota.txt"), "hola");
        File.WriteAllText(Path.Combine(Base, "documentos", "pagina.html"), "<script>alert(1)</script>");
        File.WriteAllText(Path.Combine(Base, "secreto.txt"), "fuera de las carpetas");
        builder.UseSetting("Archivos:Base", Base);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(Base, true); } catch { /* temporal */ }
    }
}

public class CarpetasTests
{
    [Theory]
    [InlineData("nota.txt", true)]
    [InlineData("Factura (2).pdf", true)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("que?.txt", false)]
    [InlineData("acaba.", false)]
    [InlineData(" espacio", false)]
    [InlineData(Carpetas.Papelera, false)]
    public void Nombres_validos_en_windows(string nombre, bool valido) => Assert.Equal(valido, Carpetas.NombreValido(nombre));
}

public class ArchivosApiTests(ArchivosFactory factory) : IClassFixture<ArchivosFactory>
{
    private async Task<HttpClient> Dentro()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password });
        return client;
    }

    private static async Task<List<string>> Nombres(HttpClient c, string raiz, string ruta = "")
    {
        var doc = await c.GetFromJsonAsync<JsonElement>($"/api/archivos/lista?raiz={raiz}&ruta={Uri.EscapeDataString(ruta)}");
        return doc.GetProperty("elementos").EnumerateArray().Select(e => e.GetProperty("nombre").GetString()!).ToList();
    }

    [Fact]
    public async Task Sin_sesion_no_se_ve_nada()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/archivos/raices")).StatusCode);
    }

    [Fact]
    public async Task Solo_aparecen_las_carpetas_montadas()
    {
        var c = await Dentro();
        var raices = await c.GetFromJsonAsync<JsonElement>("/api/archivos/raices");
        Assert.Equal(["documentos", "musica"], raices.EnumerateArray().Select(r => r.GetProperty("id").GetString()!).ToArray());
    }

    [Theory]
    [InlineData("documentos", "..")]
    [InlineData("documentos", "../secreto.txt")]
    [InlineData("documentos", "Facturas/../../secreto.txt")]
    [InlineData("documentos", "/etc/passwd")]
    [InlineData("otra", "")]
    [InlineData("documentos", Carpetas.Papelera)]
    public async Task No_se_sale_de_las_carpetas(string raiz, string ruta)
    {
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/archivos/lista?raiz={raiz}&ruta={Uri.EscapeDataString(ruta)}")).StatusCode);
        var r = await c.GetAsync($"/api/archivos/archivo?raiz={raiz}&ruta={Uri.EscapeDataString(ruta)}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Html_nunca_se_abre_en_el_navegador()
    {
        var c = await Dentro();
        var r = await c.GetAsync("/api/archivos/archivo?raiz=documentos&ruta=pagina.html&ver=true");
        Assert.Equal("application/octet-stream", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", r.Content.Headers.ContentDisposition!.DispositionType);

        var t = await c.GetAsync("/api/archivos/archivo?raiz=documentos&ruta=nota.txt&ver=true");
        Assert.Equal("text/plain", t.Content.Headers.ContentType!.MediaType);
        Assert.Contains("sandbox", t.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Subir_renombrar_borrar_y_recuperar()
    {
        var c = await Dentro();
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent("uno"u8.ToArray()), "archivos", "cancion.mp3" },
            { new ByteArrayContent("dos"u8.ToArray()), "archivos", "cancion.mp3" },
        };
        var subida = await c.PostAsync("/api/archivos/subir?raiz=musica", form);
        Assert.Equal(HttpStatusCode.OK, subida.StatusCode);
        Assert.Equal(["cancion (2).mp3", "cancion.mp3"], (await Nombres(c, "musica")).Order().ToArray());

        // un nombre con barra no sirve; uno normal, sí
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/archivos/renombrar", new { raiz = "musica", ruta = "cancion.mp3", nombre = "../x.mp3" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/archivos/renombrar", new { raiz = "musica", ruta = "cancion.mp3", nombre = "tema.mp3" })).StatusCode);

        // la descarga trae el contenido y admite rangos
        var parte = new HttpRequestMessage(HttpMethod.Get, "/api/archivos/archivo?raiz=musica&ruta=tema.mp3");
        parte.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1, 2);
        var r = await c.SendAsync(parte);
        Assert.Equal(HttpStatusCode.PartialContent, r.StatusCode);
        Assert.Equal("no", await r.Content.ReadAsStringAsync());

        // borrar lo manda a la papelera, que no se ve en la lista; recuperar lo devuelve a su sitio
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/archivos?raiz=musica&ruta=tema.mp3")).StatusCode);
        Assert.Equal(["cancion (2).mp3"], await Nombres(c, "musica"));
        var papelera = await c.GetFromJsonAsync<JsonElement>("/api/archivos/papelera");
        var item = papelera.EnumerateArray().Single(p => p.GetProperty("nombre").GetString() == "tema.mp3");
        Assert.Equal("tema.mp3", item.GetProperty("origen").GetString());
        var rec = await c.PostAsJsonAsync("/api/archivos/papelera/recuperar", new { raiz = "musica", id = item.GetProperty("id").GetString() });
        Assert.Equal(HttpStatusCode.OK, rec.StatusCode);
        Assert.Contains("tema.mp3", await Nombres(c, "musica"));
    }

    [Fact]
    public async Task Carpetas_nuevas_y_mover()
    {
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/archivos/carpeta", new { raiz = "documentos", ruta = "", nombre = "Viajes" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/archivos/carpeta", new { raiz = "documentos", ruta = "", nombre = "Viajes" })).StatusCode);
        // una carpeta no entra en sí misma
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/archivos/mover", new { raiz = "documentos", ruta = "Viajes", destino = "Viajes" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/archivos/mover", new { raiz = "documentos", ruta = "Viajes", destino = "Facturas" })).StatusCode);
        Assert.Equal(["Viajes"], await Nombres(c, "documentos", "Facturas"));
    }
}
