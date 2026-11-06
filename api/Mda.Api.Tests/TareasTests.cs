using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Avisos;

namespace Mda.Api.Tests;

public class TareasApiTests
{
    private static readonly string Ayer = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-dd"), Lejos = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Crear_completar_reabrir_y_borrar()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var r = await c.PostAsJsonAsync("/api/tareas", new { titulo = "Llamar al seguro", prioridad = 2 });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var hecha = await (await c.PutAsJsonAsync($"/api/tareas/{id}", new { hecha = true })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(hecha.GetProperty("hecha").GetBoolean());
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/tareas")).GetArrayLength());
        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>("/api/tareas?hechas=true")).GetArrayLength());

        var reabierta = await (await c.PutAsJsonAsync($"/api/tareas/{id}", new { hecha = false })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(reabierta.GetProperty("hecha").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/tareas/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/tareas/{id}")).StatusCode);
    }

    [Fact]
    public async Task Las_vencidas_van_primero_y_se_marcan()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        await c.PostAsJsonAsync("/api/tareas", new { titulo = "Sin fecha" });
        await c.PostAsJsonAsync("/api/tareas", new { titulo = "Lejana", vence = Lejos });
        await c.PostAsJsonAsync("/api/tareas", new { titulo = "Atrasada", vence = Ayer });

        var lista = await c.GetFromJsonAsync<JsonElement>("/api/tareas");
        Assert.Equal("Atrasada", lista[0].GetProperty("titulo").GetString());
        Assert.True(lista[0].GetProperty("vencida").GetBoolean());
        Assert.Equal("Lejana", lista[1].GetProperty("titulo").GetString());
        Assert.Equal("Sin fecha", lista[2].GetProperty("titulo").GetString());

        var resumen = await c.GetFromJsonAsync<JsonElement>("/api/tareas/resumen");
        Assert.Equal(3, resumen.GetProperty("pendientes").GetInt32());
        Assert.Equal(1, resumen.GetProperty("vencidas").GetInt32());
    }

    [Fact]
    public async Task Quitar_la_fecha_con_cadena_vacia_y_validaciones()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var id = (await (await c.PostAsJsonAsync("/api/tareas", new { titulo = "Con fecha", vence = Lejos })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var sin = await (await c.PutAsJsonAsync($"/api/tareas/{id}", new { vence = "" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, sin.GetProperty("vence").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/tareas", new { titulo = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/tareas", new { titulo = "x", prioridad = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/tareas", new { titulo = "x", vence = "mañana" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/tareas/9999", new { titulo = "nada" })).StatusCode);
    }

    [Fact]
    public async Task Vaciar_las_hechas_solo_borra_las_hechas()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var a = (await (await c.PostAsJsonAsync("/api/tareas", new { titulo = "A" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await c.PostAsJsonAsync("/api/tareas", new { titulo = "B" });
        await c.PutAsJsonAsync($"/api/tareas/{a}", new { hecha = true });

        var r = await (await c.DeleteAsync("/api/tareas/hechas")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, r.GetProperty("borradas").GetInt32());
        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>("/api/tareas")).GetArrayLength());
    }

    [Fact]
    public async Task Sin_sesion_no_hay_tareas()
    {
        using var f = new ModulosFactory();
        using var anonimo = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/tareas")).StatusCode);
    }
}

public class VigilanciaTests
{
    [Fact]
    public void Anota_el_ultimo_resultado_y_cuenta_los_fallos()
    {
        var r = new RegistroVigilancia(TimeProvider.System);
        r.Anotar("copias");
        r.Anotar("coche", new InvalidOperationException("roto"));
        r.Anotar("coche", new InvalidOperationException("roto otra vez"));
        r.Anotar("coche");
        var coche = r.Pasos().Single(p => p.Nombre == "coche");
        Assert.True(coche.UltimoOk);
        Assert.Equal(2, coche.Fallos);
        Assert.Equal("roto otra vez", coche.UltimoError);
        Assert.Equal(2, r.Pasos().Count);
    }

    [Fact]
    public void Cuenta_los_errores_de_la_ventana()
    {
        var r = new RegistroVigilancia(TimeProvider.System);
        for (var i = 0; i < 3; i++) r.Error5xx();
        Assert.Equal(3, r.Errores5xx(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public async Task Avisa_al_pasar_el_umbral_y_calla_durante_una_hora()
    {
        var avisos = new AvisosFalsos();
        var r = new RegistroVigilancia(TimeProvider.System);
        var a = new AlertaErrores(r, avisos, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<AlertaErrores>.Instance);
        for (var i = 0; i < AlertaErrores.Umbral - 1; i++) r.Error5xx();
        await a.Revisar(CancellationToken.None);
        Assert.Equal(0, avisos.Telegram);
        r.Error5xx();
        await a.Revisar(CancellationToken.None);
        await a.Revisar(CancellationToken.None);
        Assert.Equal(1, avisos.Telegram);
    }

    private sealed class AvisosFalsos : IAvisos
    {
        public int Telegram;
        public Canales Disponibles() => throw new NotImplementedException();
        public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorTelegram(string texto, CancellationToken ct = default) { Telegram++; return Task.FromResult(true); }
    }
}
