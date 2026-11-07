using Xunit;

namespace Mda.Lanzador.Tests;

public class AppsPcTests
{
    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("{6D809377-6AF0-444B-8957-A3773F02200E}\\Google\\Chrome\\Application\\chrome.exe")]
    [InlineData("Claude_pzs8sxrjxfjjc!Claude")]
    public void Acepta_identificadores_de_aplicaciones_del_menu_inicio(string id) => Assert.True(AppsPc.AppIdValido(id));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]        // una ruta con unidad
    [InlineData("..\\..\\cmd.exe")]
    [InlineData("\\\\red\\carpeta\\x.exe")]
    [InlineData("a\"b")] [InlineData("a;b")] [InlineData("a&b")] [InlineData("a|b")] [InlineData("a`b")] [InlineData("a$b")]
    public void Rechaza_rutas_comillas_y_caracteres_de_consola(string? id) => Assert.False(AppsPc.AppIdValido(id));

    [Theory]
    [InlineData("explorer", true)] [InlineData("Explorer.EXE", true)] [InlineData("Mda.Api", true)] [InlineData("Mda.Agente", true)] [InlineData("ZonaPrivada", true)]
    [InlineData("postgres", true)] [InlineData("tailscaled", true)] [InlineData("Tailscale-ipn", true)] [InlineData("svchost", true)] [InlineData("winlogon", true)] [InlineData("dwm", true)]
    [InlineData("chrome", false)] [InlineData("Claude", false)] [InlineData("Code", false)] [InlineData("notepad", false)]
    public void Los_protegidos_no_se_cierran(string nombre, bool protegido) => Assert.Equal(protegido, AppsPc.EsProtegido(nombre));

    [Theory]
    [InlineData("1234:1700000000:amable", 1234, 1700000000L, false)]
    [InlineData("99:5:forzar", 99, 5L, true)]
    public void El_cierre_lleva_numero_hora_y_modo(string p, int pid, long inicio, bool forzar) => Assert.Equal((pid, inicio, forzar), AppsPc.Cierre(p));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("1234")] [InlineData("1234:1700000000")] [InlineData("4:1700000000:amable")] [InlineData("0:1:amable")]
    [InlineData("abc:1:amable")] [InlineData("1234:0:amable")] [InlineData("1234:1700000000:matar")] [InlineData("1234:1700000000:amable:extra")]
    public void Un_cierre_mal_formado_no_vale(string? p) => Assert.Null(AppsPc.Cierre(p));

    [Fact]
    public void Un_numero_reutilizado_por_otro_programa_no_es_el_mismo_proceso()
    {
        Assert.True(AppsPc.MismoProceso(1_700_000_000, 1_700_000_001));
        Assert.False(AppsPc.MismoProceso(1_700_000_000, 1_700_000_500));
    }

    [Fact]
    public void Interpreta_la_lista_de_Get_StartApps_con_uno_o_varios_y_descarta_lo_raro()
    {
        var varios = """[{"Name":"Calculadora","AppID":"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"},{"Name":"Chrome","AppID":"{ABC}\\Google\\chrome.exe"},{"Name":"Mala","AppID":"C:\\x.exe"},{"Name":"","AppID":"Algo!App"},{"Name":"Calculadora repetida","AppID":"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"}]""";
        var l = AppsPc.InterpretarStartApps(varios);
        Assert.Equal(["Calculadora", "Chrome"], l.Select(x => x.Nombre));
        var uno = AppsPc.InterpretarStartApps("""{"Name":"Claude","AppID":"Claude_pzs8sxrjxfjjc!Claude"}""");
        Assert.Single(uno);
        Assert.Empty(AppsPc.InterpretarStartApps(""));
        Assert.Empty(AppsPc.InterpretarStartApps("no es json"));
    }
}
