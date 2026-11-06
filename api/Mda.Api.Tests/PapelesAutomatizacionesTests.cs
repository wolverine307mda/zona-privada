using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Automatizaciones;
using Mda.Api.Data;
using Mda.Api.Papeles;
using Mda.Api.Tareas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Tests;

public class TareasRepetidasTests
{
    [Theory]
    [InlineData("Diaria", "2026-11-05", "2026-11-06", "2026-11-07")]
    [InlineData("Diaria", "2026-11-01", "2026-11-06", "2026-11-07")]     // completada con retraso: no se queda atrasada
    [InlineData("Semanal", "2026-11-02", "2026-11-06", "2026-11-09")]
    [InlineData("Mensual", "2026-10-31", "2026-11-06", "2026-11-30")]
    [InlineData("Anual", "2026-11-03", "2026-11-06", "2027-11-03")]
    public void La_siguiente_fecha_siempre_queda_por_delante_de_hoy(string repite, string vence, string hoy, string esperada)
        => Assert.Equal(DateOnly.Parse(esperada), ServicioTareas.Siguiente(DateOnly.Parse(vence), repite, DateOnly.Parse(hoy)));

    [Fact]
    public async Task Al_completar_una_que_se_repite_nace_la_siguiente()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var vence = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        var id = (await (await c.PostAsJsonAsync("/api/tareas", new { titulo = "Regar", vence, repite = "Semanal" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await c.PutAsJsonAsync($"/api/tareas/{id}", new { hecha = true });

        var pendientes = await c.GetFromJsonAsync<JsonElement>("/api/tareas");
        Assert.Equal(1, pendientes.GetArrayLength());
        Assert.Equal("Regar", pendientes[0].GetProperty("titulo").GetString());
        Assert.Equal("Semanal", pendientes[0].GetProperty("repite").GetString());
        Assert.False(pendientes[0].GetProperty("vencida").GetBoolean());
        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>("/api/tareas?hechas=true")).GetArrayLength());
    }

    [Fact]
    public async Task Una_que_se_repite_necesita_fecha_y_la_repeticion_debe_ser_valida()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/tareas", new { titulo = "x", repite = "Semanal" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/tareas", new { titulo = "x", vence = "2026-12-01", repite = "Quincenal" })).StatusCode);
    }
}

public class PapelesTests
{
    [Theory]
    [InlineData(-1, 30, 3)]
    [InlineData(0, 30, 2)]
    [InlineData(7, 30, 2)]
    [InlineData(8, 30, 1)]
    [InlineData(30, 30, 1)]
    [InlineData(31, 30, 0)]
    [InlineData(20, 3, 0)]
    public void El_nivel_de_aviso_depende_de_los_dias_que_quedan(int dias, int avisarDias, int nivel)
        => Assert.Equal(nivel, ServicioPapeles.Nivel(dias, avisarDias));

    [Fact]
    public async Task Crear_ordenar_renovar_y_borrar()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        string En(int dias) => DateTime.UtcNow.AddDays(dias).ToString("yyyy-MM-dd");
        await c.PostAsJsonAsync("/api/papeles", new { nombre = "Seguro", categoria = "Seguro", vence = En(100) });
        var r = await c.PostAsJsonAsync("/api/papeles", new { nombre = "DNI", categoria = "Identidad", vence = En(10) });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var lista = (await c.GetFromJsonAsync<JsonElement>("/api/papeles")).GetProperty("papeles");
        Assert.Equal("DNI", lista[0].GetProperty("nombre").GetString());
        Assert.Equal("pronto", lista[0].GetProperty("estado").GetString());
        Assert.Equal("bien", lista[1].GetProperty("estado").GetString());

