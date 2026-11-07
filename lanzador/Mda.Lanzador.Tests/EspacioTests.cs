using Xunit;

namespace Mda.Lanzador.Tests;

public class EspacioTests
{
    [Theory]
    [InlineData("C:\\", "C:\\")]
    [InlineData("c:", "C:\\")]
    [InlineData("d:\\Juegos", "D:\\Juegos")]
    [InlineData("D:/Juegos/Steam/", "D:\\Juegos\\Steam")]
    [InlineData("  C:\\Users\\Mario  ", "C:\\Users\\Mario")]
    public void Acepta_rutas_de_unidades_locales_y_las_deja_limpias(string entrada, string esperado) => Assert.Equal(esperado, Espacio.RaizValida(entrada));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    [InlineData("\\\\servidor\\compartida")]           // red
    [InlineData("//servidor/compartida")]
    [InlineData("Juegos")] [InlineData("\\Windows")] [InlineData("C")]
    [InlineData("C:Windows")]                          // relativa a la unidad actual
    [InlineData("C:\\Users\\..\\Windows")]
    [InlineData("C:\\a?b")] [InlineData("C:\\a*")] [InlineData("C:\\a|b")]
    [InlineData("C:\\a:b")]
    [InlineData("1:\\datos")]
    public void Rechaza_red_rutas_relativas_y_trucos(string? entrada) => Assert.Null(Espacio.RaizValida(entrada));

    [Fact]
    public void Las_mayores_van_de_mas_a_menos_con_tope_y_sin_vacias()
    {
        var l = Espacio.Mayores([new("b", 10), new("a", 10), new("c", 500), new("vacia", 0), new("d", 5)], 3);
        Assert.Equal(["c", "a", "b"], l.Select(x => x.Nombre));        // empate a 10: por nombre
        Assert.Equal(15, Espacio.Cuantas);
        Assert.Empty(Espacio.Mayores([]));
    }

    [Fact]
    public void Escanea_una_carpeta_real_con_sus_tamanos_y_los_archivos_sueltos()
    {
        var raiz = Directory.CreateTempSubdirectory("espacio-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(raiz, "grande", "dentro"));
            Directory.CreateDirectory(Path.Combine(raiz, "pequena"));
            File.WriteAllBytes(Path.Combine(raiz, "grande", "a.bin"), new byte[3000]);
            File.WriteAllBytes(Path.Combine(raiz, "grande", "dentro", "b.bin"), new byte[2000]);
            File.WriteAllBytes(Path.Combine(raiz, "pequena", "c.bin"), new byte[100]);
            File.WriteAllBytes(Path.Combine(raiz, "suelto.bin"), new byte[50]);

            var r = Espacio.Escanear(raiz);
            Assert.False(r.Parcial);
            Assert.Equal(["grande", "pequena", "(archivos sueltos)"], r.Carpetas.Select(c => c.Nombre));
            Assert.Equal(5000, r.Carpetas[0].Bytes);
            Assert.Equal(5150, r.TotalBytes);
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void Con_el_tiempo_agotado_devuelve_un_resultado_parcial_marcado()
    {
        var raiz = Directory.CreateTempSubdirectory("espacio-").FullName;
        try
        {
            for (var i = 0; i < 5; i++) { Directory.CreateDirectory(Path.Combine(raiz, $"c{i}")); File.WriteAllBytes(Path.Combine(raiz, $"c{i}", "x.bin"), new byte[10]); }
            var r = Espacio.Escanear(raiz, TimeSpan.Zero);
            Assert.True(r.Parcial);
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void Una_carpeta_que_no_existe_da_parcial_sin_romper()
    {
        var r = Espacio.Escanear(Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid().ToString("N")));
        Assert.True(r.Parcial); Assert.Empty(r.Carpetas);
    }
}
