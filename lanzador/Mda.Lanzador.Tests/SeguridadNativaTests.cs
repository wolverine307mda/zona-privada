using Mda.Lanzador;

namespace Mda.Lanzador.Tests;

public class SeguridadNativaTests
{
    private static readonly RutasNativas R = new(@"C:\Archivos de programa\ZonaPrivada", @"C:\Datos\ZonaPrivada");

    [Fact]
    public void La_contrasena_del_servicio_es_larga_aleatoria_y_cumple_la_complejidad()
    {
        var a = UsuarioServicio.Contrasena();
        Assert.Equal(40, a.Length);
        Assert.Contains(a, char.IsUpper); Assert.Contains(a, char.IsLower); Assert.Contains(a, char.IsDigit); Assert.Contains(a, c => !char.IsLetterOrDigit(c));
        Assert.NotEqual(a, UsuarioServicio.Contrasena());
    }

    [Fact]
    public void Los_permisos_dejan_la_configuracion_fuera_del_alcance_del_servicio()
    {
        var l = Permisos.Icacls(R, "zp-servicio", new Dictionary<string, string>());
        var config = l.Single(x => x.StartsWith($"\"{R.Config}\""));
        Assert.DoesNotContain("zp-servicio", config);                    // ni el .env ni las contraseñas
        Assert.Contains("/inheritance:r", config);
        Assert.Contains("zp-servicio:(OI)(CI)M", l.Single(x => x.StartsWith($"\"{R.PgDatos}\"")));
        Assert.Contains("zp-servicio:(OI)(CI)R", l.Single(x => x.StartsWith($"\"{R.Copias}\"")));   // las copias las lee, no las escribe
        Assert.EndsWith("zp-servicio:(OI)(CI)RX", l.Single(x => x.StartsWith($"\"{R.Componentes}\"")));   // los programas, solo lectura
        // el directorio raíz de datos no le da nada al servicio
        Assert.DoesNotContain("zp-servicio", l.First(x => x.StartsWith($"\"{R.Datos}\" ")));
    }

    [Fact]
    public void Las_carpetas_compartidas_se_dan_en_lectura_o_modificacion_segun_el_modo_y_sin_quitar_los_permisos_que_ya_tienen()
    {
        var env = new Dictionary<string, string>
        {
            ["CARPETA_DOCUMENTOS"] = @"D:\Docs", ["MODO_DOCUMENTOS"] = "ro", ["CARPETA_IMAGENES"] = @"D:\Fotos", ["CARPETA_MUSICA"] = "relativa", ["CARPETA_VIDEOS"] = "",
        };
        var l = Permisos.Icacls(R, "zp-servicio", env);
        Assert.Contains(l, x => x == "\"D:\\Docs\" /grant zp-servicio:(OI)(CI)R");
        Assert.Contains(l, x => x == "\"D:\\Fotos\" /grant zp-servicio:(OI)(CI)M");
        Assert.DoesNotContain(l, x => x.Contains("relativa"));
        Assert.DoesNotContain(l, x => x.Contains("Docs") && x.Contains("/inheritance"));
    }

    [Fact]
    public void El_cortafuegos_bloquea_la_entrada_a_los_puertos_y_la_regla_se_puede_repetir()
    {
        var c = Cortafuegos.Comandos(8090, 5433);
        Assert.StartsWith("advfirewall firewall delete rule", c[0]);
        Assert.Contains("dir=in action=block", c[1]);
        Assert.Contains("localport=8090,5433", c[1]);
    }

    private const string Netstat = """
          TCP    0.0.0.0:135            0.0.0.0:0              LISTENING       1100
          TCP    127.0.0.1:8090         0.0.0.0:0              LISTENING       4242
          TCP    127.0.0.1:5433         0.0.0.0:0              LISTENING       4343
          TCP    [::1]:5433             [::]:0                 LISTENING       4343
          TCP    0.0.0.0:8080           0.0.0.0:0              LISTENING       999
        """;

    [Fact]
    public void Se_detecta_lo_que_escucha_fuera_del_bucle_local_solo_en_nuestros_puertos()
    {
        var todas = Escuchas.Parsear(Netstat);
        Assert.Equal(5, todas.Count);
        Assert.Empty(Escuchas.Expuestas(todas, 8090, 5433));      // 8080 es de Docker y no se juzga
        var mal = Escuchas.Parsear(Netstat + "\n  TCP    0.0.0.0:8090           0.0.0.0:0              LISTENING       4242\n");
        Assert.Single(Escuchas.Expuestas(mal, 8090, 5433));
    }

    [Fact]
    public void Se_ve_con_que_cuenta_corre_cada_proceso()
    {
        var csv = "\"postgres.exe\",\"4343\",\"Services\",\"0\",\"20.000 KB\",\"Unknown\",\"EQUIPO\\zp-servicio\",\"0:00:01\",\"N/D\"\n" +
                  "\"Mda.Api.exe\",\"4242\",\"Services\",\"0\",\"90.000 KB\",\"Unknown\",\"EQUIPO\\wolve\",\"0:00:05\",\"N/D\"";
        var p = Procesos.Parsear(csv);
        Assert.Equal(2, p.Count);
        var mal = Procesos.ConOtraCuenta(p, "zp-servicio");
        Assert.Single(mal);
        Assert.Contains("Mda.Api.exe", mal[0]);
    }

    [Fact]
    public void La_comprobacion_de_integridad_acepta_solo_el_hash_exacto()
    {
        var f = Path.GetTempFileName();
        try
        {
            File.WriteAllText(f, "hola");
            var h = Integridad.Sha256("hola");
            Assert.True(Integridad.Coincide(f, h));
            Assert.True(Integridad.Coincide(f, h.ToUpperInvariant()));
            Assert.False(Integridad.Coincide(f, Integridad.Sha256("adios")));
            Assert.False(Integridad.Coincide(f, ""));
            Assert.False(Integridad.Coincide(f + ".no", h));
        }
        finally { File.Delete(f); }
    }
}
