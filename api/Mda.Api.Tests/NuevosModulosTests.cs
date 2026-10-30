using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mda.Api.Tests;

// avisos que no salen de la máquina: se apuntan para comprobarlos
public class AvisosDePrueba : IAvisos
{
    public List<string> Enviados { get; } = [];
    public Canales Disponibles() => new(true, true);
    public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) { lock (Enviados) Enviados.Add($"correo:{texto}"); return Task.FromResult(true); }
    public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) { lock (Enviados) Enviados.Add($"whatsapp:{texto}"); return Task.FromResult(true); }
}

public class ModulosFactory : ApiFactory
{
    public AvisosDePrueba Avisos { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("App:Zona", "Europe/Madrid");
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IAvisos>(Avisos)));
    }

    public async Task<HttpClient> Dentro(string agente = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(agente);
        var r = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = Usuario, password = Password });
        r.EnsureSuccessStatusCode();
        return client;
    }
}

public class OcurrenciasTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
    private static DateTimeOffset M(int a, int m, int d, int h = 10) => new(new DateTime(a, m, d, h, 0, 0), Madrid.GetUtcOffset(new DateTime(a, m, d, h, 0, 0)));

    [Fact]
    public void Lo_mensual_del_31_cae_el_ultimo_dia_y_mantiene_la_hora_local()
    {
        var e = new Evento { Inicio = M(2026, 1, 31), Repetir = Repeticion.Mensual };
        var l = Ocurrencias.Entre(e, M(2026, 2, 1, 0), M(2026, 4, 1, 0), Madrid).ToList();
        Assert.Equal([M(2026, 2, 28), M(2026, 3, 31)], l);
        // 31 de marzo ya es horario de verano: misma hora local, otro desfase
        Assert.Equal(TimeSpan.FromHours(2), l[1].Offset);
    }

    [Fact]
    public void Lo_semanal_respeta_la_fecha_de_fin()
    {
        var e = new Evento { Inicio = M(2026, 10, 1), Repetir = Repeticion.Semanal, RepetirHasta = new DateOnly(2026, 10, 20) };
        Assert.Equal(3, Ocurrencias.Entre(e, M(2026, 9, 1), M(2026, 12, 1), Madrid).Count());
    }

    [Fact]
    public void El_aviso_es_antes_del_inicio_y_si_ya_paso_se_manda_en_cuanto_se_pueda()
    {
        var e = new Evento { Inicio = M(2026, 11, 2), AvisoMinutos = 30, PorCorreo = true };
        Assert.Equal(M(2026, 11, 2).AddMinutes(-30), Ocurrencias.ProximoAviso(e, M(2026, 11, 1), M(2026, 11, 1), Madrid)!.Value.Aviso);
        var ahora = M(2026, 11, 2).AddMinutes(-10);
        Assert.Equal(ahora, Ocurrencias.ProximoAviso(e, ahora, ahora, Madrid)!.Value.Aviso);
        Assert.Null(Ocurrencias.ProximoAviso(e, M(2026, 11, 3), M(2026, 11, 3), Madrid));
        e.PorCorreo = false;
        Assert.Null(Ocurrencias.ProximoAviso(e, M(2026, 11, 1), M(2026, 11, 1), Madrid));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1", "iPhone · Safari", "movil")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0 Safari/537.36 Edg/140.0", "Windows · Edge", "ordenador")]
    [InlineData("Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 Chrome/140.0 Mobile Safari/537.36", "Android · Chrome", "movil")]
    public void Reconoce_el_equipo(string ua, string nombre, string tipo)
    {
        Assert.Equal(nombre, Dispositivo.Nombre(ua));
        Assert.Equal(tipo, Dispositivo.Tipo(ua));
    }
}

