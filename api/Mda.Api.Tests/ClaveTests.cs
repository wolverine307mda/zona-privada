using System.Net;
using System.Net.Http.Json;

namespace Mda.Api.Tests;

// Clase aparte (su propia API de prueba): cambia la contraseña del usuario
public class ClaveTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Cambiar_la_contrasena_no_te_echa_y_la_vieja_deja_de_valer()
    {
        var c = await factory.Dentro();
        var mal = await c.PostAsJsonAsync("/api/perfil/clave", new { actual = "no-es-esta", nueva = "otra-clave-larga" });
        Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);
        Assert.Contains("actual no es correcta", await mal.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/perfil/clave", new { actual = ApiFactory.Password, nueva = "corta" })).StatusCode);

        var ok = await c.PostAsJsonAsync("/api/perfil/clave", new { actual = ApiFactory.Password, nueva = "otra-clave-larga" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/perfil")).StatusCode); // sigues dentro

        var otro = factory.CreateClient();
        Assert.False((await otro.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).IsSuccessStatusCode);
        Assert.True((await otro.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = "otra-clave-larga" })).IsSuccessStatusCode);
    }
}
