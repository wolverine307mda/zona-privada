namespace Mda.Api.Telegram;

public static class TelegramEndpoints
{
    public static void MapTelegram(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/telegram").RequireAuthorization();

        g.MapGet("/", async (Bot bot, CancellationToken ct) => Results.Ok(new
        {
            configurado = bot.Configurado,
            vinculado = bot.Vinculado,
            nombre = bot.Nombre,
            bot = bot.Configurado ? await bot.Usuario(ct) : null,
        }));

        // enlace de un solo uso: abre el bot con /start <código> y queda vinculado ese chat
        g.MapPost("/vincular", async (Bot bot, CancellationToken ct) =>
        {
            if (!bot.Configurado) return Results.Problem("Falta el token del bot (TELEGRAM_TOKEN en el .env).", statusCode: 409);
            if (await bot.Usuario(ct) is not { } usuario) return Results.Problem("Telegram no reconoce el token del bot: revísalo en el .env.", statusCode: 502);
            var codigo = bot.NuevoCodigo();
            return Results.Ok(new { enlace = $"https://t.me/{usuario}?start={codigo}", bot = usuario });
        });

        g.MapDelete("/", async (Bot bot, CancellationToken ct) =>
        {
            if (bot.Vinculado) await bot.Enviar("👋 Te has desvinculado de la zona privada. Ya no te escribiré por aquí.", ct: ct);
            await bot.Desvincular(ct);
            return Results.NoContent();
        });
    }
}
