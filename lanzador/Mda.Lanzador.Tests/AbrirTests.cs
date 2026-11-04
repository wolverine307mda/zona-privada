namespace Mda.Lanzador.Tests;

public class AbrirTests
{
    [Theory]
    [InlineData("https://ejemplo.com/pagina?x=1&y=2", "url")]
    [InlineData("http://192.168.1.50:8080/", "url")]
    [InlineData("HTTPS://Ejemplo.COM", "url")]
    [InlineData("calculadora", "app")] [InlineData("Bloc", "app")] [InlineData("EXPLORADOR", "app")]
    public void Acepta_direcciones_web_y_las_aplicaciones_de_la_lista(string entrada, string tipo) => Assert.Equal(tipo, Abrir.Destino(entrada)!.Value.Tipo);

    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData(null)]
    [InlineData("file:///C:/Windows/System32/cmd.exe")] [InlineData("javascript:alert(1)")] [InlineData("ms-settings:privacy")] [InlineData("ftp://servidor/archivo")]
    [InlineData("cmd.exe")] [InlineData("powershell")] [InlineData(@"C:\Windows\System32\cmd.exe")] [InlineData("notepad.exe")]
    [InlineData("https://usuario:clave@ejemplo.com")] [InlineData("https://ejemplo.com/a b")] [InlineData("https://ejemplo.com/\"; calc")]
    [InlineData("http://")] [InlineData("//ejemplo.com")] [InlineData("https://ejemplo.com/`whoami`")]
    public void Rechaza_todo_lo_demas(string? entrada) => Assert.Null(Abrir.Destino(entrada));

    [Fact]
    public void Una_direccion_demasiado_larga_se_rechaza()
    {
        Assert.NotNull(Abrir.Destino("https://e.com/" + new string('a', Abrir.MaxLongitud - 14)));
        Assert.Null(Abrir.Destino("https://e.com/" + new string('a', Abrir.MaxLongitud)));
    }

    [Fact]
    public void Las_aplicaciones_son_programas_conocidos_y_solo_esos() =>
        Assert.Equal(["calc.exe", "explorer.exe", "notepad.exe"], Abrir.Aplicaciones.Values.Order());
}
