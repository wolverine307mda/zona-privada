using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Gastos;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mda.Api.Tests;

public class CalculosGastosTests
{
    private static Movimiento G(int dia, decimal importe, string cat = "supermercado", int mes = 10) =>
        new() { Tipo = TipoMovimiento.Gasto, Fecha = new DateOnly(2026, mes, dia), Importe = importe, Categoria = cat, Concepto = cat };
    private static Movimiento I(int dia, decimal importe, int mes = 10) =>
        new() { Tipo = TipoMovimiento.Ingreso, Fecha = new DateOnly(2026, mes, dia), Importe = importe, Categoria = "nomina", Concepto = "Nómina" };

    [Theory]
    [InlineData(Frecuencia.Mensual, 2026, 3, true)]
    [InlineData(Frecuencia.Trimestral, 2026, 4, true)]   // enero, abril, julio, octubre
    [InlineData(Frecuencia.Trimestral, 2026, 5, false)]
    [InlineData(Frecuencia.Anual, 2027, 1, true)]
    [InlineData(Frecuencia.Anual, 2026, 6, false)]
    [InlineData(Frecuencia.Mensual, 2025, 12, false)]   // antes de empezar
    public void Un_fijo_toca_segun_su_frecuencia(Frecuencia f, int anio, int mes, bool toca)
    {
        var r = new Recurrente { Frecuencia = f, Inicio = new DateOnly(2026, 1, 15), Importe = 120 };
        Assert.Equal(toca, r.TocaEn(anio, mes));
    }

    [Fact]
    public void Un_fijo_terminado_o_pausado_no_toca()
    {
        Assert.False(new Recurrente { Inicio = new DateOnly(2026, 1, 1), Fin = new DateOnly(2026, 5, 31) }.TocaEn(2026, 6));
        Assert.False(new Recurrente { Inicio = new DateOnly(2026, 1, 1), Activo = false }.TocaEn(2026, 6));
    }

    [Fact]
    public void El_mes_suma_compara_y_lista_los_fijos_pendientes()
    {
        var alquiler = new Recurrente { Id = 1, Nombre = "Alquiler", Tipo = TipoMovimiento.Gasto, Categoria = "vivienda", Importe = 700, Dia = 31, Inicio = new DateOnly(2026, 1, 1) };
        var netflix = new Recurrente { Id = 2, Nombre = "Netflix", Tipo = TipoMovimiento.Gasto, Categoria = "suscripciones", Importe = 12.99m, Dia = 5, Inicio = new DateOnly(2026, 1, 1) };
        var seguro = new Recurrente { Id = 3, Nombre = "Seguro", Tipo = TipoMovimiento.Gasto, Categoria = "seguro", Importe = 360, Frecuencia = Frecuencia.Anual, Inicio = new DateOnly(2026, 3, 1) };
        var netflixPagado = G(5, 12.99m, "suscripciones", mes: 2);
        netflixPagado.RecurrenteId = 2;

        var r = Calculos.Mes(2026, 2, [I(1, 1500, 2), G(3, 100, mes: 2), netflixPagado], [I(1, 1400, 1), G(9, 200, mes: 1)], [alquiler, netflix, seguro]);

        Assert.Equal(1500, r.Ingresos);
        Assert.Equal(112.99m, r.Gastos);
        Assert.Equal(1387.01m, r.Ahorro);
        Assert.Equal(92.5, r.TasaAhorro);
        Assert.Equal(1200, r.Anterior.Ahorro);
        Assert.Equal(28, r.GastoPorDia.Count);
        // febrero: el alquiler del día 31 cae el 28; Netflix ya está apuntado; el seguro anual es en marzo
        var p = Assert.Single(r.Pendientes);
        Assert.Equal("Alquiler", p.Nombre);
        Assert.Equal(new DateOnly(2026, 2, 28), p.Fecha);
        Assert.Equal(712.99m, r.FijosDelMes);
        Assert.Equal(new Suscripciones(12.99m, 155.88m, 1), r.Suscripciones);
    }

    [Fact]
    public void El_consumo_sale_por_el_metodo_del_deposito_lleno()
    {
        Movimiento R(int id, int dia, decimal litros, decimal precio, int km) => new()
        {
            Id = id, Tipo = TipoMovimiento.Gasto, Categoria = "gasolina", Concepto = "Repsol",
            Fecha = new DateOnly(2026, 10, dia), Litros = litros, PrecioLitro = precio, Importe = Math.Round(litros * precio, 2), Kilometros = km,
        };

        var g = Calculos.GasolinaDe([R(1, 1, 40, 1.50m, 10_000), R(2, 10, 30, 1.60m, 10_500), R(3, 20, 35, 1.55m, 11_000)], 2026);

        Assert.Equal(3, g.Repostajes.Count);
        Assert.Null(g.Repostajes[^1].Consumo);                  // el primero no tiene con qué comparar
        Assert.Equal(6.0, g.Repostajes.Single(r => r.Id == 2).Consumo); // 30 L en 500 km
        Assert.Equal(7.0, g.Repostajes.Single(r => r.Id == 3).Consumo); // 35 L en 500 km
        Assert.Equal(6.5, g.ConsumoMedio);
        Assert.Equal(1000, g.KmRecorridos);
        Assert.Equal(105, g.Litros);
        Assert.Equal(1.55m, g.UltimoPrecio);
    }

