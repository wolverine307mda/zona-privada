using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Atajos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class PanicoTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task El_boton_de_panico_pide_sesion()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/seguridad/panico", null)).StatusCode);
    }

    [Fact]
    public async Task El_panico_corta_sesiones_tokens_y_lo_publico_y_no_deja_entrar_hasta_que_se_levanta()
    {
        var c = await factory.Dentro();
        var otra = await factory.Dentro("Mozilla/5.0 (Windows NT 10.0) Chrome/130.0 Safari/537.36");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TokensAtajo.Add(new TokenAtajo { Nombre = "iPhone", Hash = "x", Alcances = "gasto" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/galeria/estado", new { activa = true })).StatusCode);

        var r = await c.PostAsync("/api/seguridad/panico", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(j.GetProperty("sesiones").GetInt32() >= 2);
        Assert.True(j.GetProperty("tokens").GetInt32() >= 1);

        // las dos sesiones se han cerrado
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/sistema")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otra.GetAsync("/api/sistema")).StatusCode);
        // y nadie entra mientras dure el bloqueo, ni con la contraseña buena
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, login.StatusCode);
        Assert.Contains("pánico", await login.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.TooManyRequests, (await factory.CreateClient().PostAsync("/api/auth/passkey/entrada", JsonContent.Create(new { }))).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.All(db.TokensAtajo, t => Assert.True(t.Revocado));
            Assert.Equal("no", db.Ajustes.Single(a => a.Clave == "galeria-activa").Valor);
            Assert.Equal("no", db.Ajustes.Single(a => a.Clave == "publico-estado").Valor);
        }
        Assert.Contains(factory.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("pánico"));

        // /desbloquear (Telegram) lo levanta
        using (var scope = factory.Services.CreateScope())
            await Panico.Levantar(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<IMemoryCache>());
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).StatusCode);
    }

    [Fact]
    public async Task El_bloqueo_caduca_solo()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var reloj = new RelojFijo(DateTimeOffset.UtcNow);
        await Panico.Ejecutar(db, reloj, cache);
        Assert.NotNull(await Panico.BloqueadoHasta(db, cache, reloj));
        reloj.Ahora = reloj.Ahora.AddMinutes(61);
        Assert.Null(await Panico.BloqueadoHasta(db, cache, reloj));
        await Panico.Levantar(db, cache);
    }
}

public class SondasTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Theory]
    [InlineData("/.env")] [InlineData("/wp-login.php")] [InlineData("/ADMIN")] [InlineData("/.git/config")] [InlineData("/phpmyadmin/")]
    public async Task Las_sondas_dan_404_como_cualquier_ruta_que_no_existe(string ruta)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync(ruta)).StatusCode);
    }

    [Fact]
    public async Task Las_rutas_de_la_app_no_se_confunden_con_sondas()
    {
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/api/salud")).StatusCode);
        Assert.False(Sondas.EsSonda("/seguridad"));
        Assert.False(Sondas.EsSonda("/api/admin-algo"));
    }

    [Fact]
    public void Una_ip_publica_que_sondea_se_bloquea_una_hora_y_las_locales_nunca()
    {
        var reloj = new RelojFijo(DateTimeOffset.UtcNow);
        var s = new Sondas(reloj);
        s.Anotar("203.0.113.9", "/.env");
        Assert.True(s.EstaBloqueada("203.0.113.9"));
        Assert.False(s.EstaBloqueada("203.0.113.10"));
        reloj.Ahora = reloj.Ahora.AddMinutes(61);
        Assert.False(s.EstaBloqueada("203.0.113.9"));

        foreach (var local in new[] { "127.0.0.1", "::1", "192.168.1.20", "10.0.0.4", "172.20.1.1", "100.100.1.1", "desconocida", "fe80::1" })
        {
            s.Anotar(local, "/.env");
            Assert.False(s.EstaBloqueada(local), local);
        }
        Assert.Equal(9, s.Vaciar().Count);
        Assert.Empty(s.Vaciar());
    }

    [Fact]
    public async Task El_resumen_por_hora_solo_sale_si_hubo_sondas_y_las_cuenta()
    {
        var s = new Sondas(TimeProvider.System);
        var resumen = new ResumenSondas(s, factory.Avisos, TimeProvider.System, NullLogger<ResumenSondas>.Instance);
        var antes = factory.Avisos.Enviados.Count;
        await resumen.Resumir(default);
        Assert.Equal(antes, factory.Avisos.Enviados.Count);
        s.Anotar("203.0.113.9", "/.env"); s.Anotar("203.0.113.9", "/wp-login.php"); s.Anotar("198.51.100.4", "/.env");
        await resumen.Resumir(default);
        var msg = factory.Avisos.Enviados.Last();
        Assert.Contains("3 sonda", msg);
        Assert.Contains("203.0.113.9 (2)", msg);
    }
}

public class LatidoTests
{
    private sealed class Respuesta(HttpStatusCode codigo) : HttpMessageHandler, IHttpClientFactory
    {
        public List<Uri> Visitadas { get; } = [];
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Visitadas.Add(req.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(codigo));
        }
    }

    [Fact]
    public async Task El_latido_visita_la_url_y_dice_si_salio_bien()
    {
        var red = new Respuesta(HttpStatusCode.OK);
        var l = new LatidoExterno(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), red, TimeProvider.System, NullLogger<LatidoExterno>.Instance);
        Assert.True(await l.Latir(new Uri("https://hc-ping.com/abc"), default));
        Assert.Equal("https://hc-ping.com/abc", red.Visitadas.Single().ToString());
        Assert.False(await new LatidoExterno(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), new Respuesta(HttpStatusCode.InternalServerError), TimeProvider.System, NullLogger<LatidoExterno>.Instance).Latir(new Uri("https://hc-ping.com/abc"), default));
    }

    [Fact]
    public async Task Sin_url_o_sin_https_no_late()
    {
        foreach (var url in new[] { "", "http://hc-ping.com/abc", "no-es-una-url" })
        {
            var red = new Respuesta(HttpStatusCode.OK);
            var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Healthcheck:Url"] = url }).Build();
            using var l = new LatidoExterno(cfg, red, TimeProvider.System, NullLogger<LatidoExterno>.Instance);
            await l.StartAsync(default);
            await Task.Delay(50);
            await l.StopAsync(default);
            Assert.Empty(red.Visitadas);
        }
    }
}
