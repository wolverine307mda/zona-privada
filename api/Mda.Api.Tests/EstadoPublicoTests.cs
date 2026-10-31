using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mda.Api.Tests;

public class EstadoPublicoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Web = "https://wolverine307mda.github.io";
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= Entrar();

    private async Task<HttpClient> Entrar()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).EnsureSuccessStatusCode();
        return c;
    }

    private static HttpRequestMessage Publica(string ruta)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, ruta);
        r.Headers.Add("Origin", Web);
        return r;
    }

    private async Task Esperar()
    {
        // el monitor mide cada 2 s: hasta que haya una lectura no hay números que enseñar
        var c = await Dentro();
        for (var i = 0; i < 30; i++)
        {
            var s = await c.GetFromJsonAsync<JsonElement>("/api/sistema");
            if (s.GetProperty("actual").ValueKind == JsonValueKind.Object) return;
            await Task.Delay(300);
        }
    }

    [Fact]
    public async Task Por_defecto_la_web_publica_no_ve_nada()
    {
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.SendAsync(Publica("/api/publico/estado"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.SendAsync(Publica("/api/publico/estado/vivo"))).StatusCode);
    }

    [Fact]
    public async Task Los_ajustes_de_lo_publico_piden_sesion()
    {
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/publico-ajustes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync("/api/publico-ajustes", new { estado = true, cpu = true })).StatusCode);
    }

    [Fact]
    public async Task Solo_estado_enseña_en_linea_y_nada_mas()
    {
        var c = await Dentro();
        await c.PutAsJsonAsync("/api/publico-ajustes", new { estado = true, cpu = false });
        var res = await factory.CreateClient().SendAsync(Publica("/api/publico/estado"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(Web, res.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var j = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.True(j.GetProperty("enLinea").GetBoolean());
        Assert.False(j.TryGetProperty("cpu", out _));
        Assert.False(j.TryGetProperty("serie", out _));
    }

    [Fact]
    public async Task Con_la_carga_activada_salen_numeros_redondeados_y_nada_identificativo()
    {
        await Esperar();
        var c = await Dentro();
        await c.PutAsJsonAsync("/api/publico-ajustes", new { estado = true, cpu = true });
        var texto = await (await factory.CreateClient().SendAsync(Publica("/api/publico/estado"))).Content.ReadAsStringAsync();
        var j = JsonDocument.Parse(texto).RootElement;
        Assert.InRange(j.GetProperty("cpu").GetInt32(), 0, 100);
        Assert.InRange(j.GetProperty("ram").GetInt32(), 0, 100);
        Assert.True(j.GetProperty("serie").GetArrayLength() >= 1);
        // nada que identifique el equipo ni su sistema
        foreach (var prohibido in new[] { "equipo", "discos", "sistemaOperativo", "cpuModelo", "nucleos", "disco", "ip", "nombre" })
            Assert.False(j.TryGetProperty(prohibido, out _), prohibido);
        Assert.DoesNotContain(Environment.MachineName, texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task El_directo_manda_eventos_y_se_corta_si_se_apaga_el_interruptor()
    {
        await Esperar();
        var c = await Dentro();
        await c.PutAsJsonAsync("/api/publico-ajustes", new { estado = true, cpu = true });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var res = await factory.CreateClient().SendAsync(Publica("/api/publico/estado/vivo"), HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", res.Content.Headers.ContentType?.MediaType);
        using var lector = new StreamReader(await res.Content.ReadAsStreamAsync(cts.Token));
        var linea = await lector.ReadLineAsync(cts.Token);
        Assert.StartsWith("data: ", linea);
        Assert.True(JsonDocument.Parse(linea![6..]).RootElement.GetProperty("enLinea").GetBoolean());

        await c.PutAsJsonAsync("/api/publico-ajustes", new { estado = false, cpu = false });
        // al apagar, la conexión termina en la siguiente vuelta (≤ 3 s) y deja de haber más eventos
        string? resto;
        do { resto = await lector.ReadLineAsync(cts.Token); } while (resto is not null && resto.Length == 0);
        Assert.Null(resto);
    }
}
