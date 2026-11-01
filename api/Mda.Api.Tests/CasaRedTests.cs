using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Pc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mda.Api.Tests;

public class ReglasBateriaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Avisa_una_vez_al_bajar_de_15_sin_enchufar_y_otra_vez_tras_cargar()
    {
        var r = new ReglasBateria();
        Assert.Null(r.Evaluar(40, false, T0));
        Assert.Contains("15", r.Evaluar(15, false, T0)!);
        Assert.Null(r.Evaluar(14, false, T0));            // ya avisó
        Assert.Null(r.Evaluar(10, true, T0));              // enchufado: nada
        Assert.NotNull(r.Evaluar(12, false, T0));          // se enchufó y volvió a bajar: avisa otra vez
    }

    [Fact]
    public void Sin_bateria_no_hay_aviso_y_enchufado_no_hay_aviso_de_baja()
    {
        var r = new ReglasBateria();
        Assert.Null(r.Evaluar(null, null, T0));
        Assert.Null(r.Evaluar(5, true, T0));
    }

    [Fact]
    public void Tras_dos_horas_enchufado_al_100_avisa_una_sola_vez_y_se_reinicia_al_desenchufar()
    {
        var r = new ReglasBateria();
        Assert.Null(r.Evaluar(100, true, T0));
        Assert.Null(r.Evaluar(100, true, T0.AddMinutes(119)));
        Assert.Contains("2 horas", r.Evaluar(100, true, T0.AddMinutes(121))!);
        Assert.Null(r.Evaluar(100, true, T0.AddHours(5)));
        Assert.Null(r.Evaluar(80, false, T0.AddHours(6)));
        Assert.Null(r.Evaluar(100, true, T0.AddHours(7)));  // empieza a contar de nuevo
        Assert.NotNull(r.Evaluar(100, true, T0.AddHours(9.1)));
    }
}

