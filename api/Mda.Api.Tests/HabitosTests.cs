using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Habitos;

namespace Mda.Api.Tests;

public class RachasTests
{
    private const int Todos = 127, LunesAViernes = 31;
    private static readonly DateOnly Hoy = new(2026, 11, 5);          // jueves
    private static Dictionary<DateOnly, int> Hechos(params DateOnly[] dias) => dias.ToDictionary(d => d, _ => 1);
    private static DateOnly D(int atras) => Hoy.AddDays(-atras);

    [Fact]
    public void Toca_usa_la_mascara_con_el_lunes_como_primer_dia()
    {
        Assert.True(Rachas.Toca(LunesAViernes, new DateOnly(2026, 11, 2)));    // lunes
        Assert.True(Rachas.Toca(LunesAViernes, new DateOnly(2026, 11, 6)));    // viernes
        Assert.False(Rachas.Toca(LunesAViernes, new DateOnly(2026, 11, 7)));   // sábado
        Assert.False(Rachas.Toca(LunesAViernes, new DateOnly(2026, 11, 8)));   // domingo
        Assert.True(Rachas.Toca(64, new DateOnly(2026, 11, 8)));               // solo domingos
    }

    [Fact]
    public void Racha_diaria_cuenta_los_dias_seguidos_hasta_hoy()
    {
        var e = Rachas.Calcular(Todos, 1, Hechos(D(0), D(1), D(2)), D(10), Hoy);
        Assert.Equal(3, e.Actual);
        Assert.Equal(3, e.Mejor);
    }

    [Fact]
    public void Hoy_sin_hacer_no_rompe_la_racha_pero_ayer_si()
    {
        Assert.Equal(2, Rachas.Calcular(Todos, 1, Hechos(D(1), D(2)), D(10), Hoy).Actual);   // hoy aún no: se mantiene
        Assert.Equal(0, Rachas.Calcular(Todos, 1, Hechos(D(2), D(3)), D(10), Hoy).Actual);   // ayer falló: rota
    }

    [Fact]
    public void Los_dias_que_no_tocaban_no_rompen_la_racha()
    {
        // lunes a viernes; hoy jueves 5: hechos jueves, miércoles, martes, lunes y el viernes anterior; el finde no cuenta
        var hechos = Hechos(D(0), D(1), D(2), D(3), D(6));              // jueves, mié, mar, lun, viernes 30
        var e = Rachas.Calcular(LunesAViernes, 1, hechos, D(20), Hoy);
        Assert.Equal(5, e.Actual);
    }

    [Fact]
    public void Un_dia_que_no_tocaba_pero_se_hizo_suma()
    {
        var e = Rachas.Calcular(LunesAViernes, 1, Hechos(D(0), D(1), D(2), D(3), D(4)), D(20), Hoy);   // el sábado 31 (D(5)) no está; D(4) es el domingo 1
        Assert.Equal(5, e.Actual);
    }

    [Fact]
    public void La_mejor_racha_puede_ser_anterior_a_la_actual()
    {
        var hechos = Hechos(D(0), D(10), D(9), D(8), D(7), D(6));      // una de 5 antigua y una de 1 ahora
        var e = Rachas.Calcular(Todos, 1, hechos, D(30), Hoy);
        Assert.Equal(1, e.Actual);
        Assert.Equal(5, e.Mejor);
    }

    [Fact]
    public void Con_objetivo_mayor_que_uno_solo_cuenta_si_se_llega()
    {
        var regs = new Dictionary<DateOnly, int> { [D(0)] = 8, [D(1)] = 7, [D(2)] = 8 };
        var e = Rachas.Calcular(Todos, 8, regs, D(10), Hoy);
        Assert.Equal(1, e.Actual);                                      // ayer se quedó en 7
        Assert.Equal('p', e.Mapa[Rachas.DiasMapa - 2]);                  // ayer: a medias
        Assert.Equal('h', e.Mapa[^1]);
    }

    [Fact]
    public void Un_habito_nuevo_no_cuenta_dias_anteriores_a_crearlo()
    {
        var e = Rachas.Calcular(Todos, 1, Hechos(D(0)), D(0), Hoy);
        Assert.Equal(1, e.Actual);
        Assert.Equal(100, e.Cumplimiento30);
        Assert.Equal('-', e.Mapa[0]);
        Assert.Equal('h', e.Mapa[^1]);
    }

    [Fact]
    public void El_cumplimiento_son_los_dias_que_tocaban_en_30_y_hoy_pendiente_no_resta()
    {
        // lunes a viernes durante 30 días; se hizo la mitad exacta de los que tocaban (los pares)
        var tocan = Enumerable.Range(1, 29).Select(D).Where(f => Rachas.Toca(LunesAViernes, f)).ToList();
        var hechos = tocan.Where((_, i) => i % 2 == 0).ToDictionary(f => f, _ => 1);
        var e = Rachas.Calcular(LunesAViernes, 1, hechos, D(60), Hoy);
        Assert.Equal((int)Math.Round(100.0 * hechos.Count / tocan.Count), e.Cumplimiento30);
        Assert.Equal('o', e.Mapa[^1]);                                  // hoy es jueves y aún no está hecho
    }

