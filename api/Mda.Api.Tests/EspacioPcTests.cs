using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

public class EspacioPcTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Theory]
    [InlineData("c:\\", "C:\\")]
    [InlineData("D:/Juegos/", "D:\\Juegos")]
    [InlineData("C:\\Users\\Mario Pérez\\Descargas", "C:\\Users\\Mario Pérez\\Descargas")]
    public void Valida_rutas_locales(string e, string esperado) => Assert.Equal(esperado, EspacioPc.RaizValida(e));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("\\\\red\\carpeta")] [InlineData("C:\\a\\..\\b")] [InlineData("C:Windows")] [InlineData("relativa\\ruta")] [InlineData("C:\\a|b")]
    public void Rechaza_lo_que_no_es_una_unidad_local(string? e) => Assert.Null(EspacioPc.RaizValida(e));

    [Fact]
    public void Los_tamanos_se_leen_bien()
    {
        Assert.Equal("1,5 GB".Replace(',', '.'), EspacioPc.Formato((long)(1.5 * (1L << 30))).Replace(',', '.'));
        Assert.Equal("200 MB", EspacioPc.Formato(200L << 20));
        Assert.Equal("10 KB", EspacioPc.Formato(10 * 1024));
    }

    [Fact]
    public async Task Una_ruta_mala_da_error_y_una_buena_encola_la_orden()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/espacio", new { raiz = "\\\\red\\x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await c.PostAsJsonAsync("/api/pc/espacio", new { raiz = "d:/Juegos" })).StatusCode);
        var sin = await c.GetAsync("/api/pc/espacio");
        Assert.Equal(HttpStatusCode.OK, sin.StatusCode);
    }

    [Fact]
    public async Task Por_la_ruta_general_de_ordenes_no_se_puede_pedir()
    {
        var c = await factory.Dentro();
        var r = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "espacio", parametro = "C:\\" });
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
        Assert.NotEqual(HttpStatusCode.Accepted, r.StatusCode);
    }

    [Fact]
    public async Task El_agente_no_puede_mandar_resultados_sin_su_token()
    {
        var anonimo = factory.CreateClient();
        var r = await anonimo.PostAsJsonAsync("/api/sistema/agente/espacio", new { raiz = "C:\\", hora = DateTimeOffset.UtcNow, carpetas = Array.Empty<object>(), totalBytes = 0, parcial = false, segundos = 1 });
        Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }
}

public class AvisosDiscoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private static SaludPc Salud(int? desgaste = 10) => new(Ahora, [new DiscoSalud("0", "SSD", "Healthy", "OK", 40, desgaste, 0, 0, 1234)], [new VolumenSalud("C:", 400, 1000)], false);

    [Fact]
    public void Con_45_dias_o_menos_avisa_con_calma_y_con_14_o_menos_con_urgencia_y_otra_clave()
    {
        var calma = ReglasSalud.Evaluar(Salud(), null, Ahora, 3, null, new Dictionary<string, int> { ["C:"] = 40 });
        Assert.Contains("disco-llenando:C:", calma.Keys); Assert.DoesNotContain("disco-llenando-ya:C:", calma.Keys);
        var urgente = ReglasSalud.Evaluar(Salud(), null, Ahora, 3, null, new Dictionary<string, int> { ["C:"] = 10 });
        Assert.Contains("disco-llenando-ya:C:", urgente.Keys); Assert.DoesNotContain("disco-llenando:C:", urgente.Keys);
        Assert.Empty(ReglasSalud.Evaluar(Salud(), null, Ahora, 3, null, new Dictionary<string, int> { ["C:"] = 46 }));
    }

    [Fact]
    public void El_desgaste_avisa_desde_el_80_por_ciento()
    {
        Assert.DoesNotContain("disco-desgaste:0", ReglasSalud.Evaluar(Salud(79), null, Ahora, 3).Keys);
        Assert.Contains("disco-desgaste:0", ReglasSalud.Evaluar(Salud(80), null, Ahora, 3).Keys);
    }

    [Fact]
    public void Los_dos_avisos_de_llenado_se_dan_por_resueltos_con_su_texto()
    {
        Assert.Contains("ya no se está llenando", ReglasSalud.Resuelto("disco-llenando-ya:C:"));
        Assert.Contains("ya no se está llenando", ReglasSalud.Resuelto("disco-llenando:C:"));
    }
}
