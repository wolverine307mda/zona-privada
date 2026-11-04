using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Pc;

namespace Mda.Api.Tests;

// las escenas: ajustes del PC que se aplican de golpe
public class EscenasTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private async Task AgenteConectado() =>
        await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/salud")
        {
            Headers = { { "X-Agente-Token", ApiFactory.Token } },
            Content = JsonContent.Create(new { hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>() }),
        });

    private async Task<List<(string Tipo, string Parametro)>> Pendientes()
    {
        var r = new HttpRequestMessage(HttpMethod.Get, "/api/sistema/agente/ordenes");
        r.Headers.Add("X-Agente-Token", ApiFactory.Token);
        var j = await (await factory.CreateClient().SendAsync(r)).Content.ReadFromJsonAsync<JsonElement>();
        return j.EnumerateArray().Select(o => (o.GetProperty("tipo").GetString()!, o.GetProperty("parametro").GetString()!)).ToList();
    }

    [Fact]
    public void El_resumen_cuenta_lo_que_cambia_la_escena()
    {
        Assert.Equal("plan cine · brillo 40 % · volumen 50 %", Escenas.Resumen(Escenas.Predeterminadas()[0]));
        Assert.Equal("no cambia nada", Escenas.Resumen(new Escena()));
    }

    [Fact]
    public async Task Se_crean_se_validan_y_no_se_repiten_los_nombres()
    {
        var c = await factory.Dentro();
        var ok = await c.PostAsJsonAsync("/api/escenas", new { nombre = "Lectura", icono = "📚", energia = "ahorro", brillo = 30, volumen = 0, concentracion = true });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/escenas", new { nombre = "lectura" })).StatusCode);                     // mismo nombre, otra mayúscula
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/escenas", new { nombre = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/escenas", new { nombre = "Mala", energia = "turbo" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/escenas", new { nombre = "Mala", brillo = 150 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/escenas", new { nombre = "Mala", apagarMinutos = 999 })).StatusCode);
        var id = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/escenas/{id}", new { nombre = "Lectura nocturna", brillo = 20 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/escenas/9999", new { nombre = "x" })).StatusCode);
    }

    [Fact]
    public async Task Aplicar_una_escena_manda_una_orden_por_cada_ajuste_y_sin_agente_avisa()
    {
        var c = await factory.Dentro();
        var id = (await (await c.PostAsJsonAsync("/api/escenas", new { nombre = "Película", energia = "cine", brillo = 35, volumen = 55, despierto = true })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await AgenteConectado();
        await Pendientes();                                                                                       // vacía lo que hubiera
        var r = await c.PostAsync($"/api/escenas/{id}/aplicar", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var pendientes = await Pendientes();
        Assert.Contains(("energia", "cine"), pendientes);
        Assert.Contains(("brillo", "35"), pendientes);
        Assert.Contains(("volumen", "55"), pendientes);
        Assert.Contains(("despierto", "on"), pendientes);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/escenas/9999/aplicar", null)).StatusCode);
    }

    [Fact]
    public async Task Una_escena_que_apaga_el_pc_pide_face_id_y_el_atajo_no_puede_aplicarla()
    {
        var c = await factory.Dentro();
        var id = (await (await c.PostAsJsonAsync("/api/escenas", new { nombre = "A dormir", apagarMinutos = 45 })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await AgenteConectado();
        var sin = await c.PostAsync($"/api/escenas/{id}/aplicar", null);
        Assert.Equal(HttpStatusCode.Forbidden, sin.StatusCode);
        Assert.True((await sin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reautenticar").GetBoolean());

        // el atajo del iPhone (token con permiso «casa») no puede apagar el PC
        var token = (await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "iPhone", alcances = new[] { "casa" } })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var atajo = new HttpRequestMessage(HttpMethod.Post, "/api/atajos/escena") { Content = JsonContent.Create(new { nombre = "a dormir" }) };
        atajo.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Conflict, (await factory.CreateClient().SendAsync(atajo)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/escenas/{id}/aplicar", null)).StatusCode);
        Assert.Contains(("apagar", "45"), await Pendientes());
    }

    [Fact]
    public async Task El_atajo_aplica_una_escena_por_su_nombre_con_su_token()
    {
        var c = await factory.Dentro();
        await c.PostAsJsonAsync("/api/escenas", new { nombre = "Sofá", brillo = 25 });
        await AgenteConectado(); await Pendientes();
        var token = (await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "iPhone 2", alcances = new[] { "casa" } })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        HttpRequestMessage Peticion(string nombre, string? tk)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "/api/atajos/escena") { Content = JsonContent.Create(new { nombre }) };
            if (tk is not null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tk);
            return r;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(Peticion("sofá", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().SendAsync(Peticion("no existe", token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().SendAsync(Peticion("SOFÁ", token))).StatusCode);
        Assert.Contains(("brillo", "25"), await Pendientes());
    }
}

// su propia clase (y su propia base): las demás pruebas crean escenas y entonces ya no se siembran las de ejemplo
public class EscenasSemillaTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task La_primera_vez_hay_tres_de_ejemplo_y_quitarlas_todas_no_las_vuelve_a_crear()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/escenas")).StatusCode);
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/escenas");
        var nombres = lista.EnumerateArray().Select(e => e.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("Cine", nombres); Assert.Contains("Trabajo", nombres); Assert.Contains("Dormir", nombres);
        foreach (var e in lista.EnumerateArray()) Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/escenas/{e.GetProperty("id").GetInt32()}")).StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/escenas")).GetArrayLength());
    }
}
