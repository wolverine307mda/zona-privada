using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class EquiposConfianzaLogicaTests
{
    private const string Iphone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";
    private const string Windows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static EquipoConocido Equipo(string ua, string ip) => new() { Id = 1, Nombre = Dispositivo.Nombre(ua), UltimaIp = ip, Pais = "RU", Ciudad = "Moscú" };
    private static SesionAbierta S(string id, string ua, string ip, DateTimeOffset? cerrada = null) => new() { Id = id, Agente = ua, Ip = ip, Cerrada = cerrada };

    [Fact]
    public void Cierra_las_sesiones_abiertas_de_ese_equipo_y_esa_ip_y_nada_mas()
    {
        var e = Equipo(Windows, "5.6.7.8");
        var plan = EquiposConfianza.Plan(e, [S("a", Windows, "5.6.7.8"), S("b", Windows, "5.6.7.8"), S("c", Windows, "9.9.9.9"), S("d", Iphone, "5.6.7.8")], S("mia", Iphone, "1.1.1.1"));
        Assert.True(plan.Permitido);
        Assert.Equal(["a", "b"], plan.SesionesACerrar);
    }

    [Fact]
    public void Un_equipo_sin_sesiones_abiertas_se_marca_igualmente_sin_cerrar_nada()
    {
        var plan = EquiposConfianza.Plan(Equipo(Windows, "5.6.7.8"), [S("vieja", Windows, "5.6.7.8", Ahora)], S("mia", Iphone, "1.1.1.1"));
        Assert.True(plan.Permitido); Assert.Empty(plan.SesionesACerrar);
    }

    [Fact]
    public void El_propio_equipo_no_se_deja_y_se_explica()
    {
        var e = Equipo(Iphone, "1.1.1.1");
        var plan = EquiposConfianza.Plan(e, [S("mia", Iphone, "1.1.1.1")], S("mia", Iphone, "1.1.1.1"));
        Assert.False(plan.Permitido);
        Assert.Contains("no puedes echarte a ti mismo", plan.Motivo);
        Assert.Empty(plan.SesionesACerrar);
    }

    [Fact]
    public void El_mismo_tipo_de_equipo_desde_otra_ip_no_es_el_propio()
    {
        var e = Equipo(Iphone, "5.6.7.8");           // otro iPhone distinto del que uso
        Assert.True(EquiposConfianza.Plan(e, [S("x", Iphone, "5.6.7.8")], S("mia", Iphone, "1.1.1.1")).Permitido);
    }

    [Theory]
    [InlineData(true, 10, "en-linea")]
    [InlineData(false, 60, "reciente")]
    [InlineData(false, 60 * 24 * 2, "reciente")]
    [InlineData(false, 60 * 24 * 4, "hace-tiempo")]
    public void El_estado_sale_de_la_conexion_y_de_cuanto_hace(bool enLinea, int minutos, string esperado) =>
        Assert.Equal(esperado, EquiposConfianza.Estado(Ahora.AddMinutes(-minutos), enLinea, Ahora));

    [Fact]
    public void El_visto_se_lee_en_castellano_con_el_sitio_si_se_sabe()
    {
        Assert.Equal("hace 3 h desde Madrid, ES", EquiposConfianza.Visto(Ahora.AddHours(-3), "ES", "Madrid", Ahora));
        Assert.Equal("hace 12 min desde ES", EquiposConfianza.Visto(Ahora.AddMinutes(-12), "ES", "", Ahora));
        Assert.Equal("ahora mismo", EquiposConfianza.Visto(Ahora.AddSeconds(-20), "?", "", Ahora));
        Assert.Equal("hace 5 d", EquiposConfianza.Visto(Ahora.AddDays(-5), "", "", Ahora));
    }

    [Theory]
    [InlineData("1.2.3.0/24", "1.2.3.0")]
    [InlineData("2a00:1:2::/48", "2a00:1:2::")]
    [InlineData("no-es-ip/24", null)]
    [InlineData("", null)]
    public void De_una_red_sale_una_ip_para_buscarla(string red, string? esperada) => Assert.Equal(esperada, EquiposConfianza.IpDeRed(red));

    [Fact]
    public void El_semaforo_se_pone_ambar_con_un_equipo_sospechoso()
    {
        var d = new DatosVida(true, 5, false, 0, true, 100, 0, true, 0, true, 60, 0, EquiposSospechosos: 1);
        var s = SemaforoVida.Evaluar(d);
        Assert.Equal("ambar", s.Estado);
        Assert.Contains("no lo reconozco", s.Areas.First(a => a.Id == "seguridad").Motivo);
        Assert.Equal("verde", SemaforoVida.Evaluar(d with { EquiposSospechosos = 0 }).Estado);
    }
}

public class EquiposConfianzaApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task No_lo_reconozco_marca_el_equipo_y_de_confianza_quita_la_sospecha()
    {
        var c = await factory.Dentro();
        int id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = new EquipoConocido { Huella = "Windows · Chrome|RU", Nombre = "Windows · Chrome", Pais = "RU", Ciudad = "Moscú", UltimaIp = "5.6.7.8" };
            db.Equipos.Add(e); await db.SaveChangesAsync(); id = e.Id;
        }
        var r = await c.PostAsync($"/api/seguridad/equipos/{id}/no-reconozco", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(j.GetProperty("cambiarClave").GetBoolean());
        Assert.Equal("5.6.7.8", j.GetProperty("ip").GetString());

        var lista = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/equipos");
        var eq = lista.EnumerateArray().First(x => x.GetProperty("id").GetInt32() == id);
        Assert.True(eq.GetProperty("sospechoso").GetBoolean());
        Assert.False(eq.GetProperty("confianza").GetBoolean());
        Assert.Equal("Moscú", eq.GetProperty("ciudad").GetString());
        Assert.Equal("ahora mismo desde Moscú, RU", eq.GetProperty("visto").GetString());

        await c.PutAsJsonAsync($"/api/seguridad/equipos/{id}/confianza", new { confianza = true });
        var tras = (await c.GetFromJsonAsync<JsonElement>("/api/seguridad/equipos")).EnumerateArray().First(x => x.GetProperty("id").GetInt32() == id);
        Assert.False(tras.GetProperty("sospechoso").GetBoolean());
        Assert.True(tras.GetProperty("confianza").GetBoolean());
    }

    [Fact]
    public async Task Un_equipo_que_no_existe_da_404()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/seguridad/equipos/99999/no-reconozco", null)).StatusCode);
    }

    [Fact]
    public async Task Hace_falta_entrar_para_pedir_el_mapa()
    {
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/seguridad/equipos/mapa")).StatusCode);
    }
}