    [Fact]
    public void Las_estadisticas_acumulan_el_ahorro()
    {
        var e = Calculos.Periodo(2026, 3, 3, [I(1, 1000, 1), G(2, 400, mes: 1), I(1, 1000, 2), G(2, 1200, mes: 2), I(1, 1000, 3), G(2, 300, mes: 3)]);
        Assert.Equal([600m, -200m, 700m], e.Serie.Select(p => p.Ahorro));
        Assert.Equal(1100, e.Serie[^1].AhorroAcumulado);
        Assert.Equal(3, e.MejorMes!.Mes);
        Assert.Equal(2, e.PeorMes!.Mes);
    }
}

public class GastosApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> Dentro()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password });
        return client;
    }

    [Fact]
    public async Task Sin_sesion_no_hay_gastos()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/gastos/mes/2026/10")).StatusCode);
    }

    [Fact]
    public async Task Rechaza_apuntes_mal_hechos()
    {
        var c = await Dentro();
        var mal = await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = "2026-10-01", importe = -5, categoria = "nomina", concepto = "" });
        Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);
        var errores = (await mal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errores.TryGetProperty("importe", out _));
        Assert.True(errores.TryGetProperty("categoria", out _));
        Assert.True(errores.TryGetProperty("concepto", out _));
    }

    [Fact]
    public async Task La_gasolina_calcula_el_importe_con_litros_y_precio()
    {
        var c = await Dentro();
        var r = await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = "2026-10-02", importe = 0, categoria = "gasolina", concepto = "Repsol", litros = 40, precioLitro = 1.499 });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(59.96m, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("importe").GetDecimal());
    }

    [Fact]
    public async Task Un_fijo_sale_pendiente_hasta_registrarlo_y_solo_una_vez()
    {
        var c = await Dentro();
        var alta = await c.PostAsJsonAsync("/api/gastos/recurrentes", new
        {
            nombre = "Spotify", tipo = "Gasto", categoria = "suscripciones", importe = 10.99, variable = false, dia = 3, frecuencia = "Mensual", inicio = "2026-01-01",
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var id = (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        async Task<bool> Pendiente() => (await c.GetFromJsonAsync<JsonElement>("/api/gastos/mes/2026/7")).GetProperty("pendientes")
            .EnumerateArray().Any(p => p.GetProperty("recurrenteId").GetInt32() == id);

        Assert.True(await Pendiente());
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync($"/api/gastos/recurrentes/{id}/registrar", new { fecha = "2026-07-03" })).StatusCode);
        Assert.False(await Pendiente());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/gastos/recurrentes/{id}/registrar", new { fecha = "2026-07-20" })).StatusCode);

        var mes = await c.GetFromJsonAsync<JsonElement>("/api/gastos/mes/2026/7");
        Assert.Contains(mes.GetProperty("movimientos").EnumerateArray(), m => m.GetProperty("concepto").GetString() == "Spotify" && m.GetProperty("importe").GetDecimal() == 10.99m);
    }

    [Fact]
    public async Task Una_suscripcion_cobrada_antes_cubre_su_mes_y_el_siguiente_sale_pendiente()
    {
        var c = await Dentro();
        var alta = await c.PostAsJsonAsync("/api/gastos/recurrentes", new
        {
            nombre = "HBO", tipo = "Gasto", categoria = "suscripciones", importe = 9.99, variable = false, dia = 2, frecuencia = "Mensual", inicio = "2026-01-01",
        });
        var id = (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        async Task<bool> Pendiente(int mes) => (await c.GetFromJsonAsync<JsonElement>($"/api/gastos/mes/2026/{mes}")).GetProperty("pendientes")
            .EnumerateArray().Any(p => p.GetProperty("recurrenteId").GetInt32() == id);

        // te la cobran el 30 de septiembre, antes del 2 de octubre
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync($"/api/gastos/recurrentes/{id}/registrar", new { fecha = "2026-09-30", periodo = "2026-10" })).StatusCode);
        Assert.False(await Pendiente(10));   // octubre ya está cubierto aunque el apunte sea de septiembre
        Assert.True(await Pendiente(9));     // y septiembre sigue con su propia ocurrencia pendiente
        Assert.True(await Pendiente(11));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/gastos/recurrentes/{id}/registrar", new { fecha = "2026-10-02" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/gastos/recurrentes/{id}/registrar", new { fecha = "2026-10-02", periodo = "octubre" })).StatusCode);
    }
}
