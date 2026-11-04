using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mda.Api.Tests;

public class EscenasCasaTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static async Task<int> Crear(HttpClient c, string nombre, string tipo) =>
        (await (await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre, tipo, driver = "demo" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

    private static async Task<string?> Valor(HttpClient c, int id, string clave)
    {
        var casa = await c.GetFromJsonAsync<JsonElement>("/api/domotica");
        var a = casa.GetProperty("aparatos").EnumerateArray().First(x => x.GetProperty("id").GetInt32() == id);
        return a.GetProperty("entidades").EnumerateArray().First(e => e.GetProperty("clave").GetString() == clave).GetProperty("valor").GetString();
    }

    [Fact]
    public async Task Una_escena_guarda_como_estan_los_aparatos_y_los_vuelve_a_poner()
    {
        var c = await factory.Dentro();
        var luz = await Crear(c, "Luz escena", "luz");
        await c.GetAsync("/api/domotica");
        await c.PostAsJsonAsync($"/api/domotica/aparatos/{luz}/ejecutar", new { clave = "encendido", valor = true });
        await c.PostAsJsonAsync($"/api/domotica/aparatos/{luz}/ejecutar", new { clave = "brillo", valor = 30 });

        var alta = await c.PostAsJsonAsync("/api/domotica/escenas", new { nombre = "Cine", icono = "🎬", aparatoIds = new[] { luz } });
        Assert.Equal(HttpStatusCode.OK, alta.StatusCode);
        var id = (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // la luz cambia y la escena la devuelve a como estaba
        await c.PostAsJsonAsync($"/api/domotica/aparatos/{luz}/ejecutar", new { clave = "encendido", valor = false });
        await c.PostAsJsonAsync($"/api/domotica/aparatos/{luz}/ejecutar", new { clave = "brillo", valor = 90 });
        var r = await (await c.PostAsync($"/api/domotica/escenas/{id}/aplicar", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(r.GetProperty("hechas").GetInt32() >= 3);   // encendido, brillo y color
        Assert.Equal(0, r.GetProperty("fallos").GetArrayLength());
        Assert.Equal("true", await Valor(c, luz, "encendido"));
        Assert.Equal("30", await Valor(c, luz, "brillo"));
    }

    [Fact]
    public async Task Valida_el_nombre_y_no_admite_repetidos_y_se_puede_borrar()
    {
        var c = await factory.Dentro();
        var luz = await Crear(c, "Luz 2", "luz");
        await c.GetAsync("/api/domotica");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/domotica/escenas", new { nombre = " ", aparatoIds = new[] { luz } })).StatusCode);
        var ok = await c.PostAsJsonAsync("/api/domotica/escenas", new { nombre = "Dormir", aparatoIds = new[] { luz } });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/domotica/escenas", new { nombre = "dormir", aparatoIds = new[] { luz } })).StatusCode);
        var id = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/domotica/escenas/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync($"/api/domotica/escenas/{id}/aplicar", null)).StatusCode);
    }
}
