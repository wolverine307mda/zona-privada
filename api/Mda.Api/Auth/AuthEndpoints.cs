using System.Security.Claims;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Identity;

namespace Mda.Api.Auth;

public static class AuthEndpoints
{
    // Rutas de Identity que no se ofrecen: la zona es solo mía, nadie se registra desde fuera.
    private static readonly string[] Cerradas = ["/register", "/forgotPassword", "/resetPassword", "/resendConfirmationEmail"];

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        // login, 2FA, refresh… de Identity, con límite de intentos por IP.
        // Ojo: en /login el campo se llama «email» pero Identity busca por nombre de usuario (Admin:Usuario).
        app.MapGroup("/api/auth")
            .MapIdentityApi<IdentityUser>()
            .RequireRateLimiting(Limites.Acceso)
            .AddEndpointFilter(async (ctx, next) =>
            {
                var ruta = ctx.HttpContext.Request.Path.Value ?? "";
                return Cerradas.Any(c => ruta.EndsWith(c, StringComparison.OrdinalIgnoreCase))
                    ? Results.NotFound()
                    : await next(ctx);
            });

        var sesion = app.MapGroup("/api/sesion").RequireAuthorization();

        // quién soy (el front lo usa para saber si hay sesión)
        sesion.MapGet("/", (ClaimsPrincipal user) =>
            Results.Ok(new { usuario = user.Identity?.Name, email = user.FindFirstValue(ClaimTypes.Email) }));

        // cerrar sesión: borra la cookie
        sesion.MapPost("/salir", async (SignInManager<IdentityUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });
    }
}