public class NuevosModulosTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("/api/notas")]
    [InlineData("/api/gastos/deudas")]
    [InlineData("/api/agenda/proximos")]
    [InlineData("/api/seguridad/sesiones")]
    [InlineData("/api/musica/ahora")]
    [InlineData("/api/conexiones")]
    public async Task Todo_pide_sesion(string ruta)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(ruta)).StatusCode);
    }

    [Fact]
    public async Task Una_deuda_suma_sus_pagos_y_calcula_lo_que_falta()
    {
        var c = await factory.Dentro();
        var r = await c.PostAsJsonAsync("/api/gastos/deudas", new { nombre = "Coche", total = 9000, categoria = "cuota-coche", inicio = "2025-10-01" });
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await c.PostAsJsonAsync($"/api/gastos/deudas/{id}/pagos", new { importe = 450, fecha = "2025-10-18" });
        await c.PostAsJsonAsync($"/api/gastos/deudas/{id}/pagos", new { importe = 50.5, fecha = "2025-10-28" });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/gastos/deudas/{id}/pagos", new { importe = 0 })).StatusCode);

        var j = await c.GetFromJsonAsync<JsonElement>("/api/gastos/deudas");
        var d = j.GetProperty("deudas").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal(500.5m, d.GetProperty("pagado").GetDecimal());
        Assert.Equal(8499.5m, d.GetProperty("restante").GetDecimal());
        Assert.Equal(2, d.GetProperty("numPagos").GetInt32());
        // los pagos son gastos del mes, con su categoría
        var movs = await c.GetFromJsonAsync<JsonElement>("/api/gastos/movimientos?categoria=cuota-coche");
        Assert.Equal(2, movs.GetArrayLength());
        Assert.Contains(j.GetProperty("grupos").EnumerateArray(), x => x.GetProperty("grupo").GetString() == "Coche");
    }

    [Fact]
    public async Task Las_notas_se_crean_buscan_archivan_y_borran()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/notas", new { titulo = "", contenido = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/notas", new { titulo = "x", color = "fucsia" })).StatusCode);
        var r = await c.PostAsJsonAsync("/api/notas", new { titulo = "Compra", contenido = "- [ ] leche\n- [x] pan", color = "verde", etiquetas = new[] { "Casa", "casa" }, fijada = true });
        var n = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["casa"], n.GetProperty("etiquetas").EnumerateArray().Select(x => x.GetString()));
        var id = n.GetProperty("id").GetInt32();

        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>("/api/notas?q=leche")).GetArrayLength());
        await c.PutAsJsonAsync($"/api/notas/{id}", new { titulo = "Compra", contenido = "- [x] leche", color = "verde", archivada = true });
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/notas?q=leche")).GetArrayLength());
        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>("/api/notas?archivadas=true&q=leche")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/notas/{id}")).StatusCode);
    }

    [Fact]
    public async Task Un_recordatorio_mensual_avisa_y_se_reprograma()
    {
        var c = await factory.Dentro();
        var inicio = DateTimeOffset.UtcNow.AddMinutes(5);
        var r = await c.PostAsJsonAsync("/api/agenda", new
        {
            tipo = "Recordatorio", titulo = "Pagar el coche", inicio, todoElDia = false, repetir = "Mensual",
            avisoMinutos = 10, porCorreo = true, porWhatsApp = true, enGoogle = false,
        }, Web);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        await factory.Services.GetRequiredService<Recordatorios>().Revisar(CancellationToken.None);
        Assert.Contains(factory.Avisos.Enviados, x => x.StartsWith("correo:⏰ Pagar el coche"));
        Assert.Contains(factory.Avisos.Enviados, x => x.StartsWith("whatsapp:⏰ Pagar el coche"));

        using var scope = factory.Services.CreateScope();
        var ev = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Eventos.FindAsync(id);
        Assert.True(ev!.ProximoAviso > DateTimeOffset.UtcNow.AddDays(25));

        var prox = await c.GetFromJsonAsync<JsonElement>("/api/agenda/proximos");
        Assert.Contains(prox.EnumerateArray(), x => x.GetProperty("titulo").GetString() == "Pagar el coche");
        var mes = await c.GetFromJsonAsync<JsonElement>($"/api/agenda?desde={Uri.EscapeDataString(inicio.AddDays(-1).ToString("O"))}&hasta={Uri.EscapeDataString(inicio.AddDays(70).ToString("O"))}");
        Assert.InRange(mes.GetProperty("apariciones").GetArrayLength(), 2, 3);
        Assert.False(mes.GetProperty("google").GetBoolean());
    }

    [Fact]
    public async Task Las_sesiones_se_ven_y_se_cierran_desde_otro_equipo()
    {
        var movil = await factory.Dentro();
        var pc = await factory.Dentro("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0 Safari/537.36");
        var lista = await pc.GetFromJsonAsync<JsonElement>("/api/seguridad/sesiones");
        var actual = lista.EnumerateArray().First();
        Assert.True(actual.GetProperty("actual").GetBoolean());
        Assert.Equal("Windows", actual.GetProperty("dispositivo").GetString());
        Assert.Contains(lista.EnumerateArray(), x => x.GetProperty("dispositivo").GetString() == "iPhone" && x.GetProperty("enLinea").GetBoolean());

        // desde el PC se cierran las demás: el móvil se queda fuera
        Assert.Equal(HttpStatusCode.OK, (await pc.DeleteAsync("/api/seguridad/sesiones")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await movil.GetAsync("/api/sesion")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pc.GetAsync("/api/sesion")).StatusCode);

        // al salir, la sesión queda cerrada
        await pc.PostAsync("/api/sesion/salir", null);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.DoesNotContain(db.Sesiones, s => s.Cerrada == null && s.Agente.Contains("Windows"));
    }

    [Fact]
    public async Task Los_intentos_de_entrar_quedan_registrados()
    {
        var fuera = factory.CreateClient();
        await fuera.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = "mala-mala" });
        var c = await factory.Dentro();
        var accesos = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/accesos");
        Assert.Contains(accesos.EnumerateArray(), a => !a.GetProperty("exito").GetBoolean());
        Assert.Contains(accesos.EnumerateArray(), a => a.GetProperty("exito").GetBoolean());
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/seguridad/estado")).GetProperty("fallosMes").GetInt32() >= 1);
    }

    [Fact]
    public async Task Las_conexiones_sin_configurar_o_con_state_falso_no_pasan()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password });
        var r = await c.GetAsync("/api/conexiones/spotify/conectar");
        Assert.Equal("/musica?conexion=sin-configurar", r.Headers.Location?.ToString());
        var v = await factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync("/api/conexiones/google/vuelta?code=x&state=inventado");
        Assert.Equal("/agenda?conexion=caducada", v.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync("/api/musica/ahora")).StatusCode);
    }
}
