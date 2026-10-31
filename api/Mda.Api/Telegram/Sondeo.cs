using System.Text.Json;

namespace Mda.Api.Telegram;

// Escucha al bot con «long polling» (getUpdates): no hace falta abrir nada hacia Internet ni un webhook.
// Cada mensaje o botón pulsado lo atiende la Conversacion.
public class Sondeo(Bot bot, IServiceScopeFactory scopes, ILogger<Sondeo> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!bot.Configurado) return;
        try
        {
            // sin webhook, por si el bot se usó antes en otro sitio (si no, getUpdates no funciona)
            await bot.Llamar("deleteWebhook", new { }, ct);
            // el menú de comandos que enseña Telegram al pulsar «/», y la descripción del bot
            await bot.Llamar("setMyCommands", new
            {
                commands = new[]
                {
                    new { command = "hoy", description = "Tu día: tiempo, agenda y dinero" },
                    new { command = "tiempo", description = "El tiempo de hoy" },
                    new { command = "saldo", description = "Cuánto tienes y cuánto puedes gastar" },
                    new { command = "ayuda", description = "Qué le puedes decir" },
                },
            }, ct);
            await bot.Llamar("setMyShortDescription", new { short_description = "Tu zona privada: apunta gastos, recordatorios y pregúntale lo que quieras." }, ct);
            var yo = await bot.Usuario(ct);
            log.LogInformation("Bot de Telegram @{Bot} escuchando", yo);
            long desde = 0;
            while (!ct.IsCancellationRequested)
            {
                var r = await bot.Llamar("getUpdates", new { offset = desde, timeout = 50, allowed_updates = new[] { "message", "callback_query" } }, ct, TimeSpan.FromSeconds(70));
                if (r is not { ValueKind: JsonValueKind.Array } lista)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct); // sin red o token mal puesto: se reintenta con calma
                    continue;
                }
                foreach (var u in lista.EnumerateArray())
                {
                    desde = u.GetProperty("update_id").GetInt64() + 1;
                    try
                    {
                        using var scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<Conversacion>().Atender(u, ct);
                    }
                    catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Fallo atendiendo un mensaje de Telegram"); }
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
