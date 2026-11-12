using Mda.Api.Seguridad;

namespace Mda.Api.Tests;

public class VigilanteDireccionTests
{
    private static readonly DateTimeOffset Hora = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static ResultadoDireccion Bien(int? dias = 50) => new(true, dias, null, Hora);
    private static ResultadoDireccion Mal(string e = "Timeout") => new(false, null, e, Hora);

    [Fact]
    public void Un_fallo_suelto_no_avisa_pero_el_segundo_seguido_si_y_solo_una_vez()
    {
        var e = new EstadoDireccion();
        Assert.Empty(e.Evaluar(Mal()));
        var avisos = e.Evaluar(Mal());
        Assert.Single(avisos);
        Assert.Contains("no responde", avisos[0]);
        Assert.Empty(e.Evaluar(Mal()));
    }

    [Fact]
    public void Un_parpadeo_se_olvida_y_al_volver_tras_avisar_lo_cuenta()
    {
        var e = new EstadoDireccion();
        e.Evaluar(Mal());
        Assert.Empty(e.Evaluar(Bien()));           // se recuperó antes del segundo fallo: nada que contar
        e.Evaluar(Mal()); e.Evaluar(Mal());
        Assert.Contains("vuelve a responder", Assert.Single(e.Evaluar(Bien())));
        Assert.Empty(e.Evaluar(Bien()));
    }

    [Fact]
    public void Avisa_de_un_certificado_a_punto_de_caducar_una_sola_vez()
    {
        var e = new EstadoDireccion();
        Assert.Empty(e.Evaluar(Bien(30)));
        Assert.Contains("caduca en 7", Assert.Single(e.Evaluar(Bien(7))));
        Assert.Empty(e.Evaluar(Bien(7)));
        Assert.Contains("caducado", Assert.Single(e.Evaluar(new ResultadoDireccion(true, 0, null, Hora.AddDays(7)))));
    }

    [Fact]
    public void Si_se_renueva_el_certificado_y_vuelve_a_acercarse_avisa_de_nuevo()
    {
        var e = new EstadoDireccion();
        Assert.Single(e.Evaluar(Bien(5)));
        Assert.Empty(e.Evaluar(Bien(60)));
        Assert.Single(e.Evaluar(new ResultadoDireccion(true, 5, null, Hora.AddDays(80))));
    }

    [Fact]
    public void Guarda_el_ultimo_resultado_para_enseñarlo()
    {
        var e = new EstadoDireccion();
        Assert.Null(e.Ultimo);
        e.Evaluar(Bien(42));
        Assert.Equal(42, e.Ultimo!.DiasCertificado);
    }
}
