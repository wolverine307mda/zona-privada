using Mda.Lanzador;

namespace Mda.Lanzador.Tests;

public class NativoTests
{
    private static readonly RutasNativas R = new(@"C:\Archivos de programa\ZonaPrivada", @"C:\Datos\ZonaPrivada");
    private static readonly Dictionary<string, string> Env = new()
    {
        ["ADMIN_EMAIL"] = "yo@ejemplo.es", ["ADMIN_PASSWORD"] = "una-frase-muy-larga", ["MDA_CLAVE_MAESTRA"] = "maestra", ["AGENTE_TOKEN"] = "token",
        ["TELEGRAM_TOKEN"] = "123:abc", ["CORREO_SERVIDOR"] = "smtp.ejemplo.es", ["GOOGLE_CLIENT_ID"] = "gid", ["CARPETA_DOCUMENTOS"] = @"D:\Mis docs", ["URL_PUBLICA"] = "https://docker.ts.net",
    };

    [Fact]
    public void Los_programas_van_en_componentes_y_los_datos_en_su_carpeta()
    {
        Assert.EndsWith(Path.Combine("componentes", "api", "Mda.Api.exe"), R.ApiExe);
        Assert.StartsWith(R.Datos, R.PgDatos);
        Assert.StartsWith(R.Datos, R.Claves);
        Assert.DoesNotContain(R.Instalacion, R.PgDatos);   // nada que cambie vive donde están los programas
        Assert.Equal(8, R.DirectoriosDeDatos.Count());
    }

    [Fact]
    public void Postgres_solo_escucha_en_el_bucle_local_y_con_contrasena()
    {
        var conf = ConfigNativa.PostgresConf();
        Assert.Contains("listen_addresses = '127.0.0.1'", conf);
        Assert.Contains("port = 5433", conf);
        Assert.Contains("password_encryption = scram-sha-256", conf);
        var hba = ConfigNativa.PgHba();
        Assert.DoesNotContain("0.0.0.0", hba);
        Assert.DoesNotContain("trust", hba);
        Assert.All(hba.Split('\n', StringSplitOptions.RemoveEmptyEntries), l => Assert.EndsWith("scram-sha-256", l));
    }

    [Fact]
    public void La_api_arranca_en_el_bucle_local_con_su_propia_base_y_su_cookie_relajada()
    {
        var e = ConfigNativa.EntornoApi(Env, R, "clave-pg", ensayo: false);
        Assert.Equal("http://127.0.0.1:8090", e["ASPNETCORE_URLS"]);
        Assert.Contains("Host=127.0.0.1;Port=5433;Database=zonaprivada;Username=mda;Password=clave-pg", e["ConnectionStrings__Postgres"]);
        Assert.Equal("maestra", e["Boveda__Clave"]);
        Assert.Equal("peticion", e["App__CookieSegura"]);
        Assert.Equal("nativo", e["Copias__Origen"]);
        Assert.Equal("", e["Seq__Url"]);
        Assert.Equal(R.ApiDir, e["ASPNETCORE_CONTENTROOT"]);
        Assert.Equal(@"D:\Mis docs", e["Archivos__Rutas__documentos"]);                       // una carpeta real, si se indica
        Assert.Equal(Path.Combine(R.Carpetas, "musica"), e["Archivos__Rutas__musica"]);        // si no, una vacía propia
        Assert.Equal("", e["App__UrlPublica"]);                                                // la dirección de Docker no se reutiliza
    }

    [Fact]
    public void En_ensayo_no_se_pasan_las_claves_de_lo_que_habla_con_el_exterior()
    {
        var e = ConfigNativa.EntornoApi(Env, R, "x", ensayo: true);
        Assert.Equal("true", e["Escritorio__Ensayo"]);
        Assert.DoesNotContain("Telegram__Token", e.Keys);
        Assert.DoesNotContain("Correo__Servidor", e.Keys);
        Assert.DoesNotContain("Google__ClientId", e.Keys);
        var fuera = ConfigNativa.EntornoApi(Env, R, "x", ensayo: false);
        Assert.Equal("123:abc", fuera["Telegram__Token"]);
        Assert.Equal("false", fuera["Escritorio__Ensayo"]);
    }

    [Fact]
    public void El_agente_sigue_con_docker_mientras_el_escritorio_esta_en_ensayo()
    {
        Assert.Equal("http://localhost:8080", ConfigNativa.UrlAgente(nativo: false, ensayo: true));
        Assert.Equal("http://localhost:8080", ConfigNativa.UrlAgente(nativo: true, ensayo: true));
        Assert.Equal("http://127.0.0.1:8090", ConfigNativa.UrlAgente(nativo: true, ensayo: false));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 8)]
    [InlineData(20, 300)]
    public void Los_reintentos_esperan_cada_vez_mas_con_tope(int fallos, int segundos) =>
        Assert.Equal(TimeSpan.FromSeconds(segundos), Retroceso.Espera(fallos));

    [Fact]
    public void Un_proceso_que_aguanta_un_minuto_empieza_de_cero()
    {
        Assert.Equal(4, Retroceso.Siguiente(3, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Retroceso.Siguiente(7, TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void El_resumen_distingue_rojo_ambar_y_verde_y_avisa_del_ensayo()
    {
        Assert.Equal(Nivel.Rojo, EstadosNativos.Resumir(new(false, true, "conectado", true)).Nivel);
        Assert.Equal(Nivel.Rojo, EstadosNativos.Resumir(new(true, false, "conectado", true)).Nivel);
        Assert.Equal(Nivel.Ambar, EstadosNativos.Resumir(new(true, true, "desconectado", false)).Nivel);
        var (n, t) = EstadosNativos.Resumir(new(true, true, "conectado", true));
        Assert.Equal(Nivel.Verde, n);
        Assert.Contains("ensayo", t);
        Assert.DoesNotContain("ensayo", EstadosNativos.Resumir(new(true, true, "conectado", false)).Texto);
    }
}
