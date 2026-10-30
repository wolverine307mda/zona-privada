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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Admin:Usuario", Usuario);
        builder.UseSetting("Admin:Email", Email);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Seq:Url", "");
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
    public async Task Las_respuestas_llevan_cabeceras_de_seguridad()
    {
        var res = await factory.CreateClient().GetAsync("/api/salud");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
    }
}
