using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Contexto;
using Mda.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class DormirLogicaTests
{
    [Fact]
    public void La_alarma_es_hora_y_media_antes_de_lo_primero_del_dia()
    {
        Assert.Equal(new TimeOnly(7, 30), Dormir.Alarma(new TimeOnly(9, 0)));
        Assert.Equal(new TimeOnly(5, 30), Dormir.Alarma(new TimeOnly(7, 0)));
    }

    [Fact]
    public void Sin_nada_por_la_manana_o_a_horas_raras_no_se_sugiere_alarma()
    {
        Assert.Null(Dormir.Alarma(null));
        Assert.Null(Dormir.Alarma(new TimeOnly(1, 0)));       // no tiene sentido despertar a las 23:30 de la víspera
        Assert.Null(Dormir.Alarma(new TimeOnly(2, 0)));
    }

    [Fact]
    public void El_mensaje_de_noche_junta_agenda_tiempo_alarma_y_lo_que_se_queda_abierto()
    {
        var casa = new RevisionSalida([new(1, "Ventana salón", 5)], [new(3, "Plancha", 20)], [], []);
        var m = Dormir.Mensaje(true, "Mañana tienes: 📅 09:00 Reunión", "4 ° y nublado", new TimeOnly(7, 30), casa, 0, ["«Dormir»: casa «Noche»: 3 ajuste(s)"]);
        Assert.Equal("Buenas noches. Mañana tienes: 📅 09:00 Reunión Tiempo: 4 ° y nublado. Despierta a las 07:30. Ojo: Ventana salón abierta; Plancha encendido. Hecho: «Dormir»: casa «Noche»: 3 ajuste(s).", m);
    }

    [Fact]
    public void El_mensaje_de_la_manana_cuenta_las_tareas_y_no_sugiere_alarma_ni_mira_la_casa()
    {
        var m = Dormir.Mensaje(false, "Hoy lo tienes libre: no hay nada en la agenda.", null, new TimeOnly(7, 30), new RevisionSalida([new(1, "V", null)], [], [], []), 3, []);
        Assert.Equal("Buenos días. Hoy lo tienes libre: no hay nada en la agenda. Tareas para hoy: 3. Ojo: V abierta.", m);
    }

    [Fact]
    public void Los_eventos_de_dormir_y_despertar_son_validos_en_las_reglas()
    {
        Assert.Contains("duermo", ServicioContexto.Eventos); Assert.Contains("despierto", ServicioContexto.Eventos);
        Assert.Empty(ServicioContexto.Validar(new ReglaContextoEntrada("Noche", null, "duermo", "22:00", "04:00", null, null, null, null, null, "Buenas noches", null), true));
        Assert.Contains("evento", ServicioContexto.Validar(new ReglaContextoEntrada("x", null, "siesta", null, null, null, null, null, null, null, "a", null), true).Keys);
    }

    [Fact]
    public void Una_regla_de_dormir_cumple_en_su_franja_aunque_pase_de_medianoche()
    {
        var r = new ReglaContexto { Evento = "duermo", Desde = "22:00", Hasta = "04:00", Aviso = "x" };
        var ahora = new DateTimeOffset(2026, 11, 7, 23, 0, 0, TimeSpan.Zero);
        Assert.True(ServicioContexto.Cumple(r, "duermo", new DateTime(2026, 11, 7, 23, 0, 0), "casa", null, ahora, null));
        Assert.True(ServicioContexto.Cumple(r, "duermo", new DateTime(2026, 11, 8, 1, 30, 0), "casa", null, ahora, null));
        Assert.False(ServicioContexto.Cumple(r, "duermo", new DateTime(2026, 11, 7, 15, 0, 0), "casa", null, ahora, null));
        Assert.False(ServicioContexto.Cumple(r, "despierto", new DateTime(2026, 11, 7, 23, 0, 0), "casa", null, ahora, null));
    }
}

public class DormirApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task La_rutina_aplica_la_regla_del_evento_y_contesta_con_el_mensaje()
    {
        var c = await factory.Dentro();
        var alta = await c.PostAsJsonAsync("/api/contexto", new { nombre = "Noche", evento = "duermo", aviso = "Que descanses", esperaMinutos = 1 });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var r = await c.PostAsJsonAsync("/api/contexto/dormir", new { evento = "duermo" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var m = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString()!;
        Assert.StartsWith("Buenas noches.", m);
        Assert.Contains("«Noche»", m);

        // «despierto» no dispara las reglas de «duermo»
        var d = (await (await c.PostAsJsonAsync("/api/contexto/dormir", new { evento = "despierto" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString()!;
        Assert.StartsWith("Buenos días.", d);
        Assert.DoesNotContain("«Noche»", d);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/contexto/dormir", new { evento = "siesta" })).StatusCode);
    }

    [Fact]
    public async Task El_atajo_dormir_funciona_con_token_de_casa_y_rechaza_otra_cosa()
    {
        var c = await factory.Dentro();
        var token = (await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "Dormir", alcances = new[] { "casa" } })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var movil = factory.CreateClient();
        movil.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var ok = await movil.PostAsJsonAsync("/api/atajos/dormir", new { estado = "duermo" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.StartsWith("Buenas noches.", (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await movil.PostAsJsonAsync("/api/atajos/dormir", new { estado = "quizá" })).StatusCode);
    }
}
