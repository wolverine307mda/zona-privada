using System.Net;
using System.Net.Http.Json;
using Mda.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

// La API completa en memoria: base de datos InMemory y un administrador de prueba.
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string Usuario = "wolve";
    public const string Email = "admin@prueba.local";
    public const string Password = "clave-de-prueba";
    public const string Token = "token-del-agente-de-prueba-0123456789"; // 32 o más caracteres

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Admin:Usuario", Usuario);
        builder.UseSetting("Admin:Email", Email);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Seq:Url", "");
        builder.UseSetting("Agente:Token", Token);
        builder.UseSetting("Agente:EsperaSegundos", "1"); // el agente espera 20 s órdenes; en las pruebas, 1
        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d =>
                         d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                         d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>)).ToList())
                services.Remove(d);
            var nombre = Guid.NewGuid().ToString();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(nombre));
        });
    }
}

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Salud_responde_sin_sesion()
    {
        var res = await factory.CreateClient().GetAsync("/api/salud");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Sistema_pide_sesion()
    {
        var res = await factory.CreateClient().GetAsync("/api/sistema");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task No_hay_registro_publico()
    {
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = "x@y.z", password = "Una-clave-larga-1" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Contraseña_mala_no_entra()
    {
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = "mala" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Con_sesion_se_ve_el_sistema_y_al_salir_ya_no()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("mda.sesion=") && c.Contains("httponly") && c.Contains("samesite=strict"));

        var yo = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/sesion");
        Assert.Equal(ApiFactory.Usuario, yo!["usuario"]);
        Assert.Equal(ApiFactory.Email, yo["email"]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/sistema")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/sesion/salir", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/sistema")).StatusCode);
    }

    [Fact]
    public async Task La_web_publica_solo_puede_consultar_la_salud()
    {
        var client = factory.CreateClient();
        var salud = new HttpRequestMessage(HttpMethod.Get, "/api/salud");
        salud.Headers.Add("Origin", "https://wolverine307mda.github.io");
        var res = await client.SendAsync(salud);
        Assert.Equal("https://wolverine307mda.github.io", res.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var otraWeb = new HttpRequestMessage(HttpMethod.Get, "/api/salud");
        otraWeb.Headers.Add("Origin", "https://otra-web.com");
        Assert.False((await client.SendAsync(otraWeb)).Headers.Contains("Access-Control-Allow-Origin"));

        var sesion = new HttpRequestMessage(HttpMethod.Get, "/api/sesion");
        sesion.Headers.Add("Origin", "https://wolverine307mda.github.io");
        Assert.False((await client.SendAsync(sesion)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static object Lectura(string equipo) => new
    {
        cpu = 42.5, memoriaUsada = 8L << 30, memoriaTotal = 32L << 30, discoUsado = 100L << 30, discoTotal = 500L << 30,
        encendidoSegundos = 3600, nucleos = 16, equipo, sistemaOperativo = "Microsoft Windows 10.0.26200",
        hora = DateTimeOffset.UtcNow, cpuModelo = "CPU de prueba", discos = new[] { new { nombre = "C:\\", usado = 100L << 30, total = 500L << 30 } },
        redBajada = 1000.0, redSubida = 500.0, origen = "equipo",
    };

    [Fact]
    public async Task El_agente_necesita_el_token()
    {
        var client = factory.CreateClient();
        var sinToken = await client.PostAsJsonAsync("/api/sistema/agente", Lectura("PC-PRUEBA"));
        Assert.Equal(HttpStatusCode.Unauthorized, sinToken.StatusCode);

        var mal = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente") { Content = JsonContent.Create(Lectura("PC-PRUEBA")) };
        mal.Headers.Add("X-Agente-Token", "otro");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(mal)).StatusCode);
    }

    [Fact]
    public async Task Con_el_agente_el_panel_muestra_el_equipo_real()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password });

        string? equipo = null;
        for (var i = 0; i < 20 && equipo != "PC-PRUEBA"; i++)
        {
            var envio = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente") { Content = JsonContent.Create(Lectura("PC-PRUEBA")) };
            envio.Headers.Add("X-Agente-Token", ApiFactory.Token);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(envio)).StatusCode);
            await Task.Delay(500);
            var sistema = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/sistema");
            if (sistema.GetProperty("actual").ValueKind == System.Text.Json.JsonValueKind.Object)
                equipo = sistema.GetProperty("actual").GetProperty("equipo").GetString();
        }
        Assert.Equal("PC-PRUEBA", equipo);
    }

    [Fact]
    public async Task Las_respuestas_llevan_cabeceras_de_seguridad()
    {
        var res = await factory.CreateClient().GetAsync("/api/salud");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
    }
}
