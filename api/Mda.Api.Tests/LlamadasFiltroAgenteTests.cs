using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class LlamadasFiltroAgenteTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private async Task Sembrar()
    {
        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        db.LlamadasExternas.RemoveRange(db.LlamadasExternas);      // cada prueba parte de cero (comparten la API)
        db.LlamadasExternas.AddRange(
            new LlamadaExterna { Metodo = "POST", Ruta = "/api/sistema/agente/salud", Origen = LlamadasExternas.OrigenAgente, Estado = 401, Error = "token" },
            new LlamadaExterna { Metodo = "POST", Ruta = "/api/atajos/nota", Origen = "Atajo · iPhone", Estado = 200 },
            new LlamadaExterna { Metodo = "POST", Ruta = "/api/atajos/gasto", Origen = "Atajo · Mac", Estado = 400, Error = "importe" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Se_pueden_ocultar_las_del_agente_y_el_combo_lista_todos_los_origenes()
    {
        await Sembrar();
        var c = await factory.Dentro();
        var todas = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/llamadas");
        Assert.Equal(3, todas.GetProperty("llamadas").GetArrayLength());
        Assert.Equal(new[] { "Agente del PC", "Atajo · iPhone", "Atajo · Mac" }, todas.GetProperty("origenes").EnumerateArray().Select(o => o.GetString()!).ToArray());

        var sin = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/llamadas?sinAgente=true");
        Assert.Equal(2, sin.GetProperty("llamadas").GetArrayLength());
        Assert.DoesNotContain(sin.GetProperty("llamadas").EnumerateArray(), l => l.GetProperty("origen").GetString() == "Agente del PC");
        Assert.Equal(2, sin.GetProperty("coinciden").GetInt32());

        // por origen, solo ese; y combinado con un fallo
        var iphone = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/llamadas?origen=" + Uri.EscapeDataString("Atajo · iPhone"));
        Assert.Equal(1, iphone.GetProperty("llamadas").GetArrayLength());
        var fallos = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/llamadas?estado=fallos&sinAgente=true");
        Assert.Equal(1, fallos.GetProperty("llamadas").GetArrayLength());
        Assert.Equal("Atajo · Mac", fallos.GetProperty("llamadas")[0].GetProperty("origen").GetString());
    }

    [Fact]
    public async Task Limpiar_con_sin_agente_no_borra_las_del_agente()
    {
        await Sembrar();
        var c = await factory.Dentro();
        var r = await c.DeleteAsync("/api/seguridad/llamadas?sinAgente=true");
        r.EnsureSuccessStatusCode();
        var quedan = await c.GetFromJsonAsync<JsonElement>("/api/seguridad/llamadas");
        Assert.Contains(quedan.GetProperty("llamadas").EnumerateArray(), l => l.GetProperty("origen").GetString() == "Agente del PC");
        Assert.DoesNotContain(quedan.GetProperty("llamadas").EnumerateArray(), l => l.GetProperty("origen").GetString()!.StartsWith("Atajo"));
    }
}
