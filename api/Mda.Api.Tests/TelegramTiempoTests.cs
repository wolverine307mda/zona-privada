using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mda.Api.Asistente;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Telegram;
using Mda.Api.Tiempo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

// Internet de mentira: lo que la API pediría a Telegram y a Open-Meteo se apunta y se contesta aquí
public class RedDePrueba : HttpMessageHandler
{
    public List<(string Metodo, JsonNode? Cuerpo)> Telegram { get; } = [];
    public int LluviaManana { get; set; } = 80;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var url = req.RequestUri!.ToString();
        if (url.Contains("open-meteo.com/v1/search")) return Json("""{"results":[{"name":"Madrid","latitude":40.4165,"longitude":-3.70256,"admin1":"Comunidad de Madrid","country":"España"}]}""");
        if (url.Contains("open-meteo.com")) return Json(Prevision(DateOnly.FromDateTime(DateTime.Now), LluviaManana).ToJsonString());
        var metodo = url[(url.LastIndexOf('/') + 1)..];
        if (metodo == "getUpdates") { await Task.Delay(2000, ct); return Json("""{"ok":true,"result":[]}"""); }
        var cuerpo = req.Content is null ? null : JsonNode.Parse(await req.Content.ReadAsStringAsync(ct));
        lock (Telegram) Telegram.Add((metodo, cuerpo));
        return metodo == "getMe" ? Json("""{"ok":true,"result":{"username":"mda_bot"}}""") : Json("""{"ok":true,"result":{}}""");
    }

    private static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };

    // una semana de previsión con el formato de Open-Meteo, empezando hoy
    public static JsonObject Prevision(DateOnly hoy, int lluviaManana)
    {
        var dias = Enumerable.Range(0, 7).Select(i => hoy.AddDays(i)).ToList();
        var horas = Enumerable.Range(0, 7 * 24).Select(i => hoy.ToDateTime(TimeOnly.MinValue).AddHours(i)).ToList();
        JsonArray A<T>(IEnumerable<T> xs) => new(xs.Select(x => JsonValue.Create(x)).ToArray<JsonNode?>());
        return new JsonObject
        {
            ["current"] = new JsonObject
            {
                ["time"] = hoy.ToDateTime(new TimeOnly(12, 0)).ToString("yyyy-MM-ddTHH:mm"), ["temperature_2m"] = 18.4, ["apparent_temperature"] = 17.1,
                ["weather_code"] = 2, ["is_day"] = 1, ["wind_speed_10m"] = 9.5, ["relative_humidity_2m"] = 61,
            },
            ["hourly"] = new JsonObject
            {
                ["time"] = A(horas.Select(h => h.ToString("yyyy-MM-ddTHH:mm"))), ["temperature_2m"] = A(horas.Select(h => 10.0 + h.Hour / 2.0)),
                ["precipitation_probability"] = A(horas.Select(_ => 10)), ["weather_code"] = A(horas.Select(_ => 2)), ["is_day"] = A(horas.Select(h => h.Hour is >= 8 and < 19 ? 1 : 0)),
            },
            ["daily"] = new JsonObject
            {
                ["time"] = A(dias.Select(d => d.ToString("yyyy-MM-dd"))),
                ["weather_code"] = A(dias.Select((_, i) => i == 1 && lluviaManana >= 60 ? 63 : 2)),
                ["temperature_2m_max"] = A(dias.Select(_ => 21.3)), ["temperature_2m_min"] = A(dias.Select(_ => 9.6)),
                ["precipitation_probability_max"] = A(dias.Select((_, i) => i == 1 ? lluviaManana : 5)), ["precipitation_sum"] = A(dias.Select((_, i) => i == 1 ? 6.2 : 0)),
                ["sunrise"] = A(dias.Select(d => d.ToString("yyyy-MM-dd") + "T08:05")), ["sunset"] = A(dias.Select(d => d.ToString("yyyy-MM-dd") + "T18:22")),
                ["uv_index_max"] = A(dias.Select(_ => 3.1)),
            },
        };
    }
}

