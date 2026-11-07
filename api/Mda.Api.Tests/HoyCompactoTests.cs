using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Atajos;

namespace Mda.Api.Tests;

public class HoyCompactoTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Theory]
    [InlineData(523.4, "Disponible 523 €")] [InlineData(523.5, "Disponible 524 €")] [InlineData(0, "Disponible 0 €")] [InlineData(-80.2, "Disponible -80 €")] [InlineData(1234.9, "Disponible 1.235 €")]
    public void El_saldo_se_escribe_redondeado_y_corto(double libre, string esperado) => Assert.Equal(esperado, Hoy.TextoSaldo((decimal)libre));

    [Fact]
    public void El_proximo_dice_hoy_o_mañana_y_recorta_los_titulos_largos()
    {
        var hoy = new DateOnly(2026, 11, 7);
        Assert.Equal("12:00 Cita", Hoy.TextoProximo(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.FromHours(1)), "Cita", hoy));
        Assert.Equal("mañana 09:30 Dentista", Hoy.TextoProximo(new DateTimeOffset(2026, 11, 8, 9, 30, 0, TimeSpan.FromHours(1)), "Dentista", hoy));
        var largo = Hoy.TextoProximo(new DateTimeOffset(2026, 11, 7, 18, 0, 0, TimeSpan.Zero), new string('x', 60), hoy);
        Assert.Equal(6 + 28, largo.Length); Assert.EndsWith("…", largo);
    }

    [Fact]
    public async Task El_resumen_de_siempre_sigue_igual_y_trae_ademas_el_bloque_compacto_para_los_widgets_pequeños()
    {
        var c = await factory.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/hoy");
        // los campos antiguos (los usa el widget grande) siguen
        foreach (var k in new[] { "fecha", "gastoHoy", "gastoHoyTexto", "presupuestos", "agenda", "habitos", "lineas" }) Assert.True(j.TryGetProperty(k, out _), k);
        var cc = j.GetProperty("compacto");
        foreach (var k in new[] { "saldoTexto", "proximo", "presupuestoPct", "tareasHoy", "cumple", "entrenoHoy" }) Assert.True(cc.TryGetProperty(k, out _), k);
        Assert.True(cc.GetProperty("tareasHoy").GetInt32() >= 0);
    }
}
