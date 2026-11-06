using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Contexto;
using Mda.Api.Tiempo;

namespace Mda.Api.Tests;

public class EscenasPorContextoTests
{
    // jueves 5 de noviembre de 2026, 21:30 (hora local)
    private static readonly DateTime Noche = new(2026, 11, 5, 21, 30, 0);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 20, 30, 0, TimeSpan.Zero);
    private static Actual Clima(double grados, int codigo) => new(grados, grados, codigo, false, 5, 60, "", "");
    private static ReglaContexto Regla(Action<ReglaContexto>? ajustar = null)
    {
        var r = new ReglaContexto { Nombre = "Cine", Evento = "llego" };
        ajustar?.Invoke(r);
        return r;
    }
    private static bool Cumple(ReglaContexto r, string evento = "llego", DateTime? local = null, string presencia = "casa", Actual? clima = null, DateTime? ultimaLocal = null)
        => ServicioContexto.Cumple(r, evento, local ?? Noche, presencia, clima, Ahora, ultimaLocal);

    [Fact]
    public void Una_regla_sin_condiciones_se_aplica_en_su_evento_y_solo_en_ese()
    {
        Assert.True(Cumple(Regla()));
        Assert.False(Cumple(Regla(), evento: "salgo"));
        Assert.False(Cumple(Regla(r => r.Activa = false)));
    }

    [Theory]
    [InlineData("20:00", "23:30", true)]
    [InlineData("22:00", "23:59", false)]
    [InlineData("21:30", "21:30", true)]
    [InlineData("22:00", "02:00", false)]    // franja que cruza la medianoche: las 21:30 quedan fuera
    [InlineData("21:00", "02:00", true)]
    public void La_franja_horaria_incluye_los_extremos_y_cruza_la_medianoche(string desde, string hasta, bool dentro)
        => Assert.Equal(dentro, Cumple(Regla(r => { r.Desde = desde; r.Hasta = hasta; })));

    [Fact]
    public void Con_una_sola_punta_vale_desde_o_hasta_ella()
    {
        Assert.True(Cumple(Regla(r => r.Desde = "20:00")));
        Assert.False(Cumple(Regla(r => r.Desde = "22:00")));
        Assert.True(Cumple(Regla(r => r.Hasta = "23:00")));
        Assert.False(Cumple(Regla(r => r.Hasta = "21:00")));
    }

    [Fact]
    public void Los_dias_de_la_semana_filtran()
    {
        Assert.True(Cumple(Regla(r => r.Dias = 8)));      // el jueves es el bit 8
        Assert.False(Cumple(Regla(r => r.Dias = 1 + 2 + 4)));   // lunes a miércoles
    }

    [Theory]
    [InlineData("lluvia", 12, 63, true)] [InlineData("lluvia", 12, 1, false)] [InlineData("seco", 12, 1, true)] [InlineData("seco", 12, 81, false)]
    [InlineData("frio", 9, 1, true)] [InlineData("frio", 15, 1, false)] [InlineData("calor", 30, 1, true)] [InlineData("calor", 20, 1, false)]
    [InlineData("", 20, 95, true)]
    public void El_tiempo_condiciona(string tiempo, double grados, int codigo, bool cumple)
        => Assert.Equal(cumple, Cumple(Regla(r => r.Tiempo = tiempo), clima: Clima(grados, codigo)));

    [Fact]
    public void Sin_datos_del_tiempo_una_regla_que_lo_pide_espera()
    {
        Assert.False(Cumple(Regla(r => r.Tiempo = "lluvia"), clima: null));
        Assert.True(Cumple(Regla(), clima: null));
    }

    [Fact]
    public void Respeta_la_espera_desde_la_ultima_vez()
    {
        Assert.False(Cumple(Regla(r => { r.EsperaMinutos = 60; r.Ultima = Ahora.AddMinutes(-30); })));
        Assert.True(Cumple(Regla(r => { r.EsperaMinutos = 60; r.Ultima = Ahora.AddMinutes(-61); })));
    }

    [Fact]
    public void Las_de_hora_saltan_una_vez_al_dia_dentro_de_su_hora_y_solo_en_casa_si_se_pide()
    {
        var r = Regla(x => { x.Evento = "hora"; x.Desde = "21:00"; });
        Assert.True(Cumple(r, "hora"));                                                  // 21:30 está dentro de la hora siguiente a las 21:00
        Assert.False(Cumple(r, "hora", local: new DateTime(2026, 11, 5, 22, 30, 0)));    // ya pasó
        Assert.False(Cumple(r, "hora", ultimaLocal: new DateTime(2026, 11, 5, 21, 5, 0)));   // ya saltó hoy
        Assert.True(Cumple(r, "hora", ultimaLocal: new DateTime(2026, 11, 4, 21, 5, 0)));
        r.SoloEnCasa = true;
        Assert.False(Cumple(r, "hora", presencia: "fuera"));
        Assert.True(Cumple(r, "hora", presencia: "casa"));
        Assert.False(Cumple(Regla(x => x.Evento = "hora"), "hora"));                      // sin hora no puede saltar
    }

    [Theory]
    [InlineData(0, false)] [InlineData(51, true)] [InlineData(67, true)] [InlineData(68, false)] [InlineData(80, true)] [InlineData(82, true)] [InlineData(95, true)] [InlineData(3, false)]
    public void Reconoce_la_lluvia(int codigo, bool llueve) => Assert.Equal(llueve, ServicioContexto.Llueve(codigo));

    [Fact]
    public async Task Crear_ver_validar_y_borrar_por_la_api()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var r = await c.PostAsJsonAsync("/api/contexto", new { nombre = "Bienvenida", evento = "llego", aviso = "Hola, casa", desde = "20:00", hasta = "23:30", tiempo = "lluvia" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var todo = await c.GetFromJsonAsync<JsonElement>("/api/contexto");
        Assert.Equal(1, todo.GetProperty("reglas").GetArrayLength());
        Assert.Equal("?", todo.GetProperty("presencia").GetProperty("estado").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/contexto", new { nombre = "Sin nada", evento = "llego" })).StatusCode);                          // ni escena ni aviso
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/contexto", new { nombre = "x", evento = "hora", aviso = "a" })).StatusCode);                       // «hora» sin hora
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/contexto", new { nombre = "x", evento = "baila", aviso = "a" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/contexto", new { nombre = "x", aviso = "a", desde = "25:99" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/contexto", new { nombre = "x", aviso = "a", escenaCasaId = 999 })).StatusCode);                    // esa escena no existe
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/contexto", new { nombre = "x", aviso = "a", tiempo = "nieve" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/contexto/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/contexto/{id}")).StatusCode);
    }

    [Fact]
    public async Task Llegar_a_casa_apunta_la_presencia_y_aplica_las_reglas_con_aviso()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await c.PostAsJsonAsync("/api/contexto", new { nombre = "Bienvenida", evento = "llego", aviso = "Luces y calefacción" });
        await c.PostAsJsonAsync("/api/contexto", new { nombre = "Despedida", evento = "salgo", aviso = "Hasta luego" });

        var e = await c.PostAsJsonAsync("/api/contexto/evento", new { evento = "llego" });
        Assert.Equal(HttpStatusCode.OK, e.StatusCode);
        Assert.Contains(f.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("Bienvenida") && m.Contains("Luces y calefacción"));
        Assert.DoesNotContain(f.Avisos.Enviados, m => m.Contains("Despedida"));

        var todo = await c.GetFromJsonAsync<JsonElement>("/api/contexto");
        Assert.Equal("casa", todo.GetProperty("presencia").GetProperty("estado").GetString());
        Assert.NotEqual("", todo.GetProperty("reglas")[0].GetProperty("resultado").GetString());   // anota qué hizo

        await c.PostAsJsonAsync("/api/contexto/evento", new { evento = "llego" });                    // dentro de la espera de 60 min: no repite
        Assert.Single(f.Avisos.Enviados, m => m.Contains("Bienvenida"));

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/contexto/evento", new { evento = "hora" })).StatusCode);
    }

    [Fact]
    public async Task El_atajo_de_casa_tambien_dispara_las_reglas_aunque_el_agente_este_apagado()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await c.PostAsJsonAsync("/api/contexto", new { nombre = "Al volver", evento = "llego", aviso = "bienvenido" });
        var token = await CrearTokenCasa(c);
        using var movil = f.CreateClient();
        movil.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        var r = await movil.PostAsJsonAsync("/api/atajos/casa", new { estado = "llegada" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);                     // el agente de Windows no está en marcha: lo de siempre no se puede
        Assert.Contains(f.Avisos.Enviados, m => m.Contains("Al volver"));        // pero tu escena por contexto sí salta
    }

    private static async Task<string> CrearTokenCasa(HttpClient c)
    {
        var t = await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "iPhone casa", alcances = new[] { "casa" } })).Content.ReadFromJsonAsync<JsonElement>();
        return t.GetProperty("token").GetString()!;
    }
}
