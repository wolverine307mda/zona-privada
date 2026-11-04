using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Mda.Api.Seguridad;

namespace Mda.Api.Tests;

public class PinFactory : ArchivosFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Documentos:PinInicial", "1939");
    }

    public async Task<HttpClient> Dentro()
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = Usuario, password = Password })).EnsureSuccessStatusCode();
        return c;
    }
}

public class PinTests(PinFactory f) : IClassFixture<PinFactory>
{
    private static async Task<bool> Pide(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.Forbidden && (await r.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("pin", out var p) && p.GetBoolean();

    [Fact]
    public void El_pin_son_cuatro_cifras() { Assert.True(Pin.Valido("1939")); Assert.False(Pin.Valido("193")); Assert.False(Pin.Valido("19390")); Assert.False(Pin.Valido("19a9")); Assert.False(Pin.Valido(null)); }

    [Fact]
    public async Task Documentos_la_galeria_de_carpeta_y_los_senuelos_piden_el_pin_pero_las_raices_no()
    {
        var c = await f.Dentro();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/archivos/raices")).StatusCode); // Inicio las cuenta sin PIN
        Assert.True(await Pide(await c.GetAsync("/api/archivos/lista?raiz=documentos")));
        Assert.True(await Pide(await c.GetAsync("/api/archivos/archivo?raiz=documentos&ruta=nota.txt")));
        Assert.True(await Pide(await c.GetAsync("/api/galeria/carpeta")));
        Assert.True(await Pide(await c.GetAsync("/api/senuelos")));
        var estado = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/pin");
        Assert.True(estado.GetProperty("requerido").GetBoolean());
        Assert.False(estado.GetProperty("vigente").GetBoolean());
    }

    [Fact]
    public async Task Con_el_pin_bueno_se_entra_y_al_salir_se_vuelve_a_pedir()
    {
        var c = await f.Dentro();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/pin", new { pin = "1939" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/archivos/lista?raiz=documentos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/senuelos")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/seguridad/pin")).StatusCode);
        Assert.True(await Pide(await c.GetAsync("/api/archivos/lista?raiz=documentos")));
    }

    [Fact]
    public async Task El_pin_de_una_sesion_no_vale_para_otra()
    {
        var a = await f.Dentro();
        var b = await f.Dentro();
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/seguridad/pin", new { pin = "1939" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/archivos/lista?raiz=documentos")).StatusCode);
        Assert.True(await Pide(await b.GetAsync("/api/archivos/lista?raiz=documentos")));
    }

    [Fact]
    public async Task Cinco_fallos_bloquean_la_sesion_aunque_luego_se_acierte()
    {
        var c = await f.Dentro();
        for (var i = 1; i <= 4; i++)
        {
            var r = await c.PostAsJsonAsync("/api/seguridad/pin", new { pin = "0000" });
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal(5 - i, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("restantes").GetInt32());
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.PostAsJsonAsync("/api/seguridad/pin", new { pin = "0000" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.PostAsJsonAsync("/api/seguridad/pin", new { pin = "1939" })).StatusCode); // bloqueada: ni el bueno vale
        Assert.True(await Pide(await c.GetAsync("/api/archivos/lista?raiz=documentos")));
    }

    [Fact]
    public async Task Cambiar_el_pin_pide_confirmar_y_el_nuevo_sustituye_al_viejo()
    {
        var c = await f.Dentro();
        var sin = await c.PutAsJsonAsync("/api/seguridad/pin", new { nuevo = "2468" });
        Assert.Equal(HttpStatusCode.Forbidden, sin.StatusCode);
        Assert.True((await sin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reautenticar").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/seguridad/pin", new { nuevo = "12" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/seguridad/pin", new { nuevo = "2468" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/seguridad/pin", new { pin = "1939" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/pin", new { pin = "2468" })).StatusCode);
        // dejarlo como estaba para el resto de pruebas de la clase
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/seguridad/pin", new { nuevo = "1939" })).StatusCode);
    }
}
