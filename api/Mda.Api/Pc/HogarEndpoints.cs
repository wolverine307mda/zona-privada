using Mda.Api.Seguridad;

namespace Mda.Api.Pc;

// Lo de casa: luces, enchufes y escenas de Home Assistant (y, más abajo, AdGuard Home). Todo con sesión y con las claves de Seguridad → Claves.
public record ProteccionEntrada(bool Activa, int? Minutos);

public static class HogarEndpoints
{
    private static IResult NoConfigurado(string quien) =>
        Results.Conflict(new { detalle = $"{quien} no está configurado: añade su dirección y sus claves en Seguridad → Claves." });

    private static IResult NoResponde(string quien) => Results.Json(new { detalle = $"{quien} no responde." }, statusCode: StatusCodes.Status502BadGateway);

    public static void MapHogar(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/hogar").RequireAuthorization();

        // qué servicios hay configurados (la pantalla solo enseña los que sí)
        g.MapGet("/estado", (ClienteHogar ha, ClienteAdGuard ag) => Results.Ok(new { homeAssistant = ha.Configurado, adguard = ag.Configurado }));

        g.MapGet("/entidades", async (ClienteHogar ha, CancellationToken ct) =>
        {
            if (!ha.Configurado) return NoConfigurado("Home Assistant");
            try { return Results.Ok(await ha.Listar(ct)); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return NoResponde("Home Assistant"); }
        });

        // AdGuard Home: lo que ha bloqueado la red y pausar la protección unos minutos
        g.MapGet("/adguard", async (ClienteAdGuard ag, CancellationToken ct) =>
        {
            if (!ag.Configurado) return NoConfigurado("AdGuard Home");
            try { return Results.Ok(await ag.Resumen(ct)); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return NoResponde("AdGuard Home"); }
        });

        g.MapPost("/adguard/proteccion", async (ProteccionEntrada e, ClienteAdGuard ag, CancellationToken ct) =>
        {
            if (e.Minutos is < 1 or > ClienteAdGuard.PausaMaximaMinutos) return Results.ValidationProblem(new Dictionary<string, string[]> { ["minutos"] = [$"De 1 a {ClienteAdGuard.PausaMaximaMinutos} minutos."] });
            if (!ag.Configurado) return NoConfigurado("AdGuard Home");
            try { return await ag.Proteccion(e.Activa, e.Minutos, ct) ? Results.NoContent() : NoResponde("AdGuard Home"); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return NoResponde("AdGuard Home"); }
        }).RequireRateLimiting(Limites.Atajos);

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
