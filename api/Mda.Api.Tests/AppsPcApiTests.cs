using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

public class AppsPcLogicaTests
{
    [Theory]
    [InlineData("Visual Studio Code", "visual-studio-code")]
    [InlineData("Cálculadora & más", "calculadora-mas")]
    [InlineData("¿¿??", "app")]
    public void La_clave_sale_del_nombre_sin_tildes_ni_simbolos(string nombre, string esperada) => Assert.Equal(esperada, AppsPcEndpoints.Clave(nombre, []));

    [Fact]
    public void Si_la_clave_ya_existe_se_numera() => Assert.Equal("chrome-3", AppsPcEndpoints.Clave("Chrome", ["chrome", "chrome-2"]));

    [Theory]
    [InlineData("explorer", true)] [InlineData("Mda.Api", true)] [InlineData("tailscaled", true)] [InlineData("chrome", false)]
    public void Los_protegidos_coinciden_con_los_del_agente(string n, bool p) => Assert.Equal(p, AppsPcEndpoints.EsProtegido(n));

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)] [InlineData("C:\\cmd.exe", false)] [InlineData("..\\x", false)] [InlineData("a;b", false)]
    public void Los_identificadores_se_validan_tambien_en_la_api(string id, bool ok) => Assert.Equal(ok, AppsPcEndpoints.AppIdValido(id));
}

public class AppsPcApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task El_catalogo_solo_admite_aplicaciones_que_el_agente_ha_listado()
    {
        var c = await factory.Dentro();
        var mala = await c.PutAsJsonAsync("/api/pc/apps", new[] { new { appId = "Inventada!App", nombre = "X", delicada = false } });
        Assert.Equal(HttpStatusCode.BadRequest, mala.StatusCode);
        var vacio = await c.GetFromJsonAsync<JsonElement>("/api/pc/apps");
        Assert.Equal(0, vacio.GetProperty("catalogo").GetArrayLength());
    }

    [Fact]
    public async Task Abrir_una_clave_que_no_existe_da_404_y_sin_agente_avisa()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/pc/apps/no-existe/abrir", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/pc/apps/actualizar", null)).StatusCode);   // el agente no está en marcha en las pruebas
    }

    [Fact]
    public async Task Cerrar_algo_que_no_esta_en_la_lista_no_se_deja()
    {
        var c = await factory.Dentro();
        var r = await c.PostAsJsonAsync("/api/pc/procesos/cerrar", new { pid = 1234, inicio = 1700000000, forzar = false });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Los_resultados_del_agente_piden_su_token()
    {
        var anonimo = factory.CreateClient();
        Assert.True((await anonimo.PostAsJsonAsync("/api/sistema/agente/apps", new { apps = new[] { new { nombre = "X", appId = "X!App" } } })).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.True((await anonimo.PostAsJsonAsync("/api/sistema/agente/procesos", new { procesos = Array.Empty<object>() })).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Por_la_ruta_general_no_se_pueden_pedir_las_ordenes_de_apps()
    {
        var c = await factory.Dentro();
        foreach (var tipo in new[] { "abrir-app", "cerrar", "apps", "procesos" })
        {
            var r = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo, parametro = "x" });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
    }
}
