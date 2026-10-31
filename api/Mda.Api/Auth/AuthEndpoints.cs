using System.Security.Claims;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Identity;

namespace Mda.Api.Auth;

public static class AuthEndpoints
{
    // Rutas de Identity que no se ofrecen: la zona es solo mía, nadie se registra desde fuera.
    // (/refresh y /confirmEmail también: sin sesión por cookie no se emiten ni se renuevan tokens)
    private static readonly string[] Cerradas = ["/register", "/forgotPassword", "/resetPassword", "/resendConfirmationEmail", "/refresh", "/confirmEmail"];

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        // login, 2FA, refresh… de Identity, con límite de intentos por IP.
        // Ojo: en /login el campo se llama «email» pero Identity busca por nombre de usuario (Admin:Usuario).
        app.MapGroup("/api/auth")
            .MapIdentityApi<IdentityUser>()
            .RequireRateLimiting(Limites.Acceso)
            .AddEndpointFilter(async (ctx, next) =>
            {
                var peticion = ctx.HttpContext.Request;
                var ruta = peticion.Path.Value ?? "";
                if (Cerradas.Any(c => ruta.EndsWith(c, StringComparison.OrdinalIgnoreCase))) return Results.NotFound();
                // sin cookie Identity daría un token Bearer que no aparece en Seguridad › Sesiones ni se puede cerrar
                if (ruta.EndsWith("/login", StringComparison.OrdinalIgnoreCase) && !string.Equals(peticion.Query["useCookies"], "true", StringComparison.OrdinalIgnoreCase))
                    return Results.Problem("Solo se entra con cookie de sesión.", statusCode: StatusCodes.Status400BadRequest);
                return await next(ctx);
            });

        var sesion = app.MapGroup("/api/sesion").RequireAuthorization();

        // quién soy (el front lo usa para saber si hay sesión)
        sesion.MapGet("/", (ClaimsPrincipal user) =>
            Results.Ok(new { usuario = user.Identity?.Name, email = user.FindFirstValue(ClaimTypes.Email) }));

        // cerrar sesión: borra la cookie
        sesion.MapPost("/salir", async (ClaimsPrincipal user, SignInManager<IdentityUser> signIn, Data.AppDbContext db) =>
        {
            if (Sesiones.Actual(user) is { } sid && await db.Sesiones.FindAsync(sid) is { } s)
            {
                s.Cerrada = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            await signIn.SignOutAsync();
            return Results.NoContent();
        });
    }
}
