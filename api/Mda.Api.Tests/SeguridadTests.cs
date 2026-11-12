using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Archivos;
using Mda.Api.Auth;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

// Fase 0 de seguridad: la IP no se puede falsear, el Bearer está cerrado, cabeceras, ejecutables, sesiones y avisos que fallan
public class IpClienteTests
{
    private static DefaultHttpContext Contexto(bool cloudflare, string? cabecera, string? remota)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Proxy:Cloudflare"] = cloudflare ? "true" : "false" }).Build();
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IConfiguration>(config).BuildServiceProvider() };
        if (cabecera is not null) ctx.Request.Headers["CF-Connecting-IP"] = cabecera;
        if (remota is not null) ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remota);
        return ctx;
    }

    [Fact]
    public void Una_cabecera_inventada_no_cambia_la_ip() =>
        Assert.Equal("203.0.113.7", IpCliente.De(Contexto(false, "1.2.3.4", "203.0.113.7")));

    [Fact]
    public void Con_cloudflare_se_usa_su_cabecera_si_es_una_ip() =>
        Assert.Equal("1.2.3.4", IpCliente.De(Contexto(true, "1.2.3.4", "203.0.113.7")));

    [Fact]
    public void Con_cloudflare_una_cabecera_que_no_es_ip_se_ignora() =>
        Assert.Equal("203.0.113.7", IpCliente.De(Contexto(true, new string('x', 200), "203.0.113.7")));

    [Fact]
    public void La_ipv4_dentro_de_ipv6_se_normaliza() =>
        Assert.Equal("81.43.200.225", IpCliente.De(Contexto(false, null, "::ffff:81.43.200.225")));

    [Fact]
    public void Sin_conexion_conocida_es_desconocida() => Assert.Equal("desconocida", IpCliente.De(Contexto(false, null, null)));
}

public class SeguridadApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Entrar_sin_cookie_no_da_token_bearer()
    {
        var c = factory.CreateClient();
        var res = await c.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.Usuario, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.DoesNotContain("accessToken", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_fallo_revisado_deja_de_contar_como_incidencia()
    {
        var c = await factory.Dentro();
        int id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var a = new Acceso { Exito = false, Ip = "203.0.113.7", Motivo = "contraseña o código incorrectos" };
            db.Accesos.Add(a);
            await db.SaveChangesAsync();
            id = a.Id;
        }
        var antes = (await c.GetFromJsonAsync<JsonElement>("/api/seguridad/estado")).GetProperty("fallosMes").GetInt32();
        Assert.True(antes >= 1);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/api/seguridad/accesos/{id}/revisado?revisado=true", null)).StatusCode);
        var despues = (await c.GetFromJsonAsync<JsonElement>("/api/seguridad/estado")).GetProperty("fallosMes").GetInt32();
        Assert.Equal(antes - 1, despues);

        var lista = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/accesos");
        Assert.Contains(lista.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id && x.GetProperty("revisado").GetBoolean());

        var todos = await c.PostAsync("/api/seguridad/accesos/revisar", null);
        Assert.Equal(HttpStatusCode.OK, todos.StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/seguridad/estado")).GetProperty("fallosMes").GetInt32());
    }

    [Fact]
    public async Task Refresh_y_confirmar_correo_estan_cerrados()
    {
        var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/auth/confirmEmail?userId=x&code=y")).StatusCode);
    }

    [Fact]
    public async Task Una_ip_falsa_en_la_cabecera_no_llega_al_registro_de_accesos()
    {
        var c = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login?useCookies=true") { Content = JsonContent.Create(new { email = ApiFactory.Usuario, password = "mala-mala-mala" }) };
        req.Headers.Add("CF-Connecting-IP", "198.51.100.99");
        await c.SendAsync(req);
        using var scope = factory.Services.CreateScope();
        var accesos = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Accesos.ToListAsync();
        Assert.DoesNotContain(accesos, a => a.Ip == "198.51.100.99");
    }

    [Fact]
    public async Task Cabeceras_de_seguridad_y_sin_cache_en_la_api()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var res = await c.GetAsync("/api/sesion");
        Assert.Equal("same-origin", res.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Equal("same-origin", res.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Contains("max-age=", res.Headers.GetValues("Strict-Transport-Security").Single());
        Assert.Contains("no-store", res.Headers.GetValues("Cache-Control").Single());
        Assert.Contains("geolocation=(self)", res.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public async Task Sin_https_no_hay_hsts_y_la_salud_se_puede_leer_desde_otra_web()
    {
        var res = await factory.CreateClient().GetAsync("/api/salud");
        Assert.False(res.Headers.Contains("Strict-Transport-Security"));
        Assert.False(res.Headers.Contains("Cross-Origin-Resource-Policy"));
    }

    [Fact]
    public async Task Una_sesion_de_mas_de_30_dias_ya_no_vale()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sistema")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var s in await db.Sesiones.Where(s => s.Cerrada == null).ToListAsync()) s.Creada = DateTimeOffset.UtcNow.AddDays(-31);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/sistema")).StatusCode);
    }

    [Fact]
    public async Task El_agente_con_una_cabecera_de_proxy_se_rechaza()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente") { Content = JsonContent.Create(new { cpu = 1 }) };
        req.Headers.Add("X-Agente-Token", ApiFactory.Token);
        // llega de Internet por el túnel: UseForwardedHeaders ya ha retirado X-Forwarded-For y deja X-Original-For
        // (el servidor de pruebas no tiene IP remota y el middleware no la copia; en producción se comprueba con curl a la dirección pública)
        req.Headers.Add("X-Original-For", "203.0.113.5");
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task El_agente_no_acepta_cuerpos_enormes()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente") { Content = new StringContent(new string('a', 100_000), System.Text.Encoding.UTF8, "application/json") };
        req.Headers.Add("X-Agente-Token", ApiFactory.Token);
        var res = await factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
    }
}

