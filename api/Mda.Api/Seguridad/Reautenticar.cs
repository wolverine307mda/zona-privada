using Mda.Api.Auth;
using Mda.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

// Volver a demostrar que eres tú antes de lo delicado (apagar el PC, por ejemplo): contraseña y, si tienes los dos pasos,
// el código. Vale 2 minutos y solo para esta sesión: una cookie robada no basta para dar órdenes al PC.
public static class Reautenticar
{
    public static readonly TimeSpan Validez = TimeSpan.FromMinutes(2);

    public static async Task<bool> Vigente(HttpContext ctx, AppDbContext db, TimeProvider reloj)
    {
        var sid = Sesiones.Actual(ctx.User);
        return sid is not null && await db.Sesiones.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid) is { ReautenticadaHasta: { } h } && h > reloj.GetUtcNow();
    }

    // 403 con «reautenticar» para que la app pida la contraseña y repita la petición
    public static IResult Pedir() => Results.Json(new { reautenticar = true, detalle = "Confirma que eres tú: contraseña y, si tienes dos pasos, el código." }, statusCode: StatusCodes.Status403Forbidden);

    // contraseña y, si hay dos pasos, el código; cada fallo cuenta para el bloqueo de la cuenta. null = todo bien
    public static async Task<IResult?> Verificar(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, IdentityUser u, string? password, string? codigo)
    {
        var r = await signIn.CheckPasswordSignInAsync(u, password ?? "", lockoutOnFailure: true);
        if (r.IsLockedOut) return Results.Problem("Demasiados intentos: espera 15 minutos.", statusCode: StatusCodes.Status429TooManyRequests);
        if (!r.Succeeded) return Results.Problem("Contraseña incorrecta.", statusCode: StatusCodes.Status401Unauthorized);
        if (await users.GetTwoFactorEnabledAsync(u))
        {
            var c = (codigo ?? "").Replace(" ", "");
            var ok = c.Length > 0 && (await users.VerifyTwoFactorTokenAsync(u, TokenOptions.DefaultAuthenticatorProvider, c)
                || (await users.RedeemTwoFactorRecoveryCodeAsync(u, c)).Succeeded);
            if (!ok)
            {
                await users.AccessFailedAsync(u);
                return Results.Problem("Falta el código de la app de dos pasos (o no es correcto).", statusCode: StatusCodes.Status401Unauthorized);
            }
        }
        return null;
    }

    public static void MapReautenticar(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/seguridad/reautenticar", async (ReautenticarEntrada e, ClaimsPrincipalAccessor yo, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, AppDbContext db, HttpContext ctx, TimeProvider reloj) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            if (await Verificar(users, signIn, u, e.Password, e.Codigo) is { } error) return error;
            if (Sesiones.Actual(ctx.User) is { } sid && await db.Sesiones.FindAsync(sid) is { } s)
            {
                s.ReautenticadaHasta = reloj.GetUtcNow().Add(Validez);
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting(Limites.Reauth);

        // lo mismo con el passkey (Face ID): se pide un reto de esta cuenta y la firma lo confirma
        app.MapPost("/api/seguridad/reautenticar/passkey/opciones", async (ClaimsPrincipalAccessor yo, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            return u is null ? Results.Unauthorized() : Results.Content(await signIn.MakePasskeyRequestOptionsAsync(u), "application/json");
        }).RequireAuthorization().RequireRateLimiting(Limites.Reauth);

        app.MapPost("/api/seguridad/reautenticar/passkey", async (CredencialEntrada c, ClaimsPrincipalAccessor yo, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, AppDbContext db, HttpContext ctx, TimeProvider reloj) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            var r = await signIn.PerformPasskeyAssertionAsync(c.Credencial.GetRawText());
            // tiene que ser un passkey de ESTA cuenta
            if (!r.Succeeded || r.User is null || r.User.Id != u.Id) return Results.Problem("Ese passkey no es de esta cuenta.", statusCode: StatusCodes.Status401Unauthorized);
            await users.AddOrUpdatePasskeyAsync(r.User, r.Passkey);
            await PasskeyInfos.Uso(db, ctx, PasskeyInfos.IdDe(r.Passkey.CredentialId), "confirmación", reloj);
            if (Sesiones.Actual(ctx.User) is { } sid && await db.Sesiones.FindAsync(sid) is { } s)
            {
                s.ReautenticadaHasta = reloj.GetUtcNow().Add(Validez);
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting(Limites.Reauth);
    }
}
