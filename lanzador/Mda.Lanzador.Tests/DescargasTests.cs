using System.Net;
using Xunit;

namespace Mda.Lanzador.Tests;

public class DescargasTests
{
    [Theory]
    [InlineData("https://ejemplo.com/a.pdf", true)] [InlineData("http://ejemplo.com:8080/a?x=1", true)]
    [InlineData("ftp://ejemplo.com/a", false)] [InlineData("file:///C:/Windows/win.ini", false)] [InlineData("javascript:alert(1)", false)]
    [InlineData("https://u:p@ejemplo.com/a", false)] [InlineData("https://ejemplo.com/a b", false)] [InlineData("", false)] [InlineData(null, false)]
    public void Solo_direcciones_web_normales(string? p, bool valida) => Assert.Equal(valida, DescargaLogica.Direccion(p) is not null);

    [Fact]
    public void Hasta_300_letras() => Assert.Null(DescargaLogica.Direccion("https://e.com/" + new string('a', DescargaLogica.MaxUrl)));

    [Theory]
    [InlineData("127.0.0.1", true)] [InlineData("10.1.2.3", true)] [InlineData("172.16.0.1", true)] [InlineData("172.31.255.255", true)] [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)] [InlineData("100.64.0.1", true)] [InlineData("100.127.255.255", true)] [InlineData("0.0.0.0", true)] [InlineData("224.0.0.1", true)]
    [InlineData("::1", true)] [InlineData("fe80::1", true)] [InlineData("fc00::1", true)] [InlineData("fd12:3456::1", true)] [InlineData("::ffff:192.168.1.5", true)]
    [InlineData("8.8.8.8", false)] [InlineData("1.1.1.1", false)] [InlineData("172.32.0.1", false)] [InlineData("100.128.0.1", false)] [InlineData("2606:4700:4700::1111", false)]
    public void Reconoce_las_direcciones_que_no_son_de_Internet(string ip, bool local) => Assert.Equal(local, DescargaLogica.EsLocal(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("setup.exe", true)] [InlineData("SCRIPT.PS1", true)] [InlineData("acceso.lnk", true)] [InlineData("foto.jpg", false)] [InlineData("informe.pdf", false)]
    public void No_se_descargan_programas(string nombre, bool bloqueado) => Assert.Equal(bloqueado, DescargaLogica.Bloqueado(nombre));

    [Theory]
    [InlineData(null, "https://e.com/a/mi%20foto.jpg", "mi foto.jpg")]
    [InlineData("informe final.pdf", "https://e.com/x", "informe final.pdf")]
    [InlineData("..\\..\\Windows\\evil.dll", "https://e.com/x", "evil.dll")]
    [InlineData("../../etc/passwd", "https://e.com/x", "passwd")]
    [InlineData("a<b>c:d.txt", "https://e.com/x", "a_b_c_d.txt")]
    [InlineData("CON.txt", "https://e.com/x", "_CON.txt")]
    [InlineData(null, "https://e.com/", "descarga")]
    [InlineData("...", "https://e.com/", "descarga")]
    public void El_nombre_es_seguro(string? propuesto, string url, string esperado) => Assert.Equal(esperado, DescargaLogica.Nombre(propuesto, new Uri(url)));

    [Fact]
    public void Un_nombre_larguisimo_se_recorta_sin_perder_la_extension()
    {
        var n = DescargaLogica.Nombre(new string('a', 400) + ".pdf", new Uri("https://e.com/x"));
        Assert.True(n.Length <= 120);
        Assert.EndsWith(".pdf", n);
    }

    [Fact]
    public void Si_el_nombre_ya_existe_se_numera()
    {
        var carpeta = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Equal("a.pdf", DescargaLogica.Libre(carpeta, "a.pdf"));
            File.WriteAllText(Path.Combine(carpeta, "a.pdf"), "x");
            Assert.Equal("a (1).pdf", DescargaLogica.Libre(carpeta, "a.pdf"));
            File.WriteAllText(Path.Combine(carpeta, "a (1).pdf"), "x");
            Assert.Equal("a (2).pdf", DescargaLogica.Libre(carpeta, "a.pdf"));
        }
        finally { Directory.Delete(carpeta, true); }
    }
}