public class EjecutablesTests(ArchivosFactory factory) : IClassFixture<ArchivosFactory>
{
    private async Task<HttpClient> Dentro()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password });
        return c;
    }

    private static MultipartFormDataContent Subida(string nombre)
    {
        var f = new MultipartFormDataContent();
        f.Add(new ByteArrayContent("MZ"u8.ToArray()), "archivo", nombre);
        return f;
    }

    [Theory]
    [InlineData("instalador.exe")]
    [InlineData("script.PS1")]
    [InlineData("acceso.lnk")]
    [InlineData("lote.bat")]
    public async Task No_se_suben_programas_ni_scripts(string nombre)
    {
        var c = await Dentro();
        var res = await c.PostAsync("/api/archivos/subir?raiz=documentos", Subida(nombre));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.False(File.Exists(Path.Combine(factory.Base, "documentos", nombre)));
    }

    [Fact]
    public async Task Un_documento_normal_si_se_sube()
    {
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/archivos/subir?raiz=documentos", Subida("apuntes.txt"))).StatusCode);
    }

    [Fact]
    public async Task No_se_renombra_un_archivo_a_ejecutable_pero_si_uno_que_ya_lo_era()
    {
        var c = await Dentro();
        File.WriteAllText(Path.Combine(factory.Base, "documentos", "viejo.txt"), "x");
        var mal = await c.PostAsJsonAsync("/api/archivos/renombrar", new { raiz = "documentos", ruta = "viejo.txt", nombre = "viejo.exe" });
        Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);

        File.WriteAllBytes(Path.Combine(factory.Base, "documentos", "antiguo.exe"), "MZ"u8.ToArray());
        var bien = await c.PostAsJsonAsync("/api/archivos/renombrar", new { raiz = "documentos", ruta = "antiguo.exe", nombre = "antiguo2.exe" });
        Assert.Equal(HttpStatusCode.OK, bien.StatusCode);
    }

    [Fact]
    public void Con_el_permiso_explicito_se_permiten()
    {
        var c = new Carpetas(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Archivos:PermitirEjecutables"] = "true" }).Build());
        Assert.False(c.ExtensionBloqueada("a.exe"));
    }
}

public class ContrasenaAdminTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Una_contrasena_cambiada_desde_el_perfil_sobrevive_al_reinicio()
    {
        var semilla = factory.Services.GetServices<IHostedService>().OfType<SemillaAdmin>().Single();
        await semilla.StartAsync(CancellationToken.None); // primer arranque con huella: la anota

        var c = await factory.Dentro();
        var cambio = await c.PostAsJsonAsync("/api/perfil/clave", new { actual = ApiFactory.Password, nueva = "otra-clave-bien-larga" });
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);

        await semilla.StartAsync(CancellationToken.None); // reinicio con el mismo .env
        var otro = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.True((await otro.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = "otra-clave-bien-larga" })).IsSuccessStatusCode);
        var vieja = factory.CreateClient();
        Assert.False((await vieja.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).IsSuccessStatusCode);
    }
}

public class PoliticaClaveTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Las_contrasenas_de_menos_de_12_caracteres_se_rechazan()
    {
        var c = await factory.Dentro();
        var res = await c.PostAsJsonAsync("/api/perfil/clave", new { actual = ApiFactory.Password, nueva = "once-chars!" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

// un canal que falla (tiempo agotado, asunto raro…) no puede repetir el aviso ni bloquear los demás eventos
public class AvisosQueFallanTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private class CanalRoto : IAvisos
    {
        public List<string> Enviados { get; } = [];
        public Canales Disponibles() => new(true, true);
        public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) { Enviados.Add(asunto); return Task.FromResult(true); }
        public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) => throw new TaskCanceledException("tiempo agotado");
    }

    [Fact]
    public async Task Un_canal_roto_no_bloquea_los_demas_eventos_ni_repite_el_aviso()
    {
        var c = await factory.Dentro();
        var inicio = DateTimeOffset.UtcNow.AddMinutes(5);
        var ids = new List<int>();
        foreach (var (titulo, whats) in new[] { ("Primero roto", true), ("Segundo sano", false) })
        {
            var r = await c.PostAsJsonAsync("/api/agenda", new
            {
                tipo = "Recordatorio", titulo, inicio, todoElDia = false, repetir = "Mensual",
                avisoMinutos = 10, porCorreo = true, porWhatsApp = whats, enGoogle = false,
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            ids.Add((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
        }

        var avisos = new CanalRoto();
        var rec = new Recordatorios(factory.Services.GetRequiredService<IServiceScopeFactory>(), avisos,
            factory.Services.GetRequiredService<Zona>(), TimeProvider.System, NullLogger<Recordatorios>.Instance);
        await rec.Revisar(CancellationToken.None);

        Assert.Contains(avisos.Enviados, a => a.Contains("Segundo sano"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // el sano ya pasó a la siguiente repetición; el roto se queda esperando para reintentar solo el WhatsApp
        Assert.True((await db.Eventos.FindAsync(ids[1]))!.ProximoAviso > DateTimeOffset.UtcNow.AddDays(25));
        var roto = (await db.Eventos.FindAsync(ids[0]))!;
        Assert.True(roto.ProximoAviso <= DateTimeOffset.UtcNow);
        Assert.Equal(1, roto.Intentos);

        var antes = avisos.Enviados.Count;
        await rec.Revisar(CancellationToken.None);
        Assert.Equal(antes, avisos.Enviados.Count); // ni el correo que ya llegó ni lo del sano se repiten
    }
}
