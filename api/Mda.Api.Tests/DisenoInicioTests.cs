using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Inicio;

namespace Mda.Api.Tests;

public class DisenoInicioTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public void Limpiar_quita_lo_raro_los_repetidos_y_los_tamanos_que_no_existen()
    {
        var d = DisenoInicioEndpoints.Limpiar(new DisenoInicio(["dinero", "dinero", "tiempo", "con espacio", "", new string('x', 41), "a;b"], ["pc", "pc!"], new() { ["dinero"] = "L", ["tiempo"] = "XL", ["mal id"] = "S" }));
        Assert.Equal(["dinero", "tiempo"], d.Orden); Assert.Equal(["pc"], d.Ocultas);
        Assert.Equal(new Dictionary<string, string> { ["dinero"] = "L" }, d.Tamanos);
        var v = DisenoInicioEndpoints.Limpiar(null);
        Assert.Empty(v.Orden!); Assert.Empty(v.Ocultas!); Assert.Empty(v.Tamanos!);
        Assert.Equal(DisenoInicioEndpoints.MaxTarjetas, DisenoInicioEndpoints.Limpiar(new DisenoInicio(Enumerable.Range(0, 200).Select(i => $"t{i}").ToList(), [], [])).Orden!.Count);
    }

    [Fact]
    public async Task Cada_tipo_de_dispositivo_guarda_su_distribucion_y_restablecer_la_borra()
    {
        var c = await factory.Dentro();
        var vacio = await c.GetFromJsonAsync<JsonElement>("/api/inicio/diseno/pc");
        Assert.Equal(0, vacio.GetProperty("orden").GetArrayLength());

        var r = await c.PutAsJsonAsync("/api/inicio/diseno/pc", new { orden = new[] { "tiempo", "dinero" }, ocultas = new[] { "gasolina" }, tamanos = new { dinero = "L" } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var pc = await c.GetFromJsonAsync<JsonElement>("/api/inicio/diseno/pc");
        Assert.Equal("tiempo", pc.GetProperty("orden")[0].GetString()); Assert.Equal("L", pc.GetProperty("tamanos").GetProperty("dinero").GetString());
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/inicio/diseno/movil")).GetProperty("orden").GetArrayLength());       // el móvil es otra cosa

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/inicio/diseno/pc")).StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/inicio/diseno/pc")).GetProperty("orden").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/inicio/diseno/tele")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/inicio/diseno/tele", new { orden = new[] { "a" } })).StatusCode);
    }

    [Fact]
    public async Task Hace_falta_entrar()
    {
        using var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/inicio/diseno/pc")).StatusCode);
    }
}
