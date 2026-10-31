using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Auth;

public record CredencialEntrada(JsonElement Credencial, string? Nombre);
public record ReautenticarEntrada(string Password, string? Codigo);

// Entrar con la cara o la huella (passkeys / WebAuthn): el iPhone usa Face ID, Windows Hello en el PC.
// Un passkey ya es «algo que tienes + tu cara», así que no pide además el código de dos pasos. La contraseña y el 2FA siguen
// ahí como respaldo. El passkey queda atado al dominio de la app (App:UrlPublica): no sirve en otro.
public static class PasskeysEndpoints
{
    private const int Maximo = 5;

    public static void MapPasskeys(this IEndpointRouteBuilder app)
    {
        // ---- entrar (sin sesión) ----
        var e = app.MapGroup("/api/auth/passkey").AllowAnonymous().RequireRateLimiting(Limites.Acceso);

        // el reto: sin saber quién eres (el passkey de este dispositivo dice de quién es)
        e.MapPost("/entrada/opciones", async (SignInManager<IdentityUser> signIn) =>
            Results.Content(await signIn.MakePasskeyRequestOptionsAsync(null), "application/json"));

        e.MapPost("/entrada", async (CredencialEntrada c, SignInManager<IdentityUser> signIn, AppDbContext db, HttpContext ctx) =>
        {
            var r = await signIn.PasskeySignInAsync(c.Credencial.GetRawText());
            // se apunta como un intento más (con IP y dispositivo), bien o mal
            db.Accesos.Add(new Acceso { Ip = IpCliente.De(ctx), Agente = ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } a ? a[..Math.Min(a.Length, 400)] : "desconocido", Exito = r.Succeeded, Motivo = r.Succeeded ? "ok (passkey)" : "passkey no válido" });
            await db.SaveChangesAsync();
            return r.Succeeded ? Results.NoContent() : Results.Problem("Ese passkey no sirve para esta cuenta.", statusCode: StatusCodes.Status401Unauthorized);
        });

        // ---- gestionar los de mi cuenta (con sesión) ----
        var g = app.MapGroup("/api/passkeys").RequireAuthorization();

        g.MapGet("/", async (UserManager<IdentityUser> users, ClaimsPrincipalAccessor yo) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            return Results.Ok((await users.GetPasskeysAsync(u)).Select(p => new { id = Convert.ToBase64String(p.CredentialId), nombre = p.Name ?? "Passkey", creado = p.CreatedAt }));
        });

        g.MapPost("/opciones", async (UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, ClaimsPrincipalAccessor yo) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            var nombre = await users.GetUserNameAsync(u) ?? "usuario";
            var json = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity { Id = await users.GetUserIdAsync(u), Name = nombre, DisplayName = nombre });
            return Results.Content(json, "application/json");
        }).RequireRateLimiting(Limites.Sensible);

        g.MapPost("/", async (CredencialEntrada c, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, ClaimsPrincipalAccessor yo) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            if ((await users.GetPasskeysAsync(u)).Count >= Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Máximo {Maximo} passkeys."] });
            var r = await signIn.PerformPasskeyAttestationAsync(c.Credencial.GetRawText());
            if (!r.Succeeded) return Results.Problem("No se pudo registrar el passkey (¿abriste la app desde otra dirección?).", statusCode: StatusCodes.Status400BadRequest);
            r.Passkey.Name = string.IsNullOrWhiteSpace(c.Nombre) ? "Passkey" : c.Nombre.Trim()[..Math.Min(c.Nombre.Trim().Length, 40)];
            var guardado = await users.AddOrUpdatePasskeyAsync(u, r.Passkey);
            return guardado.Succeeded ? Results.NoContent() : Results.Problem("No se pudo guardar el passkey.", statusCode: StatusCodes.Status500InternalServerError);
        }).RequireRateLimiting(Limites.Sensible);

        g.MapDelete("/{id}", async (string id, UserManager<IdentityUser> users, ClaimsPrincipalAccessor yo) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            byte[] credencial;
            try { credencial = Convert.FromBase64String(id); } catch (FormatException) { return Results.NotFound(); }
            var r = await users.RemovePasskeyAsync(u, credencial);
            return r.Succeeded ? Results.NoContent() : Results.NotFound();
        });
    }
}

// el usuario de la petición, para no repetir HttpContext en cada ruta
public class ClaimsPrincipalAccessor(IHttpContextAccessor http)
{
    public System.Security.Claims.ClaimsPrincipal Usuario => http.HttpContext!.User;
}
