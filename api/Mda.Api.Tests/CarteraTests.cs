using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Gastos;

namespace Mda.Api.Tests;

public class CarteraTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static string Hoy => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

    [Fact]
    public async Task El_saldo_se_fija_hoy_y_lo_nuevo_lo_mueve_incluso_a_negativo()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/gastos/saldo", new { actual = 354.36 })).StatusCode);
        Assert.Equal(354.36m, (await c.GetFromJsonAsync<JsonElement>("/api/gastos/saldo")).GetProperty("actual").GetDecimal());

        await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = Hoy, importe = 400, categoria = "ocio", concepto = "Concierto" });
        var s = await c.GetFromJsonAsync<JsonElement>("/api/gastos/saldo");
        Assert.Equal(-45.64m, s.GetProperty("actual").GetDecimal());
        Assert.True(s.GetProperty("configurado").GetBoolean());

        await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Ingreso", fecha = Hoy, importe = 100, categoria = "extra", concepto = "Bizum" });
        Assert.Equal(54.36m, (await c.GetFromJsonAsync<JsonElement>("/api/gastos/saldo")).GetProperty("actual").GetDecimal());
    }

    [Fact]
    public async Task Un_ingreso_que_se_repite_crea_su_recurrente_y_no_queda_pendiente_este_mes()
    {
        var c = await factory.Dentro();
        var r = await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Ingreso", fecha = Hoy, importe = 1200, categoria = "nomina", concepto = "Nómina", repite = "Mensual", variable = true });
        var m = await r.Content.ReadFromJsonAsync<JsonElement>();
        var idRec = m.GetProperty("recurrenteId").GetInt32();
        var rec = (await c.GetFromJsonAsync<JsonElement>("/api/gastos/recurrentes")).EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == idRec);
        Assert.True(rec.GetProperty("variable").GetBoolean());
        Assert.Equal("Ingreso", rec.GetProperty("tipo").GetString());
        var hoy = DateTime.Today;
        var mes = await c.GetFromJsonAsync<JsonElement>($"/api/gastos/mes/{hoy.Year}/{hoy.Month}");
        Assert.DoesNotContain(mes.GetProperty("pendientes").EnumerateArray(), p => p.GetProperty("recurrenteId").GetInt32() == idRec);
        // el mes que viene sí toca, con el importe que se ajuste al aplicarlo
        var sig = hoy.AddMonths(1);
        var mesSig = await c.GetFromJsonAsync<JsonElement>($"/api/gastos/mes/{sig.Year}/{sig.Month}");
        Assert.Contains(mesSig.GetProperty("pendientes").EnumerateArray(), p => p.GetProperty("recurrenteId").GetInt32() == idRec);
        var ap = await c.PostAsJsonAsync($"/api/gastos/recurrentes/{idRec}/registrar", new { importe = 1250.5, fecha = new DateOnly(sig.Year, sig.Month, 1).ToString("yyyy-MM-dd") });
        Assert.Equal(1250.5m, (await ap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("importe").GetDecimal());
    }

    [Fact]
    public async Task El_presupuesto_avisa_al_80_y_al_100_una_sola_vez()
    {
        var c = await factory.Dentro();
        await c.PutAsJsonAsync("/api/gastos/presupuestos/restaurantes", new { limite = 100 });
        int Avisos() { lock (factory.Avisos.Enviados) return factory.Avisos.Enviados.Count(x => x.Contains("Restaurantes")); }
        var antes = Avisos();
        await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = Hoy, importe = 85, categoria = "restaurantes", concepto = "Cena" });
        await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = Hoy, importe = 5, categoria = "restaurantes", concepto = "Café" });
        await Task.Delay(300);
        Assert.Equal(antes + 3, Avisos()); // push, Telegram y correo del 80 %, solo una vez
        await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = Hoy, importe = 20, categoria = "restaurantes", concepto = "Comida" });
        await Task.Delay(300);
        Assert.Equal(antes + 6, Avisos());
        var p = (await c.GetFromJsonAsync<JsonElement>("/api/gastos/presupuestos")).EnumerateArray().Single(x => x.GetProperty("categoria").GetString() == "restaurantes");
        Assert.Equal(110m, p.GetProperty("gastado").GetDecimal());
    }

    [Fact]
    public async Task Las_huchas_guardan_y_no_dejan_sacar_mas_de_lo_que_hay()
    {
        var c = await factory.Dentro();
        var h = await (await c.PostAsJsonAsync("/api/gastos/huchas", new { nombre = "Viaje", objetivo = 600, emoji = "✈️" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = h.GetProperty("id").GetInt32();
        await c.PostAsJsonAsync($"/api/gastos/huchas/{id}/aportar", new { importe = 150 });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/gastos/huchas/{id}/aportar", new { importe = -200 })).StatusCode);
        var l = (await c.GetFromJsonAsync<JsonElement>("/api/gastos/huchas")).EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal(25, l.GetProperty("porcentaje").GetDouble());
    }

    [Fact]
    public async Task El_ticket_se_adjunta_se_lee_y_se_borra()
    {
        var c = await factory.Dentro();
        var m = await (await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = Hoy, importe = 12, categoria = "supermercado", concepto = "Mercadona" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = m.GetProperty("id").GetInt32();
        var foto = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);
        foto.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/api/gastos/movimientos/{id}/ticket", foto)).StatusCode);
        var texto = new StringContent("hola");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PutAsync($"/api/gastos/movimientos/{id}/ticket", texto)).StatusCode);
        var r = await c.GetAsync($"/api/gastos/movimientos/{id}/ticket");
        Assert.Equal("image/jpeg", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(7, (await r.Content.ReadAsByteArrayAsync()).Length);
        await c.DeleteAsync($"/api/gastos/movimientos/{id}/ticket");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/gastos/movimientos/{id}/ticket")).StatusCode);
    }

    [Fact]
    public void El_mantenimiento_avisa_por_km_o_por_fecha()
    {
        var hoy = new DateOnly(2026, 10, 31);
        var aceite = new Mantenimiento { Nombre = "Aceite", CadaKm = 15000, CadaMeses = 12, UltimoKm = 10000, UltimaFecha = new DateOnly(2026, 3, 1) };
        Assert.Equal("ok", Coche.EstadoDe(aceite, 20000, hoy).Nivel);
        Assert.Equal("pronto", Coche.EstadoDe(aceite, 24500, hoy).Nivel);
        Assert.Equal("vencido", Coche.EstadoDe(aceite, 25100, hoy).Nivel);
        var itv = new Mantenimiento { Nombre = "ITV", CadaMeses = 24, UltimaFecha = new DateOnly(2024, 11, 15) };
        var e = Coche.EstadoDe(itv, null, hoy);
        Assert.Equal("pronto", e.Nivel);
        Assert.Equal(15, e.FaltanDias);
        Assert.Equal("sin-datos", Coche.EstadoDe(new Mantenimiento { Nombre = "Ruedas", CadaKm = 40000 }, 1000, hoy).Nivel);
    }

    [Fact]
    public async Task El_perfil_guarda_nombre_correo_y_telefono_sin_cerrar_la_sesion()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/perfil", new { nombre = "Mario", email = "no-es-correo", telefono = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/perfil", new { nombre = "Mario", apellidos = "Domingo Álvarez", email = "mario@prueba.local", telefono = "691 01 34 87" })).StatusCode);
        var p = await c.GetFromJsonAsync<JsonElement>("/api/perfil");
        Assert.Equal("Mario", p.GetProperty("nombre").GetString());
        Assert.Equal("Domingo Álvarez", p.GetProperty("apellidos").GetString());
        Assert.Equal("691 01 34 87", p.GetProperty("telefono").GetString());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/gastos/saldo")).StatusCode);
    }

    [Fact]
    public void Los_consejos_calculan_lo_que_queda_por_dia()
    {
        var hoy = new DateOnly(2026, 10, 21); // quedan 11 días contando hoy
        var movs = new List<Movimiento>
        {
            new() { Tipo = TipoMovimiento.Gasto, Fecha = new DateOnly(2026, 9, 10), Importe = 100, Categoria = "restaurantes" },
            new() { Tipo = TipoMovimiento.Gasto, Fecha = new DateOnly(2026, 10, 5), Importe = 300, Categoria = "restaurantes" },
        };
        var rec = new List<Recurrente> { new() { Id = 1, Nombre = "Alquiler", Tipo = TipoMovimiento.Gasto, Categoria = "vivienda", Importe = 100, Dia = 25, Inicio = new DateOnly(2026, 1, 1) } };
        var r = Cartera.ConsejosDe(hoy, movs, rec, 1200, 0);
        Assert.Equal(11, r.DiasRestantes);
        Assert.Equal(109.09m, r.PorDia); // 1200 / 11: el alquiler pendiente NO se descuenta hasta que se apunte
        Assert.Contains(r.Lista, x => x.Icono == "sube");
    }
}
