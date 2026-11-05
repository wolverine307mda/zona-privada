using Mda.Api.Pc;

namespace Mda.Api.Tests;

public class TendenciaDiscoTests
{
    private const long Gb = 1_073_741_824;
    private static readonly DateOnly Dia0 = new(2026, 10, 1);

    private static List<(DateOnly, long)> Serie(int dias, Func<int, long> libre) => Enumerable.Range(0, dias).Select(i => (Dia0.AddDays(i), libre(i))).ToList();

    [Fact]
    public void Un_disco_que_pierde_un_gb_al_dia_se_llena_en_tantos_dias_como_gb_libres()
    {
        var s = Serie(15, i => (100 - i) * Gb);   // el último día quedan 86 GB
        Assert.Equal(86, TendenciaDisco.DiasHastaLleno(s));
    }

    [Fact]
    public void Un_disco_estable_o_que_gana_espacio_no_se_llena()
    {
        Assert.Null(TendenciaDisco.DiasHastaLleno(Serie(15, _ => 50 * Gb)));
        Assert.Null(TendenciaDisco.DiasHastaLleno(Serie(15, i => (50 + i) * Gb)));
    }

    [Fact]
    public void El_ruido_pequeno_no_cuenta()
    {
        Assert.Null(TendenciaDisco.DiasHastaLleno(Serie(15, i => 50 * Gb - i * 100_000)));   // 100 KB al día
    }

    [Fact]
    public void Con_pocos_datos_no_se_adivina()
    {
        Assert.Null(TendenciaDisco.DiasHastaLleno(Serie(5, i => (100 - 5 * i) * Gb)));
    }

    [Fact]
    public void Una_limpieza_a_mitad_de_camino_no_rompe_el_calculo()
    {
        // se llena 2 GB al día y de golpe se liberan 30 GB: la recta sale menos pendiente pero sigue dando un número razonable
        var s = Serie(20, i => i < 10 ? (100 - 2 * i) * Gb : (112 - 2 * (i - 10)) * Gb);
        var d = TendenciaDisco.DiasHastaLleno(s);
        Assert.True(d is null or > 0);
    }

    [Fact]
    public void Un_ritmo_muy_lento_pasa_del_tope_y_se_ignora()
    {
        Assert.Null(TendenciaDisco.DiasHastaLleno(Serie(15, i => 500 * Gb - i * 2_000_000L)));   // 2 MB al día: 250 000 días
    }

    [Fact]
    public void La_regla_avisa_si_el_disco_se_llena_pronto_y_no_si_ya_esta_casi_lleno()
    {
        var hoy = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
        SaludPc Con(long libre) => new(hoy, [], [new VolumenSalud("C:", libre, 500 * Gb)], false);
        var llenado = new Dictionary<string, int> { ["C:"] = 20 };

        var a = ReglasSalud.Evaluar(Con(200 * Gb), null, hoy, null, null, llenado);
        Assert.Contains("disco-llenando:C:", a.Keys);
        Assert.DoesNotContain("disco-libre:C:", a.Keys);

        var b = ReglasSalud.Evaluar(Con(20 * Gb), null, hoy, null, null, llenado);     // ya al 96 %: manda la regla de siempre
        Assert.Contains("disco-libre:C:", b.Keys);
        Assert.DoesNotContain("disco-llenando:C:", b.Keys);

        Assert.DoesNotContain("disco-llenando:C:", ReglasSalud.Evaluar(Con(200 * Gb), null, hoy, null, null, new Dictionary<string, int> { ["C:"] = 90 }).Keys);
        Assert.Contains("ya no se está llenando", ReglasSalud.Resuelto("disco-llenando:C:"));
    }
}
