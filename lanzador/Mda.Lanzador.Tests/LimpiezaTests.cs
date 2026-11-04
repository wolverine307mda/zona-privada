namespace Mda.Lanzador.Tests;

public class LimpiezaTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("mda-limpieza-").FullName;
    public void Dispose() { try { Directory.Delete(_raiz, true); } catch (IOException) { } }

    private string Archivo(string ruta, int bytes, int diasDeAntiguedad)
    {
        var p = Path.Combine(_raiz, ruta);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[bytes]);
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(-diasDeAntiguedad));
        return p;
    }

    [Fact]
    public void Borra_lo_viejo_deja_lo_reciente_y_cuenta_lo_liberado()
    {
        var viejo = Archivo("viejo.tmp", 1000, 5);
        var viejoDentro = Archivo(@"sub\otro.tmp", 500, 3);
        var reciente = Archivo("reciente.tmp", 700, 0);
        var (bytes, n) = Limpieza.Barrer(_raiz, DateTime.UtcNow.AddDays(-1));
        Assert.Equal(1500, bytes);
        Assert.Equal(2, n);
        Assert.False(File.Exists(viejo)); Assert.False(File.Exists(viejoDentro));
        Assert.True(File.Exists(reciente));
        Assert.False(Directory.Exists(Path.Combine(_raiz, "sub")));     // la subcarpeta quedó vacía y se quita
    }

    [Fact]
    public void Una_subcarpeta_con_algo_reciente_se_queda()
    {
        Archivo(@"obra\viejo.tmp", 100, 9);
        var nuevo = Archivo(@"obra\nuevo.tmp", 100, 0);
        Limpieza.Barrer(_raiz, DateTime.UtcNow.AddDays(-1));
        Assert.True(File.Exists(nuevo));
    }

    [Fact]
    public void Lo_que_esta_en_uso_no_se_borra_ni_rompe_la_limpieza()
    {
        var abierto = Archivo("abierto.tmp", 300, 5);
        var otro = Archivo("otro.tmp", 200, 5);
        using (new FileStream(abierto, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var (bytes, n) = Limpieza.Barrer(_raiz, DateTime.UtcNow.AddDays(-1));
            Assert.Equal(200, bytes); Assert.Equal(1, n);
        }
        Assert.True(File.Exists(abierto));
        Assert.False(File.Exists(otro));
    }

    [Fact]
    public void Una_carpeta_que_no_existe_no_es_un_error() => Assert.Equal((0L, 0), Limpieza.Barrer(Path.Combine(_raiz, "no-existe"), DateTime.UtcNow));

    [Theory]
    [InlineData(5L * 1_048_576, "5 MB")]
    [InlineData(3L * 1_073_741_824 / 2, "1,5 GB")]
    public void Formatea_el_tamaño_en_español(long bytes, string esperado) => Assert.Equal(esperado, Limpieza.Tam(bytes));
}