// Con el agente "conectado" y sus órdenes a la vista
public class CasaTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
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
            hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>(),
        }));

    private async Task<List<(string Tipo, string Parametro)>> Pendientes()
    {
        var j = await (await factory.CreateClient().SendAsync(Agente(HttpMethod.Get, "/api/sistema/agente/ordenes"))).Content.ReadFromJsonAsync<JsonElement>();
        return j.EnumerateArray().Select(o => (o.GetProperty("tipo").GetString()!, o.GetProperty("parametro").GetString()!)).ToList();
    }

    [Fact]
    public async Task Llegue_y_me_voy_dan_sus_ordenes_con_sesion_o_con_token_de_atajos()
    {
        await AgenteConectado();
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/pc/casa", new { estado = "vuelta" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/pc/casa", new { estado = "llegada" })).StatusCode);
        var p = await Pendientes();
        Assert.Contains(("brillo", "70"), p); Assert.Contains(("volumen", "30"), p); Assert.Contains(("despierto", "off"), p);

        // desde el iPhone, con un token que tenga el permiso «casa»
        var token = (await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "Casa", alcances = new[] { "casa" } })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var movil = factory.CreateClient();
        movil.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await movil.PostAsJsonAsync("/api/atajos/casa", new { estado = "salida" })).StatusCode);
        var q = await Pendientes();
        Assert.Contains(("bloquear", ""), q); Assert.Contains(("despierto", "off"), q);
        // otro token sin ese permiso no vale
        var otro = (await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "Gasto", alcances = new[] { "gasto" } })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var malo = factory.CreateClient(); malo.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otro);
        Assert.Equal(HttpStatusCode.Unauthorized, (await malo.PostAsJsonAsync("/api/atajos/casa", new { estado = "salida" })).StatusCode);
        Assert.Contains(factory.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("Llegaste a casa"));
    }

    [Fact]
    public async Task La_concentracion_silencia_y_al_apagarla_devuelve_lo_que_habia()
    {
        await AgenteConectado();
        var c = await factory.Dentro();
        // el agente cuenta cómo está el PC: volumen 55 y brillo 80
        var envio = Agente(HttpMethod.Post, "/api/sistema/agente", new
        {
            cpu = 1.0, memoriaUsada = 1L, memoriaTotal = 2L, discoUsado = 1L, discoTotal = 2L, encendidoSegundos = 5, nucleos = 4, equipo = "PORTATIL", sistemaOperativo = "Windows",
            hora = DateTimeOffset.UtcNow, origen = "equipo", volumen = 55, brillo = 80,
        });
        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().SendAsync(envio)).StatusCode);
        await Pendientes(); // vacía lo anterior

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/pc/concentracion", new { activo = true })).StatusCode);
        var on = await Pendientes();
        Assert.Contains(("volumen", "0"), on); Assert.Contains(("brillo", "25"), on); Assert.Contains(("bloquear", ""), on);
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/pc")).GetProperty("concentracion").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/pc/concentracion", new { activo = false })).StatusCode);
        var off = await Pendientes();
        Assert.Contains(("volumen", "55"), off); Assert.Contains(("brillo", "80"), off);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/pc")).GetProperty("concentracion").GetBoolean());
    }

    [Fact]
    public async Task La_musica_solo_acepta_acciones_conocidas_y_las_ordenes_dedicadas_no_se_piden_a_mano()
    {
        await AgenteConectado();
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/pc/multimedia", new { accion = "siguiente" })).StatusCode);
        Assert.Contains(("multimedia", "siguiente"), await Pendientes());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/multimedia", new { accion = "apagar" })).StatusCode);
        foreach (var t in new[] { "multimedia", "portapapeles", "copiar" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pc/ordenes", new { tipo = t, parametro = "1" })).StatusCode);
    }

    [Fact]
    public async Task El_portapapeles_pasa_textos_en_los_dos_sentidos_y_se_puede_vaciar()
    {
        await AgenteConectado();
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/portapapeles", new { texto = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/portapapeles", new { texto = new string('x', 2001) })).StatusCode);
        var e = await (await c.PostAsJsonAsync("/api/portapapeles", new { texto = "https://ejemplo.com/a?b=1" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = e.GetProperty("id").GetInt32();
        Assert.Equal("https://ejemplo.com/a?b=1", (await c.GetFromJsonAsync<JsonElement>("/api/portapapeles"))[0].GetProperty("texto").GetString());

        // al PC: la orden lleva solo el número, y el agente pide el texto con su token
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/portapapeles/{id}/al-pc", null)).StatusCode);
        Assert.Contains(("portapapeles", id.ToString()), await Pendientes());
        var texto = await (await factory.CreateClient().SendAsync(Agente(HttpMethod.Get, $"/api/sistema/agente/portapapeles/{id}"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://ejemplo.com/a?b=1", texto.GetProperty("texto").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/sistema/agente/portapapeles/{id}")).StatusCode);

        // del PC: el agente deja lo que había copiado
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/portapapeles/del-pc", null)).StatusCode);
        Assert.Contains(("copiar", ""), await Pendientes());
        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().SendAsync(Agente(HttpMethod.Post, "/api/sistema/agente/portapapeles", new { texto = "copiado en el PC" }))).StatusCode);
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/portapapeles");
        Assert.Equal("El PC", lista[0].GetProperty("origen").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/portapapeles/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/portapapeles/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/portapapeles")).StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/portapapeles")).GetArrayLength());
    }

    [Fact]
    public async Task Solo_se_guardan_los_20_ultimos_textos()
    {
        var c = await factory.Dentro();
        await c.DeleteAsync("/api/portapapeles");
        for (var i = 0; i < 25; i++) await c.PostAsJsonAsync("/api/portapapeles", new { texto = $"texto {i}" });
        var l = await c.GetFromJsonAsync<JsonElement>("/api/portapapeles");
        Assert.Equal(20, l.GetArrayLength());
        Assert.Equal("texto 24", l[0].GetProperty("texto").GetString());
    }
}

public class EventosPcTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private HttpRequestMessage Evento(object cuerpo, bool conToken = true)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/eventos") { Content = JsonContent.Create(cuerpo) };
        if (conToken) r.Headers.Add("X-Agente-Token", ApiFactory.Token);
        return r;
    }

    [Fact]
    public async Task Una_descarga_terminada_avisa_una_vez_y_los_avisos_se_pueden_apagar()
    {
        var c = await factory.Dentro();
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Evento(new { tipo = "descarga", texto = "x" }, conToken: false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.SendAsync(Evento(new { tipo = "formatear", texto = "x" }))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await anon.SendAsync(Evento(new { tipo = "descarga", texto = "informe.pdf (2 MB)" }))).StatusCode);
        Assert.Contains(factory.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("informe.pdf"));
        // una ráfaga no manda otro: como mucho uno cada 20 s
        var antes = factory.Avisos.Enviados.Count;
        await anon.SendAsync(Evento(new { tipo = "descarga", texto = "otro.zip" }));
        Assert.Equal(antes, factory.Avisos.Enviados.Count);

        // sus ajustes
        var a = await c.GetFromJsonAsync<JsonElement>("/api/pc/avisos");
        Assert.True(a.GetProperty("descargas").GetBoolean() && a.GetProperty("windows").GetBoolean() && a.GetProperty("copias").GetBoolean() && a.GetProperty("bateria").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/pc/avisos", new { descargas = false, windows = true, copias = true, bateria = true })).StatusCode);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/pc/avisos")).GetProperty("descargas").GetBoolean());
        // Windows pide reiniciar: otro tipo, otro límite
        await anon.SendAsync(Evento(new { tipo = "reinicio-pendiente", texto = "" }));
        Assert.Contains(factory.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("reiniciar"));
    }
}

