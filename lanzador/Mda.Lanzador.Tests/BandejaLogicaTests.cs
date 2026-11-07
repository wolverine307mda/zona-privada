using Xunit;

namespace Mda.Lanzador.Tests;

public class BandejaLogicaTests
{
    [Theory]
    [InlineData("foto.jpg", "foto.jpg")]
    [InlineData("..\\..\\Windows\\system.ini", "system.ini")]
    [InlineData("a:b*c?.txt", "a_b_c_.txt")]
    [InlineData("NUL", "_NUL")]
    [InlineData("  ..sombra  ", "sombra")]
    public void El_nombre_es_solo_un_nombre_de_archivo_seguro(string e, string esperado) => Assert.Equal(esperado, BandejaLogica.NombreSeguro(e));

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("..")] [InlineData("carpeta/")]
    public void Sin_nombre_no_hay_archivo(string? e) => Assert.Null(BandejaLogica.NombreSeguro(e));

    [Theory]
    [InlineData("a.exe", true)] [InlineData("a.PS1", true)] [InlineData("a.lnk", true)] [InlineData("a.dll", true)] [InlineData("a.pdf", false)] [InlineData("a", false)]
    public void Los_ejecutables_no_se_dejan_en_el_PC(string n, bool bloqueado) => Assert.Equal(bloqueado, BandejaLogica.Bloqueada(n));

    [Fact]
    public void No_pisa_lo_que_ya_hay_y_numera()
    {
        var hay = new HashSet<string> { "foto.jpg", "foto (2).jpg" };
        Assert.Equal("nueva.jpg", BandejaLogica.Unico("nueva.jpg", hay.Contains));
        Assert.Equal("foto (3).jpg", BandejaLogica.Unico("foto.jpg", hay.Contains));
    }

    [Fact]
    public void Un_nombre_largo_se_recorta_con_su_extension() =>
        Assert.Equal(120, BandejaLogica.NombreSeguro(new string('x', 300) + ".txt")!.Length);
}
