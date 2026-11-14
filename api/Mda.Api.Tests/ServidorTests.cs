using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Pc;
using Mda.Api.Seguridad;
using Mda.Api.Servidor;
using Mda.Api.Sistema;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mda.Api.Tests;

public class ServidorUnitTests
{
    [Fact]
    public void El_paquete_magico_son_seis_FF_y_la_mac_dieciseis_veces()
    {
        var p = WakeOnLan.Paquete("aa-bb-cc-00-11-22");
        Assert.Equal(102, p.Length);
        Assert.All(p[..6], b => Assert.Equal(0xFF, b));
        for (var i = 0; i < 16; i++) Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0x00, 0x11, 0x22 }, p[(6 + i * 6)..(12 + i * 6)]);
    }

    [Theory]
    [InlineData("AA:BB:CC:00:11:22", true)] [InlineData("aa-bb-cc-00-11-22", true)] [InlineData(" AA:BB:CC:00:11:22 ", true)]
    [InlineData("AA:BB:CC:00:11", false)] [InlineData("AA:BB:CC:00:11:ZZ", false)] [InlineData("", false)] [InlineData(null, false)]
    public void Solo_vale_una_mac_bien_escrita(string? mac, bool esperado) => Assert.Equal(esperado, WakeOnLan.MacValida(mac));

    [Fact]
    public void Una_mac_mala_no_genera_paquete() => Assert.Throws<ArgumentException>(() => WakeOnLan.Paquete("123"));

    private static AprobacionLogin Nueva(string codigo = "47", int minutos = 3) => new() { Id = "x", Codigo = codigo, SecretoHash = Aprobaciones.Hash("secreto"), Creada = DateTimeOffset.UtcNow, Caduca = DateTimeOffset.UtcNow.AddMinutes(minutos) };

    [Fact]
    public void Con_el_codigo_bueno_se_aprueba()
    {
        var a = Nueva();
        Assert.True(Aprobaciones.Aprobar(a, " 47 ", DateTimeOffset.UtcNow).Ok);
        Assert.Equal("aprobada", a.Estado);
    }

    [Fact]
    public void Tres_codigos_malos_deniegan_la_peticion_y_ya_no_vale_ni_el_bueno()
    {
        var a = Nueva();
        Assert.False(Aprobaciones.Aprobar(a, "11", DateTimeOffset.UtcNow).Ok);
        Assert.False(Aprobaciones.Aprobar(a, "", DateTimeOffset.UtcNow).Ok);
        Assert.Equal("pendiente", a.Estado);
        Assert.False(Aprobaciones.Aprobar(a, "12", DateTimeOffset.UtcNow).Ok);
        Assert.Equal("denegada", a.Estado);
        Assert.False(Aprobaciones.Aprobar(a, "47", DateTimeOffset.UtcNow).Ok);
    }

    [Fact]
    public void Una_peticion_caducada_no_se_puede_aprobar()
    {
        var a = Nueva(minutos: -1);
        Assert.Equal("caducada", Aprobaciones.EstadoActual(a, DateTimeOffset.UtcNow));
        Assert.False(Aprobaciones.Aprobar(a, "47", DateTimeOffset.UtcNow).Ok);
    }

    [Fact]
    public void El_secreto_solo_lo_conoce_quien_pidio_la_entrada()
    {
        var a = Nueva();
        Assert.True(Aprobaciones.SecretoValido(a, "secreto"));
        Assert.False(Aprobaciones.SecretoValido(a, "otro"));
        Assert.False(Aprobaciones.SecretoValido(a, null));
        Assert.DoesNotContain("secreto", a.SecretoHash);   // en la base solo queda la huella
    }

    [Fact]
    public void El_codigo_son_dos_cifras()
    {
        for (var i = 0; i < 200; i++) Assert.Matches("^[1-9][0-9]$", Aprobaciones.NuevoCodigo());
    }

    private static RunbookEntrada Rb(params PasoRunbook[] pasos) => new("Mantenimiento", "", pasos.ToList());

    [Fact]
    public void Un_runbook_bien_hecho_no_da_errores() =>
        Assert.Empty(Runbooks.Validar(Rb(new PasoRunbook("comprobar", Valor: "base"), new PasoRunbook("espera", Segundos: 5), new PasoRunbook("aviso", Texto: "hecho"), new PasoRunbook("casa", Valor: "salida"),
            new PasoRunbook("despertar"), new PasoRunbook("despertar", Valor: "PC"), new PasoRunbook("escena", Valor: "Cine"), new PasoRunbook("orden", Valor: "bloquear"), new PasoRunbook("orden", Valor: "apagar", Aprobar: true))));

    [Fact]
    public void Las_ordenes_que_cortan_lo_que_haces_tienen_que_pedir_permiso()
    {
        foreach (var o in Runbooks.OrdenesDelicadas)
        {
            Assert.NotEmpty(Runbooks.Validar(Rb(new PasoRunbook("orden", Valor: o))));
            Assert.Empty(Runbooks.Validar(Rb(new PasoRunbook("orden", Valor: o, Aprobar: true))));
        }
    }

    [Fact]
    public void Se_rechaza_lo_que_no_existe_o_se_pasa_de_largo()
    {
        Assert.Contains("pasos", Runbooks.Validar(new("x", null, [])).Keys);
        Assert.Contains("pasos", Runbooks.Validar(new("x", null, null)).Keys);
        Assert.Contains("nombre", Runbooks.Validar(new("", null, [new PasoRunbook("aviso", Texto: "a")])).Keys);
        Assert.NotEmpty(Runbooks.Validar(Rb(new PasoRunbook("formatear"))));
        Assert.NotEmpty(Runbooks.Validar(Rb(new PasoRunbook("espera", Segundos: 0))));
        Assert.NotEmpty(Runbooks.Validar(Rb(new PasoRunbook("espera", Segundos: Runbooks.MaxEspera + 1))));
        Assert.NotEmpty(Runbooks.Validar(Rb(new PasoRunbook("comprobar", Valor: "todo"))));
        Assert.NotEmpty(Runbooks.Validar(Rb(new PasoRunbook("casa", Valor: "duermo"))));
        Assert.NotEmpty(Runbooks.Validar(Rb(new PasoRunbook("orden", Valor: "rm -rf"))));
        Assert.NotEmpty(Runbooks.Validar(Rb(new PasoRunbook("aviso", Texto: new string('a', 301)))));
        Assert.NotEmpty(Runbooks.Validar(new("x", null, Enumerable.Range(0, Runbooks.MaxPasos + 1).Select(_ => new PasoRunbook("aviso", Texto: "a")).ToList())));
    }

    [Fact]
    public void Los_botones_de_la_guardia_llevan_los_runbooks_y_las_acciones_de_siempre()
    {
        var json = JsonSerializer.Serialize(GuardiaServicio.Teclado("direccion", [(1, "Comprobar todo"), (2, "B"), (3, "C"), (4, "D")]));
        Assert.Contains("rb:run:1", json); Assert.Contains("rb:run:3", json);
        Assert.DoesNotContain("rb:run:4", json);            // como mucho tres, para que el mensaje no sea un muro
        Assert.Contains("gd:sil:direccion", json); Assert.Contains("gd:ok", json); Assert.Contains("gd:panico", json);
        foreach (var d in JsonDocument.Parse(json).RootElement.GetProperty("inline_keyboard").EnumerateArray().SelectMany(f => f.EnumerateArray()))
            Assert.True(d.GetProperty("callback_data").GetString()!.Length <= 64);    // el máximo de Telegram
    }

    [Fact]
    public void La_linea_junta_lo_apuntado_los_accesos_y_las_ordenes_y_se_filtra()
    {
        var ahora = DateTimeOffset.UtcNow;
        var eventos = new[] { new EventoServidor { Hora = ahora.AddMinutes(-5), Tipo = "runbook", Titulo = "Runbook «Comprobar todo» lanzado" }, new EventoServidor { Hora = ahora.AddMinutes(-60), Tipo = "guardia", Titulo = "Modo guardia activado" } };
        var accesos = new[] { new Acceso { Fecha = ahora.AddMinutes(-1), Ip = "1.2.3.4", Exito = false, Motivo = "contraseña" }, new Acceso { Fecha = ahora.AddMinutes(-30), Ip = "5.6.7.8", Exito = true, Motivo = "ok" } };
        var ordenes = new[] { new OrdenPc { Tipo = "bloquear", Creada = ahora.AddMinutes(-10), Desde = "App", Estado = "hecha" } };

        var todo = ServidorLog.Componer(eventos, accesos, ordenes, [], null, 100);
        Assert.Equal(5, todo.Count);
        Assert.True(todo.Zip(todo.Skip(1)).All(p => p.First.Hora >= p.Second.Hora));      // del más nuevo al más viejo
        Assert.Equal("aviso", todo[0].Gravedad);                                            // el intento fallido
        Assert.Equal(["acceso", "acceso"], ServidorLog.Componer(eventos, accesos, ordenes, ["acceso"], null, 100).Select(x => x.Tipo));
        Assert.Single(ServidorLog.Componer(eventos, accesos, ordenes, [], "guardia", 100));
        Assert.Equal(2, ServidorLog.Componer(eventos, accesos, ordenes, [], null, 2).Count);
    }
}

