using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Atajos;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Mda.Api.Habitos;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class HoyUnitTests
{
    private static readonly HabitosHoy SinHabitos = new(0, 0, []);

    [Fact]
    public void Las_lineas_cuentan_gasto_presupuestos_agenda_habitos_y_salida()
    {
        var l = Hoy.Lineas(12.5m, [new PresupuestoCerca("ocio", "Ocio", 85, 68, 80), new PresupuestoCerca("comida", "Comida", 104, 208, 200)], "Hoy tienes:\n⏰ 10:00 Llamar al taller",
            new HabitosHoy(3, 1, [new HabitoPendiente(1, "Meditar"), new HabitoPendiente(2, "Leer")]), "jue 5 19:40 · Mirador · hora dorada de la tarde 8/10");
        Assert.Contains("Gastado hoy: " + Cartera.Euros(12.5m), l[0]);
        Assert.Contains("Ocio al 85 % del presupuesto", l);
        Assert.Contains("Comida: te has pasado (104 %)", l);
        Assert.Contains("Hoy tienes: ⏰ 10:00 Llamar al taller", l);                     // el salto de línea de la agenda pasa a «·»
        Assert.Contains("Hábitos: 1 de 3 · faltan Meditar, Leer", l);
        Assert.Contains("Mejor salida de la semana: jue 5 19:40 · Mirador · hora dorada de la tarde 8/10", l);
    }

    [Fact]
    public void Sin_nada_dice_que_no_has_gastado_y_no_inventa_lineas()
    {
        var l = Hoy.Lineas(0, [], "Hoy lo tienes libre", SinHabitos, null);
        Assert.Equal(["Hoy no has gastado nada", "Hoy lo tienes libre"], l);
    }

    [Fact]
    public void Con_todos_los_habitos_hechos_lo_dice_y_con_muchos_pendientes_recorta()
    {
        Assert.Contains("Hábitos: todo hecho (2)", Hoy.Lineas(0, [], "x", new HabitosHoy(2, 2, []), null));
        var muchos = Enumerable.Range(1, 6).Select(i => new HabitoPendiente(i, "H" + i)).ToList();
        Assert.Contains("Hábitos: 0 de 6 · faltan H1, H2, H3, H4…", Hoy.Lineas(0, [], "x", new HabitosHoy(6, 0, muchos), null));
    }
}

public class HoyApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task La_pantalla_de_hoy_pide_sesion_y_junta_gasto_presupuesto_y_habitos()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/hoy")).StatusCode);
        var c = await factory.Dentro();
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid")).DateTime);
            db.Presupuestos.Add(new Presupuesto { Categoria = "restaurantes", Limite = 50 });
            db.Movimientos.Add(new Movimiento { Tipo = TipoMovimiento.Gasto, Fecha = hoy, Importe = 45, Categoria = "restaurantes", Concepto = "Cena" });
            await db.SaveChangesAsync();
        }
        await c.PostAsJsonAsync("/api/habitos", new { nombre = "Meditar" });
        var j = await c.GetFromJsonAsync<JsonElement>("/api/hoy");
        Assert.Equal(45m, j.GetProperty("gastoHoy").GetDecimal());
        var pres = j.GetProperty("presupuestos");
        Assert.Equal(1, pres.GetArrayLength());
        Assert.Equal(90, pres[0].GetProperty("porcentaje").GetInt32());
        Assert.Equal(1, j.GetProperty("habitos").GetProperty("total").GetInt32());
        Assert.Equal("Meditar", j.GetProperty("habitos").GetProperty("pendientes")[0].GetProperty("nombre").GetString());
        Assert.True(j.GetProperty("lineas").GetArrayLength() >= 3);
    }

    [Fact]
    public async Task El_widget_del_iphone_necesita_un_token_con_permiso_de_lectura()
    {
        var c = await factory.Dentro();
        string Token(JsonElement r) => r.GetProperty("token").GetString()!;
        var sinLectura = Token(await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "solo gasto", alcances = new[] { "gasto" } })).Content.ReadFromJsonAsync<JsonElement>());
        var conLectura = Token(await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "widget", alcances = new[] { "lectura" } })).Content.ReadFromJsonAsync<JsonElement>());

        HttpRequestMessage Pedir(string? t) { var r = new HttpRequestMessage(HttpMethod.Get, "/api/atajos/hoy"); if (t is not null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t); return r; }
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.SendAsync(Pedir(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.SendAsync(Pedir(sinLectura))).StatusCode);          // el permiso de gasto no lee
        var ok = await anonimo.SendAsync(Pedir(conLectura));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True((await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lineas").GetArrayLength() >= 1);
    }
}