public class RedFactory : ModulosFactory
{
    public static readonly Dictionary<string, bool> EnLinea = new() { ["192.168.1.50"] = true, ["192.168.1.60"] = true };

    private sealed class Falso : IComprobadorRed
    {
        public Task<(bool EnLinea, int? Ms)> Comprobar(string host, int puerto, CancellationToken ct) =>
            Task.FromResult((RedFactory.EnLinea.GetValueOrDefault(host), RedFactory.EnLinea.GetValueOrDefault(host) ? (int?)3 : null));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IComprobadorRed, Falso>()));
    }
}

public class RedTests(RedFactory factory) : IClassFixture<RedFactory>
{
    [Theory]
    [InlineData("192.168.1.10", true)] [InlineData("10.0.0.7", true)] [InlineData("172.16.5.5", true)] [InlineData("100.100.1.2", true)] [InlineData("fd00::1", true)]
    [InlineData("8.8.8.8", false)] [InlineData("127.0.0.1", false)] [InlineData("::1", false)] [InlineData("172.32.0.1", false)] [InlineData("0.0.0.0", false)]
    public void Solo_las_direcciones_de_casa_o_de_tailscale_son_vigilables(string ip, bool esperado) =>
        Assert.Equal(esperado, RedCasa.EsLocal(System.Net.IPAddress.Parse(ip)));

    [Theory]
    [InlineData("http://192.168.1.5")] [InlineData("192.168.1.5/admin")] [InlineData("usuario@192.168.1.5")] [InlineData("")] [InlineData("8.8.8.8")] [InlineData("localhost")]
    public async Task Un_host_con_rutas_o_publico_se_rechaza(string host) => Assert.NotNull(await RedCasa.ValidarHost(host));

    [Fact]
    public async Task Un_aparato_de_casa_se_anade_se_prueba_y_se_borra()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/red")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/red", new { nombre = "Router", host = "8.8.8.8", puerto = 80 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/red", new { nombre = "", host = "192.168.1.50", puerto = 80 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/red", new { nombre = "NAS", host = "192.168.1.50", puerto = 70000 })).StatusCode);

        var r = await c.PostAsJsonAsync("/api/red", new { nombre = "NAS", host = "192.168.1.50", puerto = 445, icono = "nas" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var d = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(d.GetProperty("enLinea").GetBoolean());
        Assert.Equal("nas", d.GetProperty("icono").GetString());
        var id = d.GetProperty("id").GetInt32();

        RedFactory.EnLinea["192.168.1.50"] = false;
        var p = await (await c.PostAsync($"/api/red/{id}/probar", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(p.GetProperty("enLinea").GetBoolean());
        RedFactory.EnLinea["192.168.1.50"] = true;

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/red/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/red/{id}")).StatusCode);
    }

    [Fact]
    public async Task El_vigilante_solo_avisa_tras_dos_lecturas_seguidas_y_no_en_la_primera()
    {
        var c = await factory.Dentro();
        var d = await (await c.PostAsJsonAsync("/api/red", new { nombre = "Impresora", host = "192.168.1.60", puerto = 9100, icono = "impresora" })).Content.ReadFromJsonAsync<JsonElement>();
        var vigilante = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<VigilanteRed>().Single();
        void Limpiar() { lock (factory.Avisos.Enviados) factory.Avisos.Enviados.RemoveAll(m => m.Contains("Impresora")); }
        Limpiar();

        RedFactory.EnLinea["192.168.1.60"] = false;
        await vigilante.Revisar(default);                    // una lectura mala suelta: nada
        Assert.DoesNotContain(factory.Avisos.Enviados, m => m.Contains("Impresora"));
        await vigilante.Revisar(default);                    // la segunda seguida: avisa
        Assert.Single(factory.Avisos.Enviados, m => m.Contains("«Impresora»") && m.Contains("no responde"));
        await vigilante.Revisar(default);
        Assert.Single(factory.Avisos.Enviados, m => m.Contains("«Impresora»"));  // no repite

        RedFactory.EnLinea["192.168.1.60"] = true;
        await vigilante.Revisar(default); await vigilante.Revisar(default);
        Assert.Contains(factory.Avisos.Enviados, m => m.Contains("«Impresora»") && m.Contains("vuelve"));
        RedFactory.EnLinea["192.168.1.60"] = true;
    }
}
