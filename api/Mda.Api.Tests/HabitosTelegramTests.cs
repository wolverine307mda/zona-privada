using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mda.Api.Telegram;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

// /habitos y /hecho por Telegram (con el Telegram de mentira de las otras pruebas)
public class HabitosTelegramTests(TelegramFactory factory) : IClassFixture<TelegramFactory>
{
    private static JsonElement Mensaje(long chat, string texto) =>
        JsonSerializer.SerializeToElement(new { update_id = 1, message = new { message_id = 5, chat = new { id = chat }, from = new { id = chat, first_name = "Mario" }, text = texto } });

    private async Task Decir(string texto)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Conversacion>().Atender(Mensaje(42, texto), CancellationToken.None);
    }

    private string Ultimo()
    {
        lock (factory.Red.Telegram) return factory.Red.Telegram.Last(x => x.Metodo == "sendMessage").Cuerpo!["text"]!.GetValue<string>();
    }

    [Fact]
    public async Task Lista_los_habitos_de_hoy_y_los_apunta_con_hecho()
    {
        var c = await factory.Dentro();
        var v = await (await c.PostAsJsonAsync("/api/telegram/vincular", new { })).Content.ReadFromJsonAsync<JsonElement>();
        await Decir("/start " + v.GetProperty("enlace").GetString()!.Split("start=")[1]);

        await Decir("/habitos");
        Assert.Contains("No tienes hábitos para hoy", Ultimo());

        await c.PostAsJsonAsync("/api/habitos", new { nombre = "Meditar 10 minutos" });
        await c.PostAsJsonAsync("/api/habitos", new { nombre = "Beber agua", objetivo = 8, unidad = "vasos" });
        await c.PostAsJsonAsync("/api/habitos", new { nombre = "Beber té" });

        await Decir("/habitos");
        var lista = Ultimo();
        Assert.Contains("⬜ Meditar 10 minutos", lista);
        Assert.Contains("⬜ Beber agua 0/8", lista);

        await Decir("/hecho meditar");
        Assert.Contains("✅ <b>Meditar 10 minutos</b>", Ultimo());

        await Decir("/hecho agua");
        Assert.Contains("Beber agua</b> (1/8 vasos)", Ultimo());
        await Decir("/hecho agua");
        Assert.Contains("(2/8 vasos)", Ultimo());

        await Decir("/hecho beber");                                          // encaja con dos: pide aclarar
        Assert.Contains("¿Cuál?", Ultimo());
        await Decir("/hecho inexistente");
        Assert.Contains("No encuentro ningún hábito", Ultimo());
        await Decir("/hecho");
        Assert.Contains("Dime cuál", Ultimo());

        await Decir("/habitos");
        var fin = Ultimo();
        Assert.Contains("✅ Meditar 10 minutos", fin);
        Assert.Contains("⬜ Beber agua 2/8", fin);
        var estado = await c.GetFromJsonAsync<JsonElement>("/api/habitos");
        Assert.Equal(2, estado.EnumerateArray().First(h => h.GetProperty("nombre").GetString() == "Beber agua").GetProperty("hoy").GetInt32());
    }
}
