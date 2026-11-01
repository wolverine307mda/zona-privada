using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Auth;

public record CredencialEntrada(JsonElement Credencial, string? Nombre, DispositivoCliente? Dispositivo = null);
public record PasskeyNombre(string? Nombre);
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

        e.MapPost("/entrada", async (CredencialEntrada c, SignInManager<IdentityUser> signIn, AppDbContext db, HttpContext ctx, TimeProvider reloj) =>
        {
            var r = await signIn.PasskeySignInAsync(c.Credencial.GetRawText());
            // se apunta como un intento más (con IP y dispositivo), bien o mal
            db.Accesos.Add(new Acceso { Ip = IpCliente.De(ctx), Agente = ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } a ? a[..Math.Min(a.Length, 400)] : "desconocido", Exito = r.Succeeded, Motivo = r.Succeeded ? "ok (passkey)" : "passkey no válido" });
            await db.SaveChangesAsync();
            if (r.Succeeded && PasskeyInfos.IdDe(c.Credencial) is { } cid) await PasskeyInfos.Uso(db, ctx, cid, "entrada", reloj);
            return r.Succeeded ? Results.NoContent() : Results.Problem("Ese passkey no sirve para esta cuenta.", statusCode: StatusCodes.Status401Unauthorized);
        });

        // ---- gestionar los de mi cuenta (con sesión) ----
        var g = app.MapGroup("/api/passkeys").RequireAuthorization();

        g.MapGet("/", async (UserManager<IdentityUser> users, ClaimsPrincipalAccessor yo, AppDbContext db) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            var info = await db.PasskeysInfo.AsNoTracking().Where(i => i.UsuarioId == u.Id).ToDictionaryAsync(i => i.Id);
            return Results.Ok((await users.GetPasskeysAsync(u)).OrderBy(p => p.CreatedAt).Select(p =>
            {
                var id = PasskeyInfos.IdDe(p.CredentialId);
                info.TryGetValue(id, out var i);
                return new
                {
                    id, nombre = p.Name ?? "Passkey", creado = p.CreatedAt,
                    sincronizada = p.IsBackedUp, verificada = p.IsUserVerified, transportes = p.Transports ?? [],
                    alta = i is null ? null : new { ip = i.AltaIp, dispositivo = i.Dispositivo, tipo = i.Tipo, agente = i.UserAgent, idioma = i.Idioma, zona = i.Zona, pantalla = i.Pantalla },
                    ultimoUso = i?.UltimoUso is null ? null : new { fecha = i.UltimoUso, ip = i.UltimaIp, dispositivo = i.UltimoDispositivo, motivo = i.UltimoMotivo },
                    usos = i?.Usos ?? 0,
                };
            }));
        });

        g.MapPost("/opciones", async (UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, ClaimsPrincipalAccessor yo) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            var nombre = await users.GetUserNameAsync(u) ?? "usuario";
            var json = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity { Id = await users.GetUserIdAsync(u), Name = nombre, DisplayName = nombre });
            return Results.Content(json, "application/json");
        }).RequireRateLimiting(Limites.Sensible);

        g.MapPost("/", async (CredencialEntrada c, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, ClaimsPrincipalAccessor yo, AppDbContext db, HttpContext ctx, TimeProvider reloj, IAvisos avisos) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            if ((await users.GetPasskeysAsync(u)).Count >= Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Máximo {Maximo} passkeys."] });
            var r = await signIn.PerformPasskeyAttestationAsync(c.Credencial.GetRawText());
            if (!r.Succeeded) return Results.Problem("No se pudo registrar el passkey (¿abriste la app desde otra dirección?).", statusCode: StatusCodes.Status400BadRequest);
            r.Passkey.Name = string.IsNullOrWhiteSpace(c.Nombre) ? "Passkey" : c.Nombre.Trim()[..Math.Min(c.Nombre.Trim().Length, 40)];
            var guardado = await users.AddOrUpdatePasskeyAsync(u, r.Passkey);
            if (guardado.Succeeded)
            {
                await PasskeyInfos.Alta(db, ctx, u.Id, r.Passkey.CredentialId, c.Dispositivo, reloj);
                _ = avisos.PorTelegram($"🔑 Nuevo passkey «{System.Net.WebUtility.HtmlEncode(r.Passkey.Name)}» desde {Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString())} ({IpCliente.De(ctx)}). Si no fuiste tú, quítalo en Seguridad.");
            }
            return guardado.Succeeded ? Results.NoContent() : Results.Problem("No se pudo guardar el passkey.", statusCode: StatusCodes.Status500InternalServerError);
        }).RequireRateLimiting(Limites.Sensible);

        g.MapPut("/{id}", async (string id, PasskeyNombre e, UserManager<IdentityUser> users, ClaimsPrincipalAccessor yo) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            var nombre = (e.Nombre ?? "").Trim();
            if (nombre.Length is 0 or > 40) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["El nombre tiene que tener entre 1 y 40 letras."] });
            var cred = PasskeyInfos.Decodificar(id);
            var p = cred is null ? null : (await users.GetPasskeysAsync(u)).FirstOrDefault(x => x.CredentialId.AsSpan().SequenceEqual(cred));
            if (p is null) return Results.NotFound();
            p.Name = nombre;
            return (await users.AddOrUpdatePasskeyAsync(u, p)).Succeeded ? Results.NoContent() : Results.Problem("No se pudo guardar el nombre.", statusCode: StatusCodes.Status500InternalServerError);
        });

        g.MapDelete("/{id}", async (string id, UserManager<IdentityUser> users, ClaimsPrincipalAccessor yo, AppDbContext db, HttpContext ctx, IAvisos avisos) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            var credencial = PasskeyInfos.Decodificar(id);
            if (credencial is null) return Results.NotFound();
            var existente = (await users.GetPasskeysAsync(u)).FirstOrDefault(x => x.CredentialId.AsSpan().SequenceEqual(credencial));
            if (existente is null) return Results.NotFound(); // Identity da «bien» al quitar algo que no está
            var nombre = existente.Name;
            var r = await users.RemovePasskeyAsync(u, credencial);
            if (!r.Succeeded) return Results.NotFound();
            await PasskeyInfos.Quitar(db, PasskeyInfos.IdDe(credencial));
            _ = avisos.PorTelegram($"🔑 Se quitó el passkey «{System.Net.WebUtility.HtmlEncode(nombre ?? "Passkey")}» desde {Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString())} ({IpCliente.De(ctx)}).");
            return Results.NoContent();
        });
    }
}

// el usuario de la petición, para no repetir HttpContext en cada ruta
public class ClaimsPrincipalAccessor(IHttpContextAccessor http)
{
    public System.Security.Claims.ClaimsPrincipal Usuario => http.HttpContext!.User;
}
