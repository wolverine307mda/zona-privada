using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Atajos;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class AtajosUnitTests
{
    [Theory]
    [InlineData("MERCADONA 0123 MADRID", "Mercadona Madrid")]
    [InlineData("  Netflix.com  ", "Netflix.com")]
    [InlineData("BP ESTACION 4567*12", "Bp Estacion")]
    [InlineData("", "")]
    public void El_nombre_del_comercio_se_limpia(string entra, string sale) => Assert.Equal(sale, AtajosApi.Limpiar(entra));

    [Fact]
    public void La_categoria_sale_de_lo_que_ya_usas_con_ese_comercio()
    {
        var aprendido = new Dictionary<string, string> { ["mercadona"] = "supermercado", ["bp"] = "gasolina" };
        Assert.Equal("supermercado", AtajosApi.Categoria("Mercadona Madrid", aprendido));
        Assert.Equal("otros", AtajosApi.Categoria("Tienda desconocida", aprendido));
        Assert.Equal("otros", AtajosApi.Categoria("Bp Estacion", aprendido)); // «bp» es demasiado corto para fiarse
    }

    [Theory]
    [InlineData("3,26", 3.26)]
    [InlineData("11,75 €", 11.75)]
    [InlineData("11,75 €", 11.75)]       // espacio de no separación, como lo manda Atajos
    [InlineData("11,75 €", 11.75)]       // espacio fino
    [InlineData("EUR 3.26", 3.26)]
    [InlineData("1.234,56 €", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("-4,50", -4.50)]
    [InlineData("7", 7)]
    public void El_importe_se_entiende_escrito_como_lo_escriba_el_telefono(string texto, double esperado)
    {
        Assert.True(ImporteFlexible.TryParsear(texto, out var v));
        Assert.Equal((decimal)esperado, v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin importe")]
    public void Un_importe_sin_cifras_no_vale(string texto) => Assert.False(ImporteFlexible.TryParsear(texto, out _));

    [Theory]
    [InlineData("81.43.200.225", "81.43.200")]
    [InlineData("2a09:bac2:3131:245a::39f:8f", "2A09BAC23131")]
    public void La_red_es_el_prefijo_no_la_ip_entera(string ip, string red) => Assert.Equal(red, AtajosApi.Red(ip));
}

public class AtajosTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= factory.Dentro();
    private static string Hoy => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

    private async Task<string> Token(params string[] alcances)
    {
        var c = await Dentro();
        var r = await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "iPhone", alcances });
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private HttpClient Con(string? token)
    {
        var c = factory.CreateClient();
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task La_nota_rapida_se_guarda_con_su_etiqueta_y_pide_su_permiso()
    {
        var sin = Con(await Token("gasto"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await sin.PostAsJsonAsync("/api/atajos/nota", new { texto = "comprar pan" })).StatusCode);

        var con = Con(await Token("nota"));
        Assert.Equal(HttpStatusCode.BadRequest, (await con.PostAsJsonAsync("/api/atajos/nota", new { texto = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await con.PostAsJsonAsync("/api/atajos/nota", new { texto = "comprar pan" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var n = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notas.AsNoTracking().FirstAsync(x => x.Contenido == "comprar pan");
        Assert.Equal("atajo", n.Etiquetas);
    }

    [Fact]
    public async Task El_estado_se_lee_con_permiso_de_lectura_y_despertar_pide_un_aparato_que_exista()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Con(await Token("gasto")).GetAsync("/api/atajos/estado")).StatusCode);
        var estado = await Con(await Token("lectura")).GetFromJsonAsync<JsonElement>("/api/atajos/estado");
        Assert.False(string.IsNullOrEmpty(estado.GetProperty("mensaje").GetString()));
        Assert.True(estado.GetProperty("areas").GetArrayLength() >= 5);

        var pc = Con(await Token("pc"));
        Assert.Equal(HttpStatusCode.NotFound, (await pc.PostAsJsonAsync("/api/atajos/despertar", new { nombre = "no existe" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Con(await Token("nota")).PostAsJsonAsync("/api/atajos/despertar", new { nombre = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Con(await Token("nota")).PostAsync("/api/atajos/panico", null)).StatusCode);
    }

    [Fact]
    public async Task Todos_los_errores_de_los_atajos_traen_un_mensaje_legible()
    {
        async Task<string> Mensaje(HttpResponseMessage r, HttpStatusCode esperado)
        {
            Assert.Equal(esperado, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString()!;
        }
        Assert.Contains("Token", await Mensaje(await Con("mda_inventado").PostAsJsonAsync("/api/atajos/movimiento", new { importe = 1, concepto = "x" }), HttpStatusCode.Unauthorized));
        var gasto = Con(await Token("gasto", "pago"));
        Assert.False(string.IsNullOrWhiteSpace(await Mensaje(await gasto.PostAsJsonAsync("/api/atajos/movimiento", new { importe = 0, concepto = "x" }), HttpStatusCode.BadRequest)));
        Assert.False(string.IsNullOrWhiteSpace(await Mensaje(await gasto.PostAsJsonAsync("/api/atajos/pago", new { comercio = "", importe = 0 }), HttpStatusCode.BadRequest)));
        var lectura = Con(await Token("lectura"));
        Assert.Contains("Libre", (await lectura.GetFromJsonAsync<JsonElement>("/api/atajos/resumen")).GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task El_importe_puede_llegar_como_texto_con_coma_y_un_cuerpo_roto_tiene_mensaje()
    {
        var c = Con(await Token("gasto"));
        var r = await c.PostAsJsonAsync("/api/atajos/movimiento", new { importe = "3,26", tipo = "Gasto", concepto = "Chuches" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("3,26", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString());
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/atajos/movimiento", new { importe = "1.234,50 €", concepto = "Pago" })).StatusCode);

        var roto = await c.PostAsync("/api/atajos/movimiento", new StringContent("{importe: ", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, roto.StatusCode);
        // (el mensaje de un cuerpo roto solo se ve en producción: en desarrollo el framework escribe el detalle de la excepción)
    }

    [Fact]
    public async Task El_token_se_ve_una_vez_y_se_guarda_solo_su_huella()
    {
        var token = await Token("gasto");
        Assert.StartsWith("mda_", token);
        Assert.True(token.Length >= 40);
        var c = await Dentro();
        var lista = await c.GetStringAsync("/api/atajos-tokens");
        Assert.DoesNotContain(token, lista);
        using var scope = factory.Services.CreateScope();
        var t = await scope.ServiceProvider.GetRequiredService<AppDbContext>().TokensAtajo.AsNoTracking().FirstAsync(x => x.Hash == AtajosApi.Hashear(token));
        Assert.NotEqual(token, t.Hash);
        Assert.Equal("gasto", t.Alcances);
    }

    [Fact]
    public async Task Sin_token_con_otro_alcance_o_revocado_no_entra()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Con(null).GetAsync("/api/atajos/categorias")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Con("mda_inventado").GetAsync("/api/atajos/categorias")).StatusCode);
        var lectura = await Token("lectura");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Con(lectura).GetAsync("/api/atajos/categorias")).StatusCode); // no tiene «gasto»
        Assert.Equal(HttpStatusCode.OK, (await Con(lectura).GetAsync("/api/atajos/resumen")).StatusCode);

        var gasto = await Token("gasto");
        Assert.Equal(HttpStatusCode.OK, (await Con(gasto).GetAsync("/api/atajos/categorias")).StatusCode);
        var c = await Dentro();
        var id = (await c.GetFromJsonAsync<JsonElement>("/api/atajos-tokens")).EnumerateArray().Last().GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/atajos-tokens/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Con(gasto).GetAsync("/api/atajos/categorias")).StatusCode);
        // y desaparece de la lista, no se queda deshabilitado
        Assert.DoesNotContain((await c.GetFromJsonAsync<JsonElement>("/api/atajos-tokens")).EnumerateArray(), t => t.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task La_cookie_de_sesion_sola_no_vale_para_los_atajos_ni_el_token_para_la_app()
    {
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/atajos/categorias")).StatusCode);
        var token = await Token("gasto", "pago", "lectura");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Con(token).GetAsync("/api/gastos/movimientos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Con(token).GetAsync("/api/atajos-tokens")).StatusCode);
    }

    [Fact]
    public async Task El_gasto_rapido_apunta_y_contesta_con_el_saldo()
    {
        var token = await Token("gasto");
        var res = await Con(token).PostAsJsonAsync("/api/atajos/movimiento", new { importe = 12.5, concepto = "Café", tipo = "gasto", categoria = "restaurantes" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Apuntado", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString());
        var c = await Dentro();
        var movs = await c.GetFromJsonAsync<JsonElement>("/api/gastos/movimientos?q=caf%C3%A9");
        Assert.Contains(movs.EnumerateArray(), m => m.GetProperty("importe").GetDecimal() == 12.5m && m.GetProperty("notas").GetString() == "Atajo de iOS");
        Assert.Equal(HttpStatusCode.BadRequest, (await Con(token).PostAsJsonAsync("/api/atajos/movimiento", new { importe = 0, concepto = "Nada" })).StatusCode);
    }

    [Fact]
    public async Task Un_pago_de_apple_pay_entra_por_confirmar_con_la_categoria_que_ya_usas()
    {
        var c = await Dentro();
        await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = Hoy, importe = 30, categoria = "supermercado", concepto = "Mercadona" });
        var token = await Token("pago");
        var res = await Con(token).PostAsJsonAsync("/api/atajos/pago", new { comercio = "MERCADONA 0123 MADRID", importe = 23.4, tarjeta = "Visa" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var j = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Supermercado", j.GetProperty("mensaje").GetString());
        var m = (await c.GetFromJsonAsync<JsonElement>("/api/gastos/movimientos?q=madrid")).EnumerateArray().First(x => x.GetProperty("importe").GetDecimal() == 23.4m);
        Assert.True(m.GetProperty("porConfirmar").GetBoolean());
        Assert.Equal("supermercado", m.GetProperty("categoria").GetString());
        Assert.Equal("Apple Pay · Visa", m.GetProperty("notas").GetString());

        // el mismo pago dos veces seguidas cuenta una
        var otra = await (await Con(token).PostAsJsonAsync("/api/atajos/pago", new { comercio = "MERCADONA 0123 MADRID", importe = 23.4 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(otra.GetProperty("duplicado").GetBoolean());

        // editarlo en la app lo da por bueno
        var id = m.GetProperty("id").GetInt32();
        await c.PutAsJsonAsync($"/api/gastos/movimientos/{id}", new { tipo = "Gasto", fecha = Hoy, importe = 23.4, categoria = "supermercado", concepto = "Mercadona Madrid" });
        var despues = await c.GetFromJsonAsync<JsonElement>("/api/gastos/movimientos?q=madrid");
        Assert.False(despues.EnumerateArray().First(x => x.GetProperty("id").GetInt32() == id).GetProperty("porConfirmar").GetBoolean());
    }

    [Fact]
    public async Task Una_devolucion_de_apple_pay_es_un_ingreso_y_los_datos_raros_se_rechazan()
    {
        var token = await Token("pago");
        var res = await Con(token).PostAsJsonAsync("/api/atajos/pago", new { comercio = "Zara Devoluciones", importe = -19.99 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var c = await Dentro();
        var m = (await c.GetFromJsonAsync<JsonElement>("/api/gastos/movimientos?q=zara")).EnumerateArray().Single();
        Assert.Equal("Ingreso", m.GetProperty("tipo").GetString());
        Assert.Equal(19.99m, m.GetProperty("importe").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await Con(token).PostAsJsonAsync("/api/atajos/pago", new { comercio = "", importe = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Con(token).PostAsJsonAsync("/api/atajos/pago", new { comercio = "X", importe = 0 })).StatusCode);
    }

    [Fact]
    public async Task La_frase_se_guarda_si_se_entiende_y_si_no_se_dice()
    {
        var token = await Token("gasto");
        var bien = await (await Con(token).PostAsJsonAsync("/api/atajos/frase", new { texto = "ayer 12 € en el Mercadona" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(bien.GetProperty("guardado").GetBoolean());
        var mal = await (await Con(token).PostAsJsonAsync("/api/atajos/frase", new { texto = "blablabla" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(mal.GetProperty("guardado").GetBoolean());
        // una pregunta pide permiso de lectura
        var pregunta = await (await Con(token).PostAsJsonAsync("/api/atajos/frase", new { texto = "¿cuánto llevo este mes?" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("lectura", pregunta.GetProperty("mensaje").GetString());
        var lector = await Token("gasto", "lectura");
        var conLectura = await (await Con(lector).PostAsJsonAsync("/api/atajos/frase", new { texto = "¿cuánto llevo este mes?" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(conLectura.GetProperty("guardado").GetBoolean());
        Assert.DoesNotContain("permiso", conLectura.GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task El_ping_sin_telegram_vinculado_da_conflicto()
    {
        var token = await Token("gasto");
        Assert.Equal(HttpStatusCode.Conflict, (await Con(token).PostAsJsonAsync("/api/atajos/ping", new { texto = "¿Qué has gastado?" })).StatusCode);
    }
}