public class ServidorTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= factory.Dentro();
    private HttpClient Anonimo() => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    // una sesión recién abierta (el permiso de Face ID es por sesión y dura 2 minutos: la compartida puede tenerlo ya)
    private async Task<HttpClient> Sesion()
    {
        var c = Anonimo();
        (await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).EnsureSuccessStatusCode();
        return c;
    }

    private static async Task Face(HttpClient c) =>
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);

    // ---- entrar aprobando desde el iPhone ----

    [Fact]
    public async Task El_ordenador_nuevo_entra_cuando_el_iPhone_aprueba_con_Face_ID_y_el_codigo()
    {
        var iphone = await Sesion();
        var nuevo = Anonimo();
        Assert.Equal(HttpStatusCode.Unauthorized, (await nuevo.GetAsync("/api/sesion")).StatusCode);

        var p = await (await nuevo.PostAsync("/api/auth/aprobacion", null)).Content.ReadFromJsonAsync<JsonElement>();
        var (id, codigo, secreto) = (p.GetProperty("id").GetString()!, p.GetProperty("codigo").GetString()!, p.GetProperty("secreto").GetString()!);
        Assert.Matches("^[1-9][0-9]$", codigo);
        async Task<string> Estado() => (await (await nuevo.PostAsJsonAsync($"/api/auth/aprobacion/{id}/estado", new { secreto })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estado").GetString()!;
        Assert.Equal("pendiente", await Estado());
        // sin aprobar no entra
        Assert.Equal(HttpStatusCode.Conflict, (await nuevo.PostAsJsonAsync($"/api/auth/aprobacion/{id}/entrar", new { secreto })).StatusCode);

        // el iPhone la ve (sin el código) y la aprueba: primero Face ID, luego el código
        var pendientes = (await iphone.GetFromJsonAsync<JsonElement>("/api/auth/aprobacion/pendientes")).EnumerateArray().ToList();
        var mia = pendientes.Single(x => x.GetProperty("id").GetString() == id);
        Assert.DoesNotContain("codigo", mia.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await iphone.PostAsJsonAsync($"/api/auth/aprobacion/{id}/aprobar", new { codigo })).StatusCode);
        await Face(iphone);
        var mal = await iphone.PostAsJsonAsync($"/api/auth/aprobacion/{id}/aprobar", new { codigo = codigo == "10" ? "11" : "10" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mal.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await iphone.PostAsJsonAsync($"/api/auth/aprobacion/{id}/aprobar", new { codigo })).StatusCode);
        Assert.Equal("aprobada", await Estado());

        // con otro secreto no se recoge la sesión; con el suyo, sí, y una sola vez
        Assert.Equal(HttpStatusCode.NotFound, (await nuevo.PostAsJsonAsync($"/api/auth/aprobacion/{id}/entrar", new { secreto = "otro" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await nuevo.PostAsJsonAsync($"/api/auth/aprobacion/{id}/entrar", new { secreto })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await nuevo.GetAsync("/api/sesion")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Anonimo().PostAsJsonAsync($"/api/auth/aprobacion/{id}/entrar", new { secreto })).StatusCode);
    }

    [Fact]
    public async Task Denegar_la_peticion_impide_entrar_y_la_anonima_no_enseña_nada_a_quien_no_tiene_el_secreto()
    {
        var iphone = await Dentro();
        var nuevo = Anonimo();
        var p = await (await nuevo.PostAsync("/api/auth/aprobacion", null)).Content.ReadFromJsonAsync<JsonElement>();
        var (id, secreto) = (p.GetProperty("id").GetString()!, p.GetProperty("secreto").GetString()!);
        Assert.Equal(HttpStatusCode.NotFound, (await nuevo.PostAsJsonAsync($"/api/auth/aprobacion/{id}/estado", new { secreto = "adivinado" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await iphone.PostAsync($"/api/auth/aprobacion/{id}/denegar", null)).StatusCode);
        var tras = await nuevo.PostAsJsonAsync($"/api/auth/aprobacion/{id}/estado", new { secreto });
        Assert.True(tras.IsSuccessStatusCode, $"{(int)tras.StatusCode} {await tras.Content.ReadAsStringAsync()}");
        Assert.Equal("denegada", (await tras.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await nuevo.PostAsJsonAsync($"/api/auth/aprobacion/{id}/entrar", new { secreto })).StatusCode);
        // aprobar es cosa del iPhone con sesión
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonimo().GetAsync("/api/auth/aprobacion/pendientes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonimo().PostAsJsonAsync($"/api/auth/aprobacion/{id}/aprobar", new { codigo = "10" })).StatusCode);
    }

    [Fact]
    public async Task No_puede_haber_mas_de_tres_peticiones_pendientes()
    {
        var iphone = await Dentro();
        foreach (var x in (await iphone.GetFromJsonAsync<JsonElement>("/api/auth/aprobacion/pendientes")).EnumerateArray())
            await iphone.PostAsync($"/api/auth/aprobacion/{x.GetProperty("id").GetString()}/denegar", null);
        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) codigos.Add((await Anonimo().PostAsync("/api/auth/aprobacion", null)).StatusCode);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], codigos);
        foreach (var x in (await iphone.GetFromJsonAsync<JsonElement>("/api/auth/aprobacion/pendientes")).EnumerateArray())
            await iphone.PostAsync($"/api/auth/aprobacion/{x.GetProperty("id").GetString()}/denegar", null);
    }

    // ---- runbooks ----

    private static async Task<int> CrearRunbook(HttpClient c, string nombre, object[] pasos, bool guardia = false)
    {
        var r = await c.PostAsJsonAsync("/api/servidor/runbooks", new { nombre, descripcion = "", pasos, guardia });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static async Task<JsonElement> Esperar(HttpClient c, int ejecucion, params string[] estados)
    {
        for (var i = 0; i < 100; i++)
        {
            var e = await c.GetFromJsonAsync<JsonElement>($"/api/servidor/ejecuciones/{ejecucion}");
            if (estados.Contains(e.GetProperty("estado").GetString())) return e;
            await Task.Delay(100);
        }
        throw new TimeoutException($"La ejecución {ejecucion} no llegó a {string.Join("/", estados)}.");
    }

    private static async Task<int> Ejecutar(HttpClient c, int runbook)
    {
        var r = await c.PostAsync($"/api/servidor/runbooks/{runbook}/ejecutar", null);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ejecucion").GetInt32();
    }

    [Fact]
    public async Task Hay_un_runbook_de_partida_y_los_mal_hechos_se_rechazan()
    {
        var c = await Dentro();
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/servidor/runbooks");
        Assert.Contains(lista.EnumerateArray(), r => r.GetProperty("nombre").GetString() == "Comprobar todo" && r.GetProperty("guardia").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/servidor/runbooks", new { nombre = "Malo", pasos = new object[] { new { tipo = "orden", valor = "apagar" } } })).StatusCode);
        await CrearRunbook(c, "Duplicado", [new { tipo = "espera", segundos = 1 }]);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/servidor/runbooks", new { nombre = "duplicado", pasos = new object[] { new { tipo = "espera", segundos = 1 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonimo().GetAsync("/api/servidor/runbooks")).StatusCode);
    }

    [Fact]
    public async Task Un_runbook_se_ejecuta_paso_a_paso_y_queda_su_historial()
    {
        var c = await Dentro();
        var id = await CrearRunbook(c, "Ensayo ok", [new { tipo = "comprobar", valor = "base" }, new { tipo = "espera", segundos = 1 }]);
        var ej = await Ejecutar(c, id);
        var fin = await Esperar(c, ej, "ok", "error");
        Assert.Equal("ok", fin.GetProperty("estado").GetString());
        var pasos = fin.GetProperty("pasos").EnumerateArray().ToList();
        Assert.All(pasos, p => Assert.Equal("ok", p.GetProperty("estado").GetString()));
        Assert.Contains("base de datos", pasos[0].GetProperty("salida").GetString());
        Assert.Contains((await c.GetFromJsonAsync<JsonElement>("/api/servidor/ejecuciones")).EnumerateArray(), e => e.GetProperty("id").GetInt32() == ej);
        // y aparece en la línea de tiempo
        var linea = await c.GetFromJsonAsync<JsonElement>("/api/servidor/linea?tipos=runbook");
        Assert.Contains(linea.GetProperty("sucesos").EnumerateArray(), s => s.GetProperty("titulo").GetString()!.Contains("Ensayo ok"));
    }

    [Fact]
    public async Task Un_paso_que_falla_para_el_runbook_salvo_que_se_diga_que_continue()
    {
        var c = await Dentro();
        // el agente del PC no está conectado en las pruebas: «comprobar agente» falla
        var para = await CrearRunbook(c, "Ensayo para", [new { tipo = "comprobar", valor = "agente" }, new { tipo = "espera", segundos = 1 }]);
        var f1 = await Esperar(c, await Ejecutar(c, para), "ok", "error");
        Assert.Equal("error", f1.GetProperty("estado").GetString());
        Assert.Equal(["error", "omitido"], f1.GetProperty("pasos").EnumerateArray().Select(p => p.GetProperty("estado").GetString()));

        var sigue = await CrearRunbook(c, "Ensayo sigue", [new { tipo = "comprobar", valor = "agente", continuar = true }, new { tipo = "espera", segundos = 1 }]);
        var f2 = await Esperar(c, await Ejecutar(c, sigue), "ok", "error");
        Assert.Equal("error", f2.GetProperty("estado").GetString());     // acaba con fallos, pero siguió
        Assert.Equal(["error", "ok"], f2.GetProperty("pasos").EnumerateArray().Select(p => p.GetProperty("estado").GetString()));
    }

    [Fact]
    public async Task Un_paso_con_permiso_espera_y_solo_sigue_con_Face_ID_o_se_cancela()
    {
        var c = await Dentro();
        var id = await CrearRunbook(c, "Ensayo permiso", [new { tipo = "espera", segundos = 1 }, new { tipo = "orden", valor = "bloquear", aprobar = true }]);
        var ej = await Ejecutar(c, id);
        var espera = await Esperar(c, ej, "esperando");
        Assert.Equal("esperando", espera.GetProperty("pasos")[1].GetProperty("estado").GetString());
        // ya hay una en marcha de ese runbook
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/servidor/runbooks/{id}/ejecutar", null)).StatusCode);

        // sin Face ID no pasa; con él sigue (y la orden falla porque no hay agente conectado)
        var sin = await c.PostAsync($"/api/servidor/ejecuciones/{ej}/aprobar", null);
        Assert.True(sin.StatusCode == HttpStatusCode.Forbidden || sin.StatusCode == HttpStatusCode.Conflict);
        await Face(c);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/servidor/ejecuciones/{ej}/aprobar", null)).StatusCode);
        var fin = await Esperar(c, ej, "ok", "error");
        Assert.Equal("error", fin.GetProperty("estado").GetString());
        Assert.Contains("agente", fin.GetProperty("pasos")[1].GetProperty("salida").GetString());

        // cancelar mientras espera
        var ej2 = await Ejecutar(c, id);
        await Esperar(c, ej2, "esperando");
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/servidor/ejecuciones/{ej2}/cancelar", null)).StatusCode);
        var cancelado = await Esperar(c, ej2, "cancelado");
        Assert.Equal("omitido", cancelado.GetProperty("pasos")[1].GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/servidor/ejecuciones/{ej2}/cancelar", null)).StatusCode);
    }

    // ---- guardia ----

    [Fact]
    public async Task La_guardia_se_activa_y_se_apaga_y_queda_apuntado()
    {
        var c = await Dentro();
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/servidor/guardia")).GetProperty("activa").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/servidor/guardia", new { activa = true })).StatusCode);
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/servidor/guardia")).GetProperty("activa").GetBoolean());
        // sin Telegram vinculado no hay a quién mandar el simulacro
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/servidor/guardia/probar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/servidor/guardia", new { activa = false })).StatusCode);
        var linea = await c.GetFromJsonAsync<JsonElement>("/api/servidor/linea?tipos=guardia");
        var titulos = linea.GetProperty("sucesos").EnumerateArray().Select(s => s.GetProperty("titulo").GetString()).ToList();
        Assert.Contains("Modo guardia activado", titulos); Assert.Contains("Modo guardia desactivado", titulos);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonimo().PutAsJsonAsync("/api/servidor/guardia", new { activa = true })).StatusCode);
    }

    // ---- línea de tiempo ----

    [Fact]
    public async Task La_linea_pide_sesion_acota_el_rango_y_filtra_por_tipo()
    {
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonimo().GetAsync("/api/servidor/linea")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/servidor/linea?desde={DateTimeOffset.UtcNow.AddDays(-200):O}")).StatusCode);
        var todo = await c.GetFromJsonAsync<JsonElement>("/api/servidor/linea");
        Assert.True(todo.GetProperty("sucesos").GetArrayLength() > 0);
        var soloAccesos = await c.GetFromJsonAsync<JsonElement>("/api/servidor/linea?tipos=acceso");
        Assert.All(soloAccesos.GetProperty("sucesos").EnumerateArray(), s => Assert.Equal("acceso", s.GetProperty("tipo").GetString()));
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/servidor/linea?q=zzz-no-existe-zzz")).GetProperty("sucesos").GetArrayLength());
    }

    // ---- despertar el PC principal ----

    [Fact]
    public async Task El_pc_principal_se_elige_entre_los_que_tienen_mac_y_se_despierta()
    {
        var c = await Dentro();
        Assert.Equal(JsonValueKind.Null, (await c.GetFromJsonAsync<JsonElement>("/api/pc/principal")).GetProperty("principal").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/pc/principal/despertar", null)).StatusCode);   // sin elegir

        async Task<int> Crear(string nombre, string? mac) => (await (await c.PostAsJsonAsync("/api/red", new { nombre, host = "192.168.1.77", puerto = 3389, icono = "pc", mac })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var sinMac = await Crear("PC sin MAC", null);
        var conMac = await Crear("PC del escritorio", "AA:BB:CC:00:11:33");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/pc/principal", new { id = sinMac })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/pc/principal", new { id = 99999 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/pc/principal", new { id = conMac })).StatusCode);
        var p = (await c.GetFromJsonAsync<JsonElement>("/api/pc/principal")).GetProperty("principal");
        Assert.Equal("PC del escritorio", p.GetProperty("nombre").GetString());
        Assert.True(p.GetProperty("conMac").GetBoolean());

        // el paquete sale por la red del equipo (si la hay); en cualquier caso se contesta con un mensaje claro
        var r = await c.PostAsync("/api/pc/principal/despertar", null);
        Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
        Assert.False(string.IsNullOrWhiteSpace((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString()));

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/pc/principal", new { id = (int?)null })).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await c.GetFromJsonAsync<JsonElement>("/api/pc/principal")).GetProperty("principal").ValueKind);
    }

    // ---- lo que se maneja desde Telegram (los comandos y los botones, sin hablar con Telegram de verdad) ----

    [Fact]
    public async Task Los_comandos_de_telegram_manejan_la_guardia_y_los_runbooks()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (db, guardia, runner, agente, reloj) = (sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<GuardiaServicio>(), sp.GetRequiredService<RunbookRunner>(), sp.GetRequiredService<EstadoAgente>(), sp.GetRequiredService<TimeProvider>());

        var (on, tecladoOn) = await TelegramServidor.Comando("/guardia", "on", db, guardia, runner, agente, reloj, default);
        Assert.Contains("ACTIVADO", on); Assert.Contains("gd:off", JsonSerializer.Serialize(tecladoOn));
        Assert.True(await GuardiaServicio.Activa(db));
        var (off, _) = await TelegramServidor.Comando("/guardia", "off", db, guardia, runner, agente, reloj, default);
        Assert.Contains("apagado", off); Assert.False(await GuardiaServicio.Activa(db));

        var (lista, botones) = await TelegramServidor.Comando("/runbooks", "", db, guardia, runner, agente, reloj, default);
        Assert.Contains("Comprobar todo", lista); Assert.Contains("rb:run:", JsonSerializer.Serialize(botones));

        Assert.Contains("No encuentro", (await TelegramServidor.Comando("/ejecuta", "no existe", db, guardia, runner, agente, reloj, default)).Html);
        Assert.Contains("Dime cuál", (await TelegramServidor.Comando("/ejecuta", "", db, guardia, runner, agente, reloj, default)).Html);
        // «/ejecuta» busca por nombre sin mirar mayúsculas ni que esté entero, y lo lanza
        Assert.Contains("Lanzado", (await TelegramServidor.Comando("/ejecuta", "COMPROBAR", db, guardia, runner, agente, reloj, default)).Html);
        // sin aparato elegido, /enciende lo dice
        Assert.Contains("no has elegido", (await TelegramServidor.Comando("/enciende", "", db, guardia, runner, agente, reloj, default)).Html);
    }

    [Fact]
    public async Task Los_botones_de_telegram_deniegan_entradas_silencian_alarmas_y_cancelan_runbooks()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var (db, guardia, runner, reloj, cache) = (sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<GuardiaServicio>(), sp.GetRequiredService<RunbookRunner>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IMemoryCache>());

        // «No he sido yo» en una petición de entrada
        var p = await (await Anonimo().PostAsync("/api/auth/aprobacion", null)).Content.ReadFromJsonAsync<JsonElement>();
        var id = p.GetProperty("id").GetString()!;
        var r = await TelegramServidor.Boton("lg", id, db, guardia, runner, cache, reloj, default);
        Assert.Contains("denegada", r!.Value.Html);
        Assert.Contains("ya no estaba pendiente", (await TelegramServidor.Boton("lg", id, db, guardia, runner, cache, reloj, default))!.Value.Html);
        Assert.Equal("denegada", (await db.Aprobaciones.FindAsync(id))!.Estado);

        // silenciar una alarma una hora, e ignorar
        Assert.False(guardia.Silenciada("direccion"));
        Assert.Contains("Silenciado", (await TelegramServidor.Boton("gd", "sil:direccion", db, guardia, runner, cache, reloj, default))!.Value.Html);
        Assert.True(guardia.Silenciada("direccion"));
        Assert.Contains("Ignorado", (await TelegramServidor.Boton("gd", "ok", db, guardia, runner, cache, reloj, default))!.Value.Html);

        // lanzar un runbook y cancelar uno que espera permiso
        var c = await Dentro();
        var rb = await CrearRunbook(c, "Ensayo telegram", [new { tipo = "orden", valor = "bloquear", aprobar = true }]);
        Assert.Contains("Lanzado", (await TelegramServidor.Boton("rb", $"run:{rb}", db, guardia, runner, cache, reloj, default))!.Value.Html);
        var ejec = (await c.GetFromJsonAsync<JsonElement>("/api/servidor/ejecuciones?max=1")).EnumerateArray().First();
        await Esperar(c, ejec.GetProperty("id").GetInt32(), "esperando");
        Assert.Contains("ya se está ejecutando", (await TelegramServidor.Boton("rb", $"run:{rb}", db, guardia, runner, cache, reloj, default))!.Value.Html);
        Assert.Contains("cancelado", (await TelegramServidor.Boton("rb", $"no:{ejec.GetProperty("id").GetInt32()}", db, guardia, runner, cache, reloj, default))!.Value.Html);
        await Esperar(c, ejec.GetProperty("id").GetInt32(), "cancelado");

        Assert.Null(await TelegramServidor.Boton("zz", "x", db, guardia, runner, cache, reloj, default));
    }
}