        var renovado = await (await c.PutAsJsonAsync($"/api/papeles/{id}", new { vence = En(3650) })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("bien", renovado.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/papeles/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/papeles/{id}")).StatusCode);
    }

    [Fact]
    public async Task Validaciones()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/papeles", new { nombre = "", vence = "2027-01-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/papeles", new { nombre = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/papeles", new { nombre = "x", vence = "2027-01-01", categoria = "Inventada" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/papeles", new { nombre = "x", vence = "2027-01-01", avisarDias = 0 })).StatusCode);
    }

    [Fact]
    public async Task El_vigilante_avisa_una_vez_por_nivel()
    {
        using var f = new ModulosFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var v = f.Services.GetRequiredService<Mda.Api.Avisos.Vigilante>();
        db.Papeles.Add(new Papel { Nombre = "ITV", Categoria = "Coche", Vence = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), AvisarDias = 30 });
        await db.SaveChangesAsync();

        await v.PapelesPorCaducar(db, default);
        await v.PapelesPorCaducar(db, default);   // igual: no se repite
        Assert.Single(f.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("ITV"));
    }
}

public class AutomatizacionesTests
{
    [Fact]
    public void Cumple_segun_el_sentido_del_disparador()
    {
        var disco = Disparadores.Buscar("disco")!;
        Assert.True(Disparadores.Cumple(disco, 90, 90));
        Assert.True(Disparadores.Cumple(disco, 90, 95));
        Assert.False(Disparadores.Cumple(disco, 90, 89.9));
        Assert.Null(Disparadores.Buscar("inventado"));
    }

    [Fact]
    public async Task Crear_ver_probar_y_borrar_por_la_api()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var r = await c.PostAsJsonAsync("/api/automatizaciones", new { nombre = "Disco lleno", disparador = "tareas-vencidas", umbral = 1, accion = "avisar" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var todo = await c.GetFromJsonAsync<JsonElement>("/api/automatizaciones");
        Assert.True(todo.GetProperty("disparadores").GetArrayLength() >= 5);
        Assert.Equal(1, todo.GetProperty("reglas").GetArrayLength());

        var p = await c.PostAsync($"/api/automatizaciones/{id}/probar", null);
        Assert.Equal(HttpStatusCode.OK, p.StatusCode);
        Assert.Contains(f.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("(prueba)"));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/automatizaciones/{id}")).StatusCode);
    }

    [Fact]
    public async Task Validaciones()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/automatizaciones", new { nombre = "x", disparador = "marte", umbral = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/automatizaciones", new { nombre = "x", disparador = "disco" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/automatizaciones", new { nombre = "x", disparador = "disco", umbral = 90, accion = "apagar" })).StatusCode);
    }

    [Fact]
    public async Task El_motor_crea_una_tarea_una_sola_vez_y_respeta_la_espera()
    {
        using var f = new ModulosFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var motor = f.Services.GetRequiredService<MotorAutomatizaciones>();
        db.Tareas.Add(new Tarea { Titulo = "Vieja", Vence = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), Creada = DateTimeOffset.UtcNow });
        db.Automatizaciones.Add(new Automatizacion { Nombre = "Demasiadas vencidas", Disparador = "tareas-vencidas", Umbral = 1, Accion = "tarea", EsperaHoras = 24 });
        await db.SaveChangesAsync();

        await motor.Vuelta(db, default);
        await motor.Vuelta(db, default);   // dentro de la espera: nada
        Assert.Equal(1, await db.Tareas.CountAsync(t => t.Titulo == "Demasiadas vencidas"));
        Assert.NotNull((await db.Automatizaciones.SingleAsync()).Ultima);
    }

    [Fact]
    public async Task Una_regla_que_no_se_cumple_no_hace_nada()
    {
        using var f = new ModulosFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var motor = f.Services.GetRequiredService<MotorAutomatizaciones>();
        db.Automatizaciones.Add(new Automatizacion { Nombre = "Sin tareas vencidas", Disparador = "tareas-vencidas", Umbral = 1, Accion = "avisar" });
        await db.SaveChangesAsync();
        await motor.Vuelta(db, default);
        Assert.DoesNotContain(f.Avisos.Enviados, m => m.Contains("Sin tareas vencidas"));
        Assert.Null((await db.Automatizaciones.SingleAsync()).Ultima);
    }
}
