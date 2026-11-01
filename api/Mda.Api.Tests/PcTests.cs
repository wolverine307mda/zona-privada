using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Data;
using Mda.Api.Pc;
using Mda.Api.Sistema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class ReglasSaludTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static SaludPc Salud(long libre = 400, long total = 1000, string salud = "Healthy", double? temp = 40, int? desgaste = 10, bool reinicio = false) =>
        new(Ahora, [new DiscoSalud("0", "Samsung SSD", salud, "OK", temp, desgaste, 0)], [new VolumenSalud("C:", libre, total)], reinicio);

    [Fact]
    public void Un_pc_sano_no_da_avisos() => Assert.Empty(ReglasSalud.Evaluar(Salud(), null, Ahora, 3));

    [Theory]
    [InlineData(140, "disco-libre:C:", "🟠")]  // 14 % libre: aviso
    [InlineData(70, "disco-libre:C:", "🔴")]   // 7 % libre: urgente
    public void El_espacio_libre_avisa_a_los_15_y_es_urgente_a_los_8(long libre, string id, string marca)
    {
        var r = ReglasSalud.Evaluar(Salud(libre: libre), null, Ahora, 3);
        Assert.StartsWith(marca, r[id]);
    }

    [Fact]
    public void Un_disco_enfermo_caliente_o_gastado_avisa()
    {
        Assert.Contains("disco-salud:0", ReglasSalud.Evaluar(Salud(salud: "Warning"), null, Ahora, 3).Keys);
        Assert.Contains("disco-temp:0", ReglasSalud.Evaluar(Salud(temp: 72), null, Ahora, 3).Keys);
        Assert.Contains("disco-desgaste:0", ReglasSalud.Evaluar(Salud(desgaste: 93), null, Ahora, 3).Keys);
        Assert.Empty(ReglasSalud.Evaluar(Salud(temp: null, desgaste: null), null, Ahora, 3)); // sin sensor no falla
    }

    [Fact]
    public void El_reinicio_pendiente_solo_avisa_pasados_7_dias_y_el_encendido_largo_pasados_45()
    {
        Assert.Empty(ReglasSalud.Evaluar(Salud(reinicio: true), Ahora.AddDays(-3), Ahora, 3));
        Assert.Contains("reinicio", ReglasSalud.Evaluar(Salud(reinicio: true), Ahora.AddDays(-8), Ahora, 3).Keys);
        Assert.Contains("encendido", ReglasSalud.Evaluar(Salud(), null, Ahora, 50).Keys);
    }

    [Fact]
    public void El_texto_de_resuelto_nombra_lo_que_se_arreglo()
    {
        Assert.Contains("C:", ReglasSalud.Resuelto("disco-libre:C:"));
        Assert.Contains("reiniciado", ReglasSalud.Resuelto("reinicio"));
    }
}

