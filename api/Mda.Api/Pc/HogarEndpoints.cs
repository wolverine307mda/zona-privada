using Mda.Api.Seguridad;

namespace Mda.Api.Pc;

// Lo de casa: luces, enchufes y escenas de Home Assistant (y, más abajo, AdGuard Home). Todo con sesión y con las claves de Seguridad → Claves.
public static class HogarEndpoints
{
    private static IResult NoConfigurado(string quien) =>
        Results.Conflict(new { detalle = $"{quien} no está configurado: añade su dirección y sus claves en Seguridad → Claves." });

    private static IResult NoResponde(string quien) => Results.Json(new { detalle = $"{quien} no responde." }, statusCode: StatusCodes.Status502BadGateway);

    public static void MapHogar(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/hogar").RequireAuthorization();

        // qué servicios hay configurados (la pantalla solo enseña los que sí)
        g.MapGet("/estado", (ClienteHogar ha) => Results.Ok(new { homeAssistant = ha.Configurado }));

        g.MapGet("/entidades", async (ClienteHogar ha, CancellationToken ct) =>
        {
            if (!ha.Configurado) return NoConfigurado("Home Assistant");
            try { return Results.Ok(await ha.Listar(ct)); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return NoResponde("Home Assistant"); }
        });

        g.MapPost("/entidades/{id}/{accion}", async (string id, string accion, ClienteHogar ha, CancellationToken ct) =>
        {
            if (!ClienteHogar.IdValido(id) || !ClienteHogar.Acciones.ContainsKey(accion))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["id"] = ["Esa cosa o esa acción no se puede manejar desde aquí."] });
            if (!ha.Configurado) return NoConfigurado("Home Assistant");
            try { return await ha.Llamar(id, accion, ct) ? Results.NoContent() : NoResponde("Home Assistant"); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return NoResponde("Home Assistant"); }
        }).RequireRateLimiting(Limites.Atajos);
    }
}
