using Mda.Lanzador;

namespace Mda.Lanzador.Tests;

public class PrimeraTests
{
    private static Dictionary<string, string> Env(params (string, string)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void Dice_que_falta_en_el_env_incluyendo_lo_que_esta_vacio()
    {
        var faltan = Primera.Faltan(Env(("POSTGRES_PASSWORD", "x"), ("ADMIN_EMAIL", ""), ("SEQ_PASSWORD", "y")));
        Assert.Contains("ADMIN_EMAIL", faltan);
        Assert.Contains("ADMIN_PASSWORD", faltan);
        Assert.Contains("MDA_CLAVE_MAESTRA", faltan);
        Assert.DoesNotContain("POSTGRES_PASSWORD", faltan);
    }

    [Fact]
    public void Las_claves_generadas_tienen_la_forma_que_pide_la_api()
    {
        var maestra = Convert.FromBase64String(Primera.Generar("MDA_CLAVE_MAESTRA"));
        Assert.Equal(32, maestra.Length);
        var token = Primera.Generar("AGENTE_TOKEN");
        Assert.True(token.Length >= 32);                       // la API no acepta menos de 32
        Assert.Matches("^[0-9a-f]+$", token);
        var pass = Primera.Generar("POSTGRES_PASSWORD");
        Assert.Matches("^[A-Za-z0-9]{32}$", pass);
        Assert.NotEqual(Primera.Generar("MDA_CLAVE_MAESTRA"), Primera.Generar("MDA_CLAVE_MAESTRA"));
    }

    [Fact]
    public void Completar_nunca_cambia_una_clave_que_ya_tiene_valor_rellena_las_vacias_y_añade_las_que_faltan()
    {
        var original = "# mi env\r\nPOSTGRES_PASSWORD=la-de-siempre\r\nSEQ_PASSWORD=\r\nADMIN_EMAIL=yo@casa.es\r\n";
        var nuevo = Primera.Completar(original, new Dictionary<string, string>
        {
            ["POSTGRES_PASSWORD"] = "OTRA", ["SEQ_PASSWORD"] = "rellena", ["MDA_CLAVE_MAESTRA"] = "nueva",
        });
        var env = Entorno.LeerEnv(nuevo.Split("\r\n"));
        Assert.Equal("la-de-siempre", env["POSTGRES_PASSWORD"]);   // intacta: cambiarla dejaría la base ilegible
        Assert.Equal("rellena", env["SEQ_PASSWORD"]);
        Assert.Equal("nueva", env["MDA_CLAVE_MAESTRA"]);
        Assert.Equal("yo@casa.es", env["ADMIN_EMAIL"]);
        Assert.StartsWith("# mi env\r\n", nuevo);                    // respeta comentarios y saltos de línea
        Assert.DoesNotContain("\n\n", nuevo.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Completar_sobre_un_archivo_vacio_crea_el_env()
    {
        var nuevo = Primera.Completar("", new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" });
        Assert.Equal("A=1\nB=2\n", nuevo);
    }

    [Fact]
    public void El_diagnostico_cuenta_las_claves_pero_nunca_enseña_sus_valores()
    {
        var salida = Primera.EnvSinValores(["POSTGRES_PASSWORD=secreto-de-verdad", "SEQ_PASSWORD=", "# nota"]);
        Assert.Contains("POSTGRES_PASSWORD = <definida>", salida);
        Assert.Contains("SEQ_PASSWORD = <vacía>", salida);
        Assert.DoesNotContain("secreto-de-verdad", salida);
    }

    [Theory]
    [InlineData("yo@casa.es", true)]
    [InlineData("sin-arroba", false)]
    [InlineData("a@b", false)]
    [InlineData("", false)]
    public void Valida_el_correo(string c, bool ok) => Assert.Equal(ok, Primera.CorreoValido(c));

    [Fact]
    public void La_clave_de_administrador_pide_doce_caracteres()
    {
        Assert.True(Primera.ClaveAdminValida("una frase larga"));
        Assert.False(Primera.ClaveAdminValida("corta"));
        Assert.False(Primera.ClaveAdminValida(null));
    }
}