    [Fact]
    public void El_mapa_distingue_hecho_fallo_libre_y_pendiente()
    {
        var e = Rachas.Calcular(LunesAViernes, 1, Hechos(D(1)), D(10), Hoy);      // ayer (miércoles) hecho
        Assert.Equal(Rachas.DiasMapa, e.Mapa.Length);
        Assert.Equal('o', e.Mapa[^1]);          // hoy jueves pendiente
        Assert.Equal('h', e.Mapa[^2]);          // miércoles hecho
        Assert.Equal('f', e.Mapa[^3]);          // martes falló
        Assert.Equal('l', e.Mapa[^5]);          // domingo 1: no tocaba
    }

    [Fact]
    public void Cambio_de_mes_y_de_anio_no_rompe_nada()
    {
        var hoy = new DateOnly(2027, 1, 2);
        var hechos = new[] { new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 2) }.ToDictionary(f => f, _ => 1);
        Assert.Equal(4, Rachas.Calcular(Todos, 1, hechos, new DateOnly(2026, 12, 1), hoy).Actual);
    }

    [Theory]
    [InlineData("21:30", true)]
    [InlineData("07:05", true)]
    [InlineData("25:00", false)]
    [InlineData("9:5", false)]
    [InlineData(null, true)]
    public void La_hora_del_aviso_va_como_HH_mm(string? h, bool valida) => Assert.Equal(valida, Rachas.HoraValida(h));
}

public class InicioHabitoTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 5);

    [Fact]
    public void Los_dias_apuntados_antes_de_crearlo_adelantan_el_inicio_y_cuentan_para_la_racha()
    {
        var h = new Habito { Creado = Hoy, Dias = 127, Objetivo = 1 };
        var regs = new Dictionary<DateOnly, int> { [Hoy] = 1, [Hoy.AddDays(-1)] = 1, [Hoy.AddDays(-2)] = 1 };
        Assert.Equal(Hoy.AddDays(-2), ServicioHabitos.Inicio(h, regs, Hoy));
        Assert.Equal(3, Rachas.Calcular(h.Dias, h.Objetivo, regs, ServicioHabitos.Inicio(h, regs, Hoy), Hoy).Actual);
    }

    [Fact]
    public void Sin_marcas_anteriores_el_inicio_es_la_creacion_y_nunca_pasa_de_un_anio_atras()
    {
        var h = new Habito { Creado = Hoy.AddDays(-3) };
        Assert.Equal(Hoy.AddDays(-3), ServicioHabitos.Inicio(h, new Dictionary<DateOnly, int>(), Hoy));
        var antiguo = new Habito { Creado = Hoy.AddDays(-1000) };
        Assert.Equal(Hoy.AddDays(-ServicioHabitos.HistoriaDias), ServicioHabitos.Inicio(antiguo, new Dictionary<DateOnly, int>(), Hoy));
    }
}

public class HabitosApiTests
{
    [Fact]
    public async Task Crear_marcar_y_ver_la_racha_por_la_api()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var r = await c.PostAsJsonAsync("/api/habitos", new { nombre = "Meditar", dias = 127, objetivo = 1 });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var m = await (await c.PutAsJsonAsync($"/api/habitos/{id}/marcar", new { })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(m.GetProperty("hecho").GetBoolean());
        Assert.Equal(1, m.GetProperty("actual").GetInt32());

        var lista = await c.GetFromJsonAsync<JsonElement>("/api/habitos");
        Assert.Equal(1, lista.GetArrayLength());
        Assert.Equal("Meditar", lista[0].GetProperty("nombre").GetString());

        var quitado = await (await c.PutAsJsonAsync($"/api/habitos/{id}/marcar", new { cantidad = 0 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(quitado.GetProperty("hecho").GetBoolean());
        Assert.Equal(0, quitado.GetProperty("actual").GetInt32());
    }

    [Fact]
    public async Task Validaciones_y_limites()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/habitos", new { nombre = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/habitos", new { nombre = "Algo", dias = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/habitos", new { nombre = "Algo", horaAviso = "25:99" })).StatusCode);
        var id = (await (await c.PostAsJsonAsync("/api/habitos", new { nombre = "Agua", objetivo = 8, unidad = "vasos" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/habitos/{id}/marcar", new { fecha = "2099-01-01" })).StatusCode);   // el futuro no se marca
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/habitos/{id}/marcar", new { fecha = "ayer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/habitos/999/marcar", new { })).StatusCode);
        for (var i = 0; i < 19; i++) Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/habitos", new { nombre = $"H{i}" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/habitos", new { nombre = "uno más" })).StatusCode);              // el vigésimo primero
    }

    [Fact]
    public async Task Archivar_lo_saca_de_la_lista_y_borrar_se_lleva_sus_marcas()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var id = (await (await c.PostAsJsonAsync("/api/habitos", new { nombre = "Leer" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await c.PutAsJsonAsync($"/api/habitos/{id}/marcar", new { });
        await c.PutAsJsonAsync($"/api/habitos/{id}", new { archivado = true });
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/habitos")).GetArrayLength());
        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>("/api/habitos?archivados=true")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/habitos/{id}")).StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/habitos?archivados=true")).GetArrayLength());
    }

    [Fact]
    public async Task Sin_sesion_no_hay_habitos()
    {
        using var f = new ModulosFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/habitos")).StatusCode);
    }
}