public class TelegramFactory : ModulosFactory
{
    public RedDePrueba Red { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Telegram:Token", "123:token-de-prueba");
        builder.ConfigureServices(s =>
        {
            foreach (var n in new[] { "telegram", "telegram-largo", "tiempo" })
                s.AddHttpClient(n).ConfigurePrimaryHttpMessageHandler(() => Red);
        });
    }
}

public class TelegramTiempoTests(TelegramFactory factory) : IClassFixture<TelegramFactory>
{
    private static JsonElement Mensaje(long chat, string texto) =>
        JsonSerializer.SerializeToElement(new { update_id = 1, message = new { message_id = 5, chat = new { id = chat }, from = new { id = chat, first_name = "Mario" }, text = texto } });

    private static JsonElement Boton(long chat, string datos) =>
        JsonSerializer.SerializeToElement(new { update_id = 2, callback_query = new { id = "cb", data = datos, from = new { id = chat }, message = new { message_id = 9, chat = new { id = chat } } } });

    private async Task Decir(JsonElement u)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Conversacion>().Atender(u, CancellationToken.None);
    }

    private (string Metodo, JsonNode? Cuerpo) Ultimo(string metodo)
    {
        lock (factory.Red.Telegram) return factory.Red.Telegram.Last(x => x.Metodo == metodo);
    }

    [Fact]
    public async Task Se_vincula_con_un_enlace_y_guarda_lo_que_le_escribes()
    {
        var c = await factory.Dentro();
        var v = await (await c.PostAsJsonAsync("/api/telegram/vincular", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var enlace = v.GetProperty("enlace").GetString()!;
        Assert.StartsWith("https://t.me/mda_bot?start=", enlace);

        // un código que no vale no vincula
        await Decir(Mensaje(42, "/start otro-codigo"));
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/telegram")).GetProperty("vinculado").GetBoolean());
        await Decir(Mensaje(42, "/start " + enlace.Split("start=")[1]));
        var estado = await c.GetFromJsonAsync<JsonElement>("/api/telegram");
        Assert.True(estado.GetProperty("vinculado").GetBoolean());
        Assert.Equal("Mario", estado.GetProperty("nombre").GetString());

        // otro chat no puede usarlo
        await Decir(Mensaje(99, "ayer 12 € en el Mercadona"));
        Assert.Contains("privado", Ultimo("sendMessage").Cuerpo!["text"]!.GetValue<string>());
        Assert.Equal(99, Ultimo("sendMessage").Cuerpo!["chat_id"]!.GetValue<long>());

        // un gasto: se propone con botones y se guarda al pulsar
        await Decir(Mensaje(42, "taxi 15,40 € al aeropuerto"));
        var propuesta = Ultimo("sendMessage").Cuerpo!;
        Assert.Contains("15,40 €", propuesta["text"]!.GetValue<string>());
        var guardar = propuesta["reply_markup"]!["inline_keyboard"]![0]![0]!["callback_data"]!.GetValue<string>();
        await Decir(Boton(42, guardar));
        var movs = await c.GetFromJsonAsync<JsonElement>("/api/gastos/movimientos");
        var taxi = movs.EnumerateArray().Single(m => m.GetProperty("importe").GetDecimal() == 15.40m);
        Assert.Equal("transporte", taxi.GetProperty("categoria").GetString());
        var editado = Ultimo("editMessageText").Cuerpo!;
        Assert.StartsWith("✅", editado["text"]!.GetValue<string>());

        // y se puede deshacer
        await Decir(Boton(42, editado["reply_markup"]!["inline_keyboard"]![0]![0]!["callback_data"]!.GetValue<string>()));
        movs = await c.GetFromJsonAsync<JsonElement>("/api/gastos/movimientos");
        Assert.DoesNotContain(movs.EnumerateArray(), m => m.GetProperty("importe").GetDecimal() == 15.40m);

        // un recordatorio avisa por donde se pida («por telegram»); si no se dice nada, solo por la app
        await Decir(Mensaje(42, "recuérdame mañana a las 9 llamar al taller por telegram"));
        await Decir(Boton(42, Ultimo("sendMessage").Cuerpo!["reply_markup"]!["inline_keyboard"]![0]![0]!["callback_data"]!.GetValue<string>()));
        using (var scope = factory.Services.CreateScope())
        {
            var ev = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Eventos.SingleAsync(e => e.Titulo == "Llamar al taller");
            Assert.True(ev.PorTelegram);
            Assert.False(ev.PorPush || ev.PorCorreo);
            Assert.NotNull(ev.ProximoAviso);
        }

        // preguntas: contesta directamente
        await Decir(Mensaje(42, "/saldo"));
        Assert.Null(Ultimo("sendMessage").Cuerpo!["reply_markup"]);
        await Decir(Mensaje(42, "/hoy"));
        Assert.Contains("Agenda", Ultimo("sendMessage").Cuerpo!["text"]!.GetValue<string>());

        // desvincular
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/telegram")).StatusCode);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/telegram")).GetProperty("vinculado").GetBoolean());
    }

    [Fact]
    public async Task El_tiempo_se_elige_el_sitio_se_ve_y_avisa_si_manana_llueve()
    {
        var c = await factory.Dentro();
        Assert.Equal(JsonValueKind.Null, (await c.GetFromJsonAsync<JsonElement>("/api/tiempo")).GetProperty("lugar").ValueKind);
        var sitios = await c.GetFromJsonAsync<JsonElement>("/api/tiempo/buscar?q=madrid");
        var madrid = sitios[0];
        Assert.Equal("Comunidad de Madrid, España", madrid.GetProperty("region").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/tiempo/lugar", madrid)).StatusCode);

        var t = await c.GetFromJsonAsync<JsonElement>("/api/tiempo");
        Assert.Equal("Madrid", t.GetProperty("lugar").GetProperty("nombre").GetString());
        Assert.Equal(18.4, t.GetProperty("ahora").GetProperty("temperatura").GetDouble());
        Assert.Equal("Algo nublado", t.GetProperty("ahora").GetProperty("texto").GetString());
        Assert.Equal(7, t.GetProperty("dias").GetArrayLength());
        Assert.Equal(80, t.GetProperty("dias")[1].GetProperty("lluvia").GetInt32());
        Assert.True(t.GetProperty("horas").GetArrayLength() >= 12);

        // el asistente también lo sabe
        var r = await (await c.PostAsJsonAsync("/api/asistente/entender", new { texto = "¿va a llover mañana?" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pregunta", r.GetProperty("clase").GetString());
        Assert.Contains("Mañana en Madrid", r.GetProperty("respuesta").GetString());
        Assert.Contains("paraguas", r.GetProperty("respuesta").GetString());

        // y avisa la víspera, una sola vez al día
        var vigilante = factory.Services.GetRequiredService<Vigilante>();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await vigilante.Tiempo(db, CancellationToken.None);
        await vigilante.Tiempo(db, CancellationToken.None);
        lock (factory.Avisos.Enviados)
        {
            Assert.Single(factory.Avisos.Enviados, x => x.StartsWith("push:☔ Mañana llueve"));
            Assert.Single(factory.Avisos.Enviados, x => x.StartsWith("telegram:☔ Mañana llueve"));
        }
    }

    [Fact]
    public void Las_frases_del_tiempo_y_los_avisos()
    {
        var hoy = new DateOnly(2026, 10, 31);
        var p = ServicioTiempo.Interpretar(new Lugar("Madrid", 40.4, -3.7), JsonSerializer.SerializeToElement(RedDePrueba.Prevision(hoy, 10)));
        Assert.Null(ServicioTiempo.AvisoDe(p, hoy.AddDays(1)));
        Assert.StartsWith("Ahora 18 °C", ServicioTiempo.Frase(p, hoy, hoy));
        var lluvia = ServicioTiempo.Interpretar(new Lugar("Madrid", 40.4, -3.7), JsonSerializer.SerializeToElement(RedDePrueba.Prevision(hoy, 90)));
        Assert.Equal("☔ Mañana llueve", ServicioTiempo.AvisoDe(lluvia, hoy.AddDays(1))!.Value.Titulo);
    }
}
