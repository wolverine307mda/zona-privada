using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

// Fase 1: búsquedas completas, importes a céntimos, validaciones sin 500, hucha concurrente, adjuntos y push
public class RobustezTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    // un solo inicio de sesión para toda la clase: el límite de 10 intentos por minuto también vale para los tests
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= factory.Dentro();

    private static string Hoy => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

    [Fact]
    public async Task La_busqueda_encuentra_apuntes_antiguos_aunque_haya_mas_de_mil_recientes()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Movimientos.Add(new Movimiento { Tipo = TipoMovimiento.Gasto, Fecha = new DateOnly(2020, 1, 5), Importe = 40, Categoria = "salud", Concepto = "Dentista de 2020" });
            for (var i = 0; i < 1100; i++)
                db.Movimientos.Add(new Movimiento { Tipo = TipoMovimiento.Gasto, Fecha = new DateOnly(2026, 1, 1).AddDays(i % 300), Importe = 1, Categoria = "otros", Concepto = $"Compra {i}" });
            await db.SaveChangesAsync();
        }
        var c = await Dentro();
        var r = await c.GetFromJsonAsync<JsonElement>("/api/gastos/movimientos?q=DENTISTA");
        Assert.Contains(r.EnumerateArray(), m => m.GetProperty("concepto").GetString() == "Dentista de 2020");
    }

    [Fact]
    public async Task Un_importe_de_una_milesima_no_es_un_importe()
    {
        var c = await Dentro();
        var res = await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = Hoy, importe = 0.001, categoria = "otros", concepto = "Nada" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task El_importe_se_redondea_a_centimos_hacia_arriba_en_la_mitad()
    {
        var c = await Dentro();
        var res = await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = Hoy, importe = 10.005, categoria = "otros", concepto = "Redondeo" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(10.01m, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("importe").GetDecimal());
    }

    [Fact]
    public async Task Un_fijo_que_no_existe_da_validacion_y_no_un_500()
    {
        var c = await Dentro();
        var res = await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = Hoy, importe = 5, categoria = "otros", concepto = "Raro", recurrenteId = 9999 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("anio=99999&mes=1")]
    [InlineData("anio=2026&mes=13")]
    [InlineData("anio=1&mes=0")]
    public async Task Las_estadisticas_con_fechas_imposibles_dan_400(string consulta)
    {
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/gastos/estadisticas?{consulta}")).StatusCode);
    }

    [Fact]
    public async Task Aportar_a_la_vez_a_una_hucha_no_pierde_dinero()
    {
        var c = await Dentro();
        var h = await (await c.PostAsJsonAsync("/api/gastos/huchas", new { nombre = "Viaje", objetivo = 1000, color = "naranja", emoji = "✈️" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = h.GetProperty("id").GetInt32();
        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => c.PostAsJsonAsync($"/api/gastos/huchas/{id}/aportar", new { importe = 10 })));
        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/gastos/huchas");
        var hucha = lista.EnumerateArray().First(x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal(40m, hucha.GetProperty("ahorrado").GetDecimal());
    }

    [Fact]
    public async Task Un_archivo_que_dice_ser_jpeg_pero_no_lo_es_se_rechaza()
    {
        var c = await Dentro();
        var m = await (await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = Hoy, importe = 3, categoria = "otros", concepto = "Ticket falso" })).Content.ReadFromJsonAsync<JsonElement>();
        var falso = new ByteArrayContent("<html><script>alert(1)</script></html>"u8.ToArray());
        falso.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PutAsync($"/api/gastos/movimientos/{m.GetProperty("id").GetInt32()}/ticket", falso)).StatusCode);
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc", HttpStatusCode.OK)]
    [InlineData("https://web.push.apple.com/abc", HttpStatusCode.OK)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc", HttpStatusCode.OK)]
    [InlineData("https://127.0.0.1/interno", HttpStatusCode.BadRequest)]
    [InlineData("https://servidor-que-no-es-de-push.example.com/x", HttpStatusCode.BadRequest)]
    [InlineData("https://fcm.googleapis.com.evil.example/x", HttpStatusCode.BadRequest)]
    public async Task Solo_se_aceptan_endpoints_de_servicios_de_push_conocidos(string endpoint, HttpStatusCode esperado)
    {
        var c = await Dentro();
        var res = await c.PostAsJsonAsync("/api/push", new { endpoint, p256dh = "BPclave", auth = "secreto" });
        Assert.Equal(esperado, res.StatusCode);
    }
}
