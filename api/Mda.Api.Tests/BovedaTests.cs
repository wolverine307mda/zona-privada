using System.Security.Cryptography;
using Mda.Api.Seguridad;

namespace Mda.Api.Tests;

public class BovedaTests
{
    [Fact]
    public void Lo_cifrado_se_descifra_y_no_se_parece_al_original()
    {
        var c = Boveda.Cifrar("hola, mundo ñandú 🔐", "notas");
        Assert.StartsWith("v1:", c);
        Assert.DoesNotContain("hola", c);
        Assert.Equal("hola, mundo ñandú 🔐", Boveda.Descifrar(c, "notas"));
    }

    [Fact]
    public void Cada_cifrado_es_distinto_aunque_el_texto_sea_igual()
    {
        Assert.NotEqual(Boveda.Cifrar("igual", "notas"), Boveda.Cifrar("igual", "notas"));
    }

    [Fact]
    public void Un_propósito_no_abre_el_dato_de_otro()
    {
        var c = Boveda.Cifrar("secreto", "notas");
        Assert.Throws<InvalidOperationException>(() => Boveda.Descifrar(c, "portapapeles"));
    }

    [Fact]
    public void Un_dato_tocado_no_se_descifra()
    {
        var c = Boveda.Cifrar("secreto", "notas");
        var bytes = Convert.FromBase64String(c[3..]);
        bytes[^1] ^= 1;
        Assert.Throws<InvalidOperationException>(() => Boveda.Descifrar("v1:" + Convert.ToBase64String(bytes), "notas"));
    }

    [Fact]
    public void Lo_antiguo_sin_cifrar_se_lee_tal_cual_y_lo_vacio_se_queda_vacio()
    {
        Assert.Equal("nota de siempre", Boveda.Descifrar("nota de siempre", "notas"));
        Assert.Equal("", Boveda.Cifrar("", "notas"));
        Assert.Equal("", Boveda.Cifrar(null, "notas"));
        Assert.Equal("", Boveda.Descifrar(null, "notas"));
    }

    [Fact]
    public void La_clave_maestra_en_produccion_es_obligatoria_y_se_valida()
    {
        Assert.Throws<InvalidOperationException>(() => Boveda.Iniciar(null, exigir: true));
        Assert.Throws<InvalidOperationException>(() => Boveda.Iniciar("esto no es base64!!", exigir: false));
        Assert.Throws<InvalidOperationException>(() => Boveda.Iniciar(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)), exigir: false));
        Boveda.Iniciar(null, exigir: false); // sin clave y sin exigirla (desarrollo): no pasa nada
    }
}
