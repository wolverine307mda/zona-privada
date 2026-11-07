using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Pc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class UbicacionIphoneTests
{
    // ---- la lógica ----

    [Theory]
    [InlineData(40.41683, -3.70379, true)]
    [InlineData(-90, 180, true)]
    [InlineData(90.1, 0, false)]
    [InlineData(0, -180.1, false)]
    [InlineData(double.NaN, 0, false)]
    [InlineData(0, double.PositiveInfinity, false)]
    public void Las_coordenadas_se_validan(double lat, double lon, bool bien) => Assert.Equal(bien, UbicacionIphone.CoordenadasValidas(lat, lon));

    [Fact]
    public void El_parametro_usa_punto_decimal_siempre_y_cinco_decimales()
    {
        var antes = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("es-ES");
            Assert.Equal("fijar:40.41683,-3.70379:90", UbicacionIphone.Parametro("fijar", 40.416829999, -3.703790001, 90));
            Assert.Equal("ir:41.5,2:30:bici", UbicacionIphone.Parametro("ir", 41.5, 2, 30, "bici"));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = antes; }
    }

    [Fact]
    public void Los_errores_dicen_que_campo_esta_mal()
    {
        Assert.Null(UbicacionIphone.Errores(40, -3, 60, null, false));
        Assert.Contains("lat", UbicacionIphone.Errores(99, 0, 60, null, false)!.Keys);
        Assert.Contains("minutos", UbicacionIphone.Errores(0, 0, 0, null, false)!.Keys);
        Assert.Contains("minutos", UbicacionIphone.Errores(0, 0, 721, null, false)!.Keys);
        Assert.Contains("modo", UbicacionIphone.Errores(0, 0, 60, "avion", true)!.Keys);
        Assert.Null(UbicacionIphone.Errores(0, 0, 60, "coche", true));
    }

    [Fact]
    public void La_posicion_avanza_con_el_tiempo_y_desaparece_al_acabar()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var s = new SimulacionActual(40, -3, t0, t0.AddHours(1), "andar", 41, -3, 1000);
        Assert.Equal((40.0, -3.0), UbicacionIphone.Posicion(s, t0)!.Value);
        var mitad = UbicacionIphone.Posicion(s, t0.AddSeconds(500))!.Value;
        Assert.Equal(40.5, mitad.Lat, 6);
        Assert.Equal((41.0, -3.0), UbicacionIphone.Posicion(s, t0.AddSeconds(2000))!.Value);   // llegó y se queda
        Assert.Null(UbicacionIphone.Posicion(s, t0.AddHours(2)));                                // pasó su hora de fin
        var fija = s with { DestinoLat = null, DestinoLon = null, Segundos = 0 };
        Assert.Equal((40.0, -3.0), UbicacionIphone.Posicion(fija, t0.AddMinutes(30))!.Value);
    }

    [Fact]
    public void Los_nombres_de_los_sitios_se_limpian_y_se_recortan()
    {
        Assert.Equal("Casa", UbicacionIphone.LimpiarNombre("  Casa\u0000\n "));
        Assert.Equal(UbicacionIphone.MaxNombre, UbicacionIphone.LimpiarNombre(new string('a', 100)).Length);
        Assert.Equal("", UbicacionIphone.LimpiarNombre(null));
    }

    // ---- la API ----

    private static async Task AgenteConectado(ModulosFactory f)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/salud") { Content = JsonContent.Create(new { hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>() }) };
        r.Headers.Add("X-Agente-Token", ApiFactory.Token);
        await f.CreateClient().SendAsync(r);
    }

    private static async Task Reautenticar(HttpClient c) =>
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);

    // lo que haría el agente: recoger la orden firmada y contestar
    private static async Task<JsonElement> RecogerYContestar(ModulosFactory f, bool ok, string resultado)
    {
        var pide = new HttpRequestMessage(HttpMethod.Get, "/api/sistema/agente/ordenes");
        pide.Headers.Add("X-Agente-Token", ApiFactory.Token);
        var lista = await (await f.CreateClient().SendAsync(pide)).Content.ReadFromJsonAsync<JsonElement>();
        var orden = lista.EnumerateArray().Last();
        var responde = new HttpRequestMessage(HttpMethod.Post, $"/api/sistema/agente/ordenes/{orden.GetProperty("id").GetInt32()}") { Content = JsonContent.Create(new { ok, resultado }) };
        responde.Headers.Add("X-Agente-Token", ApiFactory.Token);
        Assert.Equal(HttpStatusCode.NoContent, (await f.CreateClient().SendAsync(responde)).StatusCode);
        return orden;
    }

    [Fact]
    public async Task Sin_sesion_nada_y_con_sesion_empieza_apagada()
    {
        using var f = new ModulosFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/pc/ubicacion")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostAsJsonAsync("/api/pc/ubicacion/parar", new { })).StatusCode);
        var c = await f.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/pc/ubicacion");
        Assert.False(j.GetProperty("activo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("actual").ValueKind);
        // apagada: no se puede cambiar la ubicación
        await AgenteConectado(f);
        await Reautenticar(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 40.4, lon = -3.7, minutos = 60 })).StatusCode);
    }

    [Fact]
    public async Task Encender_y_cambiar_la_ubicacion_piden_confirmar_que_eres_tu()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await AgenteConectado(f);
        var r = await c.PutAsJsonAsync("/api/pc/ubicacion/ajustes", new { activo = true });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reautenticar").GetBoolean());
        await Reautenticar(c);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/pc/ubicacion/ajustes", new { activo = true })).StatusCode);
        Assert.Contains(f.Avisos.Enviados, e => e.StartsWith("telegram:📍 Ubicación del iPhone ENCENDIDA"));
    }

    [Fact]
    public async Task Fijar_valida_encola_una_orden_firmada_avisa_y_se_apunta_cuando_el_agente_confirma()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await AgenteConectado(f);
        await Reautenticar(c);
        await c.PutAsJsonAsync("/api/pc/ubicacion/ajustes", new { activo = true });

        // lo inválido no llega a existir
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 120, lon = 0, minutos = 60 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 10, lon = 0, minutos = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 10, lon = 0, minutos = 5000 })).StatusCode);
        using (var s = f.Services.CreateScope()) Assert.Equal(0, await s.ServiceProvider.GetRequiredService<AppDbContext>().OrdenesPc.CountAsync(o => o.Tipo == "ubicacion"));

        var r = await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 40.416829, lon = -3.703791, minutos = 90 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(f.Avisos.Enviados, e => e.StartsWith("telegram:📍 Orden al PC: poner el iPhone en 40.41683, -3.70379 durante 90 min"));

        // aún no confirmada: la API no dice que esté en marcha
        Assert.Equal(JsonValueKind.Null, (await c.GetFromJsonAsync<JsonElement>("/api/pc/ubicacion")).GetProperty("actual").ValueKind);

        var orden = await RecogerYContestar(f, true, "iPhone en 40.41683, -3.70379");
        Assert.Equal("ubicacion", orden.GetProperty("tipo").GetString());
        Assert.Equal("fijar:40.41683,-3.70379:90", orden.GetProperty("parametro").GetString());
        Assert.False(string.IsNullOrEmpty(orden.GetProperty("firma").GetString()));

        var j = await c.GetFromJsonAsync<JsonElement>("/api/pc/ubicacion");
        var actual = j.GetProperty("actual");
        Assert.Equal(40.41683, actual.GetProperty("lat").GetDouble(), 5);
        Assert.Equal("fijo", actual.GetProperty("modo").GetString());
        Assert.Equal(40.41683, j.GetProperty("ahora").GetProperty("lat").GetDouble(), 5);
        Assert.Equal(1, j.GetProperty("recientes").GetArrayLength());
    }

    [Fact]
    public async Task Si_el_agente_falla_no_se_apunta_nada()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await AgenteConectado(f);
        await Reautenticar(c);
        await c.PutAsJsonAsync("/api/pc/ubicacion/ajustes", new { activo = true });
        await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 10, lon = 10, minutos = 30 });
        await RecogerYContestar(f, false, "no veo el iPhone: conéctalo por cable");
        Assert.Equal(JsonValueKind.Null, (await c.GetFromJsonAsync<JsonElement>("/api/pc/ubicacion")).GetProperty("actual").ValueKind);
    }

    [Fact]
    public async Task Parar_se_puede_siempre_sin_confirmar_ni_estar_encendido_y_borra_la_simulacion()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await AgenteConectado(f);
        // sin encender y sin confirmar: parar vale (es lo seguro)
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/pc/ubicacion/parar", new { })).StatusCode);
        await RecogerYContestar(f, true, "ubicación real restaurada");

        await Reautenticar(c);
        await c.PutAsJsonAsync("/api/pc/ubicacion/ajustes", new { activo = true });
        await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 10, lon = 10, minutos = 30 });
        await RecogerYContestar(f, true, "iPhone en 10, 10");
        Assert.Equal(JsonValueKind.Object, (await c.GetFromJsonAsync<JsonElement>("/api/pc/ubicacion")).GetProperty("actual").ValueKind);

        await c.PostAsJsonAsync("/api/pc/ubicacion/parar", new { });
        await RecogerYContestar(f, true, "ubicación real restaurada");
        Assert.Equal(JsonValueKind.Null, (await c.GetFromJsonAsync<JsonElement>("/api/pc/ubicacion")).GetProperty("actual").ValueKind);
    }

    [Fact]
    public async Task Apagar_el_interruptor_con_una_simulacion_en_marcha_manda_parar()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await AgenteConectado(f);
        await Reautenticar(c);
        await c.PutAsJsonAsync("/api/pc/ubicacion/ajustes", new { activo = true });
        await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 10, lon = 10, minutos = 30 });
        await RecogerYContestar(f, true, "iPhone en 10, 10");
        await c.PutAsJsonAsync("/api/pc/ubicacion/ajustes", new { activo = false });
        var orden = await RecogerYContestar(f, true, "ubicación real restaurada");
        Assert.Equal("parar", orden.GetProperty("parametro").GetString());
    }

    [Fact]
    public async Task Caminar_pide_un_punto_de_partida_y_que_el_trayecto_quepa_en_el_tiempo()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await AgenteConectado(f);
        await Reautenticar(c);
        await c.PutAsJsonAsync("/api/pc/ubicacion/ajustes", new { activo = true });

        // sin punto de partida
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/pc/ubicacion/ir", new { lat = 40.42, lon = -3.7, minutos = 60, modo = "andar" })).StatusCode);

        await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 40.4168, lon = -3.7038, minutos = 120 });
        await RecogerYContestar(f, true, "iPhone en 40.4168, -3.7038");

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ubicacion/ir", new { lat = 40.42, lon = -3.7, minutos = 60, modo = "avion" })).StatusCode);
        // Madrid → Barcelona andando no cabe en 1 hora
        var largo = await c.PostAsJsonAsync("/api/pc/ubicacion/ir", new { lat = 41.3874, lon = 2.1686, minutos = 60, modo = "andar" });
        Assert.Equal(HttpStatusCode.BadRequest, largo.StatusCode);
        // 300 m andando caben de sobra
        var corto = await c.PostAsJsonAsync("/api/pc/ubicacion/ir", new { lat = 40.4195, lon = -3.7038, minutos = 30, modo = "ANDAR" });
        Assert.Equal(HttpStatusCode.OK, corto.StatusCode);
        var orden = await RecogerYContestar(f, true, "andar: 0.3 km");
        Assert.Equal("ir:40.4195,-3.7038:30:andar", orden.GetProperty("parametro").GetString());
        var actual = (await c.GetFromJsonAsync<JsonElement>("/api/pc/ubicacion")).GetProperty("actual");
        Assert.Equal("andar", actual.GetProperty("modo").GetString());
        Assert.Equal(40.4195, actual.GetProperty("destinoLat").GetDouble(), 5);
        Assert.InRange(actual.GetProperty("segundos").GetInt32(), 200, 260);
    }

    [Fact]
    public async Task Sin_el_agente_en_marcha_se_avisa_y_no_se_encola()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await Reautenticar(c);
        await c.PutAsJsonAsync("/api/pc/ubicacion/ajustes", new { activo = true });
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 10, lon = 10, minutos = 30 })).StatusCode);
        using var s = f.Services.CreateScope();
        Assert.Equal(0, await s.ServiceProvider.GetRequiredService<AppDbContext>().OrdenesPc.CountAsync(o => o.Tipo == "ubicacion"));
    }

    [Fact]
    public async Task Por_la_ruta_general_de_ordenes_no_se_puede_pedir_ubicacion()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await AgenteConectado(f);
        await Reautenticar(c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "ubicacion", parametro = "fijar:0,0:60" })).StatusCode);
    }

    [Fact]
    public async Task Los_sitios_guardados_se_guardan_se_limpian_y_se_borran()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ubicacion/lugares", new { nombre = "  ", lat = 1, lon = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ubicacion/lugares", new { nombre = "Mal", lat = 100, lon = 1 })).StatusCode);
        var r = await c.PostAsJsonAsync("/api/pc/ubicacion/lugares", new { nombre = " Torre Eiffel ", lat = 48.858400001, lon = 2.2945 });
        var lista = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Torre Eiffel", lista[0].GetProperty("nombre").GetString());
        Assert.Equal(48.8584, lista[0].GetProperty("lat").GetDouble(), 5);
        var id = lista[0].GetProperty("id").GetString();
        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>("/api/pc/ubicacion")).GetProperty("lugares").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/pc/ubicacion/lugares/inexistente")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync($"/api/pc/ubicacion/lugares/{id}")).StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/pc/ubicacion")).GetProperty("lugares").GetArrayLength());
    }

    [Fact]
    public async Task El_panico_restaura_la_ubicacion_si_habia_una_simulada()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await AgenteConectado(f);
        await Reautenticar(c);
        await c.PutAsJsonAsync("/api/pc/ubicacion/ajustes", new { activo = true });
        await c.PostAsJsonAsync("/api/pc/ubicacion/fijar", new { lat = 10, lon = 10, minutos = 30 });
        await RecogerYContestar(f, true, "iPhone en 10, 10");
        using var s = f.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        await UbicacionIphone.EnPanico(db, TimeProvider.System);
        var ultima = await db.OrdenesPc.OrderByDescending(o => o.Id).FirstAsync();
        Assert.Equal(("ubicacion", "parar"), (ultima.Tipo, ultima.Parametro));
    }
}
