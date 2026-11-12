using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Seguridad;

namespace Mda.Api.Tests;

public class SemaforoTests
{
    private static DatosVida Sano() => new(true, 5, false, 0, true, 100m, 0, true, 0, true, 60, 0);

    private static string Estado(Semaforo s, string area) => s.Areas.Single(a => a.Id == area).Estado;

    [Fact]
    public void Todo_en_orden_es_verde_en_las_cinco_areas()
    {
        var s = SemaforoVida.Evaluar(Sano());
        Assert.Equal("verde", s.Estado);
        Assert.Equal(5, s.Areas.Count);
        Assert.All(s.Areas, a => Assert.Equal("verde", a.Estado));
    }

    [Theory]
    [InlineData(40.0, "ambar")] [InlineData(100.0, "rojo")] [InlineData(10.0, "verde")]
    public void Las_copias_dependen_de_su_antiguedad(double horas, string esperado) => Assert.Equal(esperado, Estado(SemaforoVida.Evaluar(Sano() with { CopiaHoras = horas }), "copias"));

    [Fact]
    public void Sin_copia_es_rojo_y_sin_vigilar_es_ambar()
    {
        Assert.Equal("rojo", Estado(SemaforoVida.Evaluar(Sano() with { CopiaHoras = null }), "copias"));
        Assert.Equal("ambar", Estado(SemaforoVida.Evaluar(Sano() with { CopiaVigilada = false }), "copias"));
    }

    [Fact]
    public void El_global_es_el_peor_de_las_areas_y_cada_una_explica_su_motivo()
    {
        var s = SemaforoVida.Evaluar(Sano() with { PresupuestosPasados = 2, DireccionResponde = false });
        Assert.Equal("rojo", s.Estado);
        Assert.Equal("ambar", Estado(s, "dinero"));
        Assert.Contains("Internet", s.Areas.Single(a => a.Id == "red").Motivo);
        Assert.Equal("rojo", Estado(SemaforoVida.Evaluar(Sano() with { PanicoActivo = true }), "seguridad"));
        Assert.Equal("rojo", Estado(SemaforoVida.Evaluar(Sano() with { SaldoLibre = -5m }), "dinero"));
        Assert.Equal("ambar", Estado(SemaforoVida.Evaluar(Sano() with { DosPasos = false }), "seguridad"));
        Assert.Equal("ambar", Estado(SemaforoVida.Evaluar(Sano() with { AgenteConectado = false }), "pc"));
        Assert.Equal("ambar", Estado(SemaforoVida.Evaluar(Sano() with { DiasCertificado = 3 }), "red"));
        Assert.Equal("ambar", Estado(SemaforoVida.Evaluar(Sano() with { DispositivosCaidos = 1 }), "red"));
    }
}

public class SemaforoApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Pide_sesion_y_devuelve_las_cinco_areas()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/inicio/resumen")).StatusCode);
        var c = await factory.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/inicio/resumen");
        Assert.Equal(5, j.GetProperty("areas").GetArrayLength());
        Assert.Contains(j.GetProperty("estado").GetString(), new[] { "verde", "ambar", "rojo" });
    }
}
