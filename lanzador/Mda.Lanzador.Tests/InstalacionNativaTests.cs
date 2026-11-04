using Mda.Lanzador;

namespace Mda.Lanzador.Tests;

public class InstalacionNativaTests
{
    [Fact]
    public void Se_instala_en_archivos_de_programa_y_robocopy_acepta_los_codigos_de_exito()
    {
        Assert.Equal(Path.Combine(@"C:\Archivos de programa", "ZonaPrivada"), InstalacionNativa.Destino(@"C:\Archivos de programa"));
        Assert.StartsWith(@"""C:\dist\ZonaPrivada"" ""C:\Archivos de programa\ZonaPrivada"" /MIR", InstalacionNativa.Robocopy(@"C:\dist\ZonaPrivada\", @"C:\Archivos de programa\ZonaPrivada\"));
        Assert.True(InstalacionNativa.RobocopyBien(0)); Assert.True(InstalacionNativa.RobocopyBien(3)); Assert.True(InstalacionNativa.RobocopyBien(7));
        Assert.False(InstalacionNativa.RobocopyBien(8)); Assert.False(InstalacionNativa.RobocopyBien(16)); Assert.False(InstalacionNativa.RobocopyBien(-1));
    }

    [Fact]
    public void Los_programas_quedan_de_solo_lectura_salvo_para_administradores()
    {
        var o = InstalacionNativa.IcaclsPrograma(@"C:\Archivos de programa\ZonaPrivada");
        Assert.Contains("/inheritance:r", o);
        Assert.Contains($"{Permisos.Administradores}:(OI)(CI)F", o);
        Assert.Contains("*S-1-5-32-545:(OI)(CI)RX", o);      // Usuarios: leer y ejecutar, nada más
        Assert.DoesNotContain("(M)", o);
    }

    [Fact]
    public void Dice_que_falta_cuando_la_carpeta_no_es_el_paquete()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zp-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(4, InstalacionNativa.Faltan(dir).Count);
            foreach (var r in new[] { "ZonaPrivada.exe", @"componentes\api\Mda.Api.exe", @"componentes\postgres\bin\postgres.exe", @"componentes\age\age.exe" })
            {
                var f = Path.Combine(dir, r); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, "x");
            }
            Assert.Empty(InstalacionNativa.Faltan(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