public class PcTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= factory.Dentro();

    private HttpRequestMessage Agente(HttpMethod m, string ruta, object? cuerpo = null)
    {
        var r = new HttpRequestMessage(m, ruta);
        r.Headers.Add("X-Agente-Token", ApiFactory.Token);
        if (cuerpo is not null) r.Content = JsonContent.Create(cuerpo);
        return r;
    }

    private async Task AgenteConectado() =>
        await factory.CreateClient().SendAsync(Agente(HttpMethod.Post, "/api/sistema/agente/salud", new
        {
            hora = DateTimeOffset.UtcNow, reinicioPendiente = false,
            discos = new[] { new { nombre = "0", modelo = "SSD", salud = "Healthy", estado = "OK", temperaturaC = 41.0, desgastePct = 5, erroresLectura = 0 } },
            volumenes = new[] { new { nombre = "C:", libreBytes = 100L << 30, totalBytes = 500L << 30 } },
        }));

    private async Task Reautenticar(HttpClient c) =>
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);

    [Fact]
    public async Task La_pantalla_y_las_ordenes_piden_sesion()
    {
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/pc")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "bloquear" })).StatusCode);
    }

    [Fact]
    public async Task Una_orden_pide_el_agente_en_marcha_la_contrasena_otra_vez_y_una_orden_de_la_lista()
    {
        var c = await factory.Dentro(); // sesión propia: la «contraseña otra vez» de otras pruebas no cuenta aquí
        await AgenteConectado();
        // sin reautenticar: 403 que le dice a la app que pida la contraseña
        var sin = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "bloquear" });
        Assert.Equal(HttpStatusCode.Forbidden, sin.StatusCode);
        Assert.True((await sin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reautenticar").GetBoolean());
        // contraseña mala
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = "mala-mala-mala" })).StatusCode);

        await Reautenticar(c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "formatear" })).StatusCode);
        var ok = await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "bloquear" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains(factory.Avisos.Enviados, e => e.StartsWith("telegram:💻 Orden al PC: bloquear la pantalla"));
    }

    [Fact]
    public async Task El_agente_recibe_la_orden_firmada_y_cuenta_el_resultado()
    {
        var c = await Dentro();
        await AgenteConectado();
        await Reautenticar(c);
        var orden = await (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "despierto", parametro = "on" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = orden.GetProperty("id").GetInt32();

        // sin token, o llegando por el proxy, el agente no existe
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/sistema/agente/ordenes") { Headers = { { "X-Agente-Token", "otro-otro-otro-otro-otro-otro-otro-1234" } } })).StatusCode);
        var porProxy = Agente(HttpMethod.Get, "/api/sistema/agente/ordenes");
        porProxy.Headers.Add("X-Original-For", "203.0.113.5");
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().SendAsync(porProxy)).StatusCode);

        var res = await factory.CreateClient().SendAsync(Agente(HttpMethod.Get, "/api/sistema/agente/ordenes"));
        var lista = await res.Content.ReadFromJsonAsync<JsonElement>();
        var recibida = lista.EnumerateArray().Single(o => o.GetProperty("id").GetInt32() == id);
        Assert.Equal("despierto", recibida.GetProperty("tipo").GetString());
        // la firma la puede comprobar quien tenga el secreto compartido, y no vale si cambian la orden
        var o = new OrdenPc { Id = id, Tipo = "despierto", Parametro = "on", Caduca = DateTimeOffset.FromUnixTimeSeconds(recibida.GetProperty("caduca").GetInt64()) };
        Assert.Equal(OrdenesPc.Firma(ApiFactory.Token, o), recibida.GetProperty("firma").GetString());
        Assert.NotEqual(recibida.GetProperty("firma").GetString(), OrdenesPc.Firma(ApiFactory.Token, new OrdenPc { Id = id, Tipo = "apagar", Parametro = "on", Caduca = o.Caduca }));
        Assert.NotEqual(recibida.GetProperty("firma").GetString(), OrdenesPc.Firma("otro-secreto-otro-secreto-otro-secreto", o));

        // ya no se vuelve a mandar y el resultado queda guardado
        var otra = await (await factory.CreateClient().SendAsync(Agente(HttpMethod.Get, "/api/sistema/agente/ordenes"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(otra.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().SendAsync(Agente(HttpMethod.Post, $"/api/sistema/agente/ordenes/{id}", new { ok = true, resultado = "hecho" }))).StatusCode);
        var estado = await c.GetFromJsonAsync<JsonElement>($"/api/pc/ordenes/{id}");
        Assert.Equal("hecha", estado.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Una_orden_que_nadie_recoge_caduca()
    {
        var c = await Dentro();
        await AgenteConectado();
        await Reautenticar(c);
        var id = (await (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = "suspender" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.OrdenesPc.FindAsync(id))!.Caduca = DateTimeOffset.UtcNow.AddSeconds(-5);
            await db.SaveChangesAsync();
        }
        var lista = await (await factory.CreateClient().SendAsync(Agente(HttpMethod.Get, "/api/sistema/agente/ordenes"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(lista.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal("caducada", (await c.GetFromJsonAsync<JsonElement>($"/api/pc/ordenes/{id}")).GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Cada_dia_queda_una_foto_de_la_salud_y_se_guardan_solo_90()
    {
        var c = await factory.Dentro();
        await AgenteConectado();
        await AgenteConectado(); // el mismo día otra vez: se sustituye, no se acumula
        var h = await c.GetFromJsonAsync<JsonElement>("/api/pc/historial");
        var hoy = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Assert.Single(h.EnumerateArray(), d => d.GetProperty("fecha").GetString() == hoy);
        var dia = h.EnumerateArray().Single(d => d.GetProperty("fecha").GetString() == hoy);
        Assert.Equal("C:", dia.GetProperty("volumenes")[0].GetProperty("nombre").GetString());
        Assert.Equal(100L << 30, dia.GetProperty("volumenes")[0].GetProperty("libreBytes").GetInt64());
        Assert.Equal(5, dia.GetProperty("discos")[0].GetProperty("desgastePct").GetInt32());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var s = new SaludPc(DateTimeOffset.UtcNow, [], [new VolumenSalud("D:", 1, 2)], false);
        for (var i = 1; i <= 95; i++) await HistorialSalud.Guardar(db, s, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-95 + i));
        Assert.True(db.SaludDias.Count() <= 91);
        Assert.False(db.SaludDias.Any(d => d.Fecha < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-HistorialSalud.Dias)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/pc/historial")).StatusCode);
    }

    [Fact]
    public async Task La_salud_que_manda_el_agente_se_ve_en_la_pantalla()
    {
        var c = await Dentro();
        await AgenteConectado();
        var pc = await c.GetFromJsonAsync<JsonElement>("/api/pc");
        Assert.True(pc.GetProperty("agenteConectado").GetBoolean());
        Assert.Equal("C:", pc.GetProperty("salud").GetProperty("volumenes")[0].GetProperty("nombre").GetString());
        // y una salud enorme o rota se rechaza
        var rota = Agente(HttpMethod.Post, "/api/sistema/agente/salud");
        rota.Content = new StringContent("no es json", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().SendAsync(rota)).StatusCode);
    }
}

public class AvisosSaludTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Avisa_una_vez_al_dia_de_cada_problema_y_dice_cuando_se_arregla()
    {
        factory.Avisos.Enviados.Clear();
        var reloj = new RelojFijo(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var salud = new EstadoSalud(reloj);
        var servicio = new AvisosSalud(factory.Services.GetRequiredService<IServiceScopeFactory>(), salud, new EstadoSistema(), factory.Avisos,
            factory.Services.GetRequiredService<Zona>(), reloj, NullLogger<AvisosSalud>.Instance);

        salud.Recibir(new SaludPc(reloj.Ahora, [], [new VolumenSalud("C:", 50L << 30, 1000L << 30)], false)); // 5 % libre
        await servicio.Revisar(default);
        Assert.Single(factory.Avisos.Enviados, e => e.StartsWith("telegram:💻 🔴 Disco C: casi lleno"));
        await servicio.Revisar(default);
        Assert.Single(factory.Avisos.Enviados, e => e.StartsWith("telegram:💻 🔴 Disco C: casi lleno")); // el mismo día, una vez

        reloj.Ahora = reloj.Ahora.AddDays(1);
        salud.Recibir(new SaludPc(reloj.Ahora, [], [new VolumenSalud("C:", 50L << 30, 1000L << 30)], false));
        await servicio.Revisar(default);
        Assert.Equal(2, factory.Avisos.Enviados.Count(e => e.StartsWith("telegram:💻 🔴 Disco C: casi lleno"))); // otro día, otra vez

        salud.Recibir(new SaludPc(reloj.Ahora, [], [new VolumenSalud("C:", 600L << 30, 1000L << 30)], false));
        await servicio.Revisar(default);
        Assert.Contains(factory.Avisos.Enviados, e => e.Contains("✅ Ya hay espacio en el disco C:"));
    }

    [Fact]
    public async Task Sin_datos_recientes_del_agente_no_avisa_de_nada()
    {
        factory.Avisos.Enviados.Clear();
        var reloj = new RelojFijo(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var salud = new EstadoSalud(reloj);
        salud.Recibir(new SaludPc(reloj.Ahora, [], [new VolumenSalud("C:", 1, 1000)], false));
        reloj.Ahora = reloj.Ahora.AddHours(2); // la lectura ya es vieja: el agente se ha callado
        var servicio = new AvisosSalud(factory.Services.GetRequiredService<IServiceScopeFactory>(), salud, new EstadoSistema(), factory.Avisos,
            factory.Services.GetRequiredService<Zona>(), reloj, NullLogger<AvisosSalud>.Instance);
        await servicio.Revisar(default);
        Assert.Empty(factory.Avisos.Enviados);
    }
}

public class PasskeysTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Gestionar_passkeys_pide_sesion()
    {
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/passkeys")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsync("/api/passkeys/opciones", null)).StatusCode);
    }

    [Fact]
    public async Task El_reto_de_entrada_se_pide_sin_sesion_y_un_passkey_falso_no_entra()
    {
        var anonimo = factory.CreateClient();
        var opciones = await anonimo.PostAsync("/api/auth/passkey/entrada/opciones", null);
        Assert.Equal(HttpStatusCode.OK, opciones.StatusCode);
        Assert.Contains("challenge", await opciones.Content.ReadAsStringAsync());
        var falso = await anonimo.PostAsJsonAsync("/api/auth/passkey/entrada", new { credencial = new { id = "AAAA", type = "public-key", response = new { } } });
        Assert.NotEqual(HttpStatusCode.NoContent, falso.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/sesion")).StatusCode);
    }

    [Fact]
    public async Task Con_sesion_se_piden_las_opciones_para_registrar_uno_y_la_lista_empieza_vacia()
    {
        var c = await factory.Dentro();
        var opciones = await c.PostAsync("/api/passkeys/opciones", null);
        Assert.Equal(HttpStatusCode.OK, opciones.StatusCode);
        var j = JsonDocument.Parse(await opciones.Content.ReadAsStringAsync()).RootElement;
        Assert.True(j.TryGetProperty("challenge", out _));
        Assert.True(j.GetProperty("rp").TryGetProperty("name", out _) || j.GetProperty("rp").TryGetProperty("id", out _));
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/passkeys")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/passkeys/no-existe")).StatusCode);
    }
}
