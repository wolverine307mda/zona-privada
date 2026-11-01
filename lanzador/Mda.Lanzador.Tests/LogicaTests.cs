using Mda.Lanzador;

namespace Mda.Lanzador.Tests;

public class EntornoTests
{
    [Fact]
    public void El_env_ignora_comentarios_vacias_y_quita_comillas()
    {
        var d = Entorno.LeerEnv(["# comentario", "", "AGENTE_TOKEN=abc123", "URL_PUBLICA = \"https://x.ts.net\"", "SIN_VALOR", "CON_IGUAL=a=b", "VACIA="]);
        Assert.Equal("abc123", d["AGENTE_TOKEN"]);
        Assert.Equal("https://x.ts.net", d["URL_PUBLICA"]);
        Assert.Equal("a=b", d["CON_IGUAL"]);
        Assert.Equal("", d["VACIA"]);
        Assert.False(d.ContainsKey("SIN_VALOR"));
        Assert.False(d.ContainsKey("# comentario"));
    }

    [Fact]
    public void Busca_la_carpeta_subiendo_desde_cada_punto_de_partida()
    {
        static bool Existe(string d) => d == Path.GetFullPath(Path.Combine(Path.GetTempPath(), "proyecto"));
        var raiz = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "proyecto"));
        Assert.Equal(raiz, Entorno.BuscarCarpeta([null, "", Path.Combine(raiz, "lanzador", "publicado")], Existe));
        Assert.Null(Entorno.BuscarCarpeta([Path.Combine(Path.GetTempPath(), "otra")], Existe));
    }

    [Fact]
    public void Elige_el_primer_candidato_que_existe()
    {
        Assert.Equal("b", Entorno.PrimeroQueExiste([null, "a", "b", "c"], x => x is "b" or "c"));
        Assert.Null(Entorno.PrimeroQueExiste(["a"], _ => false));
    }

    [Fact]
    public void Los_servicios_salen_uno_por_linea_sin_importar_mayusculas()
    {
        var s = Entorno.Servicios("db\r\napi\n\nSeq\n");
        Assert.Equal(3, s.Count);
        Assert.Contains("SEQ", s);
    }
}

public class EstadosTests
{
    private static HashSet<string> S(params string[] x) => x.ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Sin_docker_es_rojo()
    {
        var (n, t) = Estados.Resumir(new(false, S("db", "api"), S(), false, null, "conectado"));
        Assert.Equal(Nivel.Rojo, n);
        Assert.Contains("Docker", t);
    }

    [Fact]
    public void Si_falta_la_api_o_la_base_es_rojo_y_dice_cual()
    {
        var (n, t) = Estados.Resumir(new(true, S("db", "api", "seq"), S("db", "seq"), false, null, "conectado"));
        Assert.Equal(Nivel.Rojo, n);
        Assert.Contains("api", t);
    }

    [Fact]
    public void Con_la_api_en_marcha_pero_otro_servicio_parado_es_ambar()
    {
        var (n, t) = Estados.Resumir(new(true, S("db", "api", "tailscale"), S("db", "api"), true, null, "conectado"));
        Assert.Equal(Nivel.Ambar, n);
        Assert.Contains("tailscale", t);
    }

    [Fact]
    public void Sin_salida_a_internet_o_con_el_agente_caido_es_ambar()
    {
        Assert.Equal(Nivel.Ambar, Estados.Resumir(new(true, S("db", "api"), S("db", "api"), true, false, "conectado")).Nivel);
        var (n, t) = Estados.Resumir(new(true, S("db", "api"), S("db", "api"), true, true, "token incorrecto"));
        Assert.Equal(Nivel.Ambar, n);
        Assert.Contains("token incorrecto", t);
    }

    [Fact]
    public void Todo_bien_es_verde()
    {
        var (n, _) = Estados.Resumir(new(true, S("db", "api", "seq"), S("db", "api", "seq"), true, true, "conectado"));
        Assert.Equal(Nivel.Verde, n);
    }
}

public class TareaProgramadaTests
{
    [Fact]
    public void La_tarea_arranca_al_iniciar_sesion_con_privilegios_maximos_y_escapa_los_caracteres()
    {
        var xml = TareaProgramada.Xml(@"C:\Users\Mario & Co\ZonaPrivada.exe", @"PC\Mario", TimeSpan.FromSeconds(20));
        var doc = System.Xml.Linq.XDocument.Parse(xml); // es XML válido aunque la ruta lleve «&»
        Assert.Contains("<LogonTrigger>", xml);
        Assert.Contains("<RunLevel>HighestAvailable</RunLevel>", xml);
        Assert.Contains("<Delay>PT20S</Delay>", xml);
        Assert.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", xml);
        Assert.Contains(@"C:\Users\Mario &amp; Co\ZonaPrivada.exe", xml);
        Assert.NotNull(doc.Root);
    }
}
