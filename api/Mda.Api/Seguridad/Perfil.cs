using System.Net.Mail;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Mda.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

public record PerfilEntrada(string? Nombre, string? Apellidos, string? Email, string? Telefono);

// El perfil del dueño: el usuario para entrar no cambia; nombre, apellidos, correo, teléfono y foto, sí.
// El correo y el teléfono van en la cuenta de Identity; el nombre y los apellidos, en ajustes.
public static partial class Perfil
{
    private const string ClaveNombre = "perfil-nombre", ClaveApellidos = "perfil-apellidos";

    [GeneratedRegex(@"^\+?[0-9 ]{9,20}$")]
    private static partial Regex Telefono();

    public static void MapPerfil(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/perfil").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal user, UserManager<IdentityUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var u = await users.GetUserAsync(user);
            if (u is null) return Results.Unauthorized();
            return Results.Ok(new
            {
                usuario = u.UserName,
                nombre = (await db.Ajustes.FindAsync([ClaveNombre], ct))?.Valor ?? "",
                apellidos = (await db.Ajustes.FindAsync([ClaveApellidos], ct))?.Valor ?? "",
                email = u.Email ?? "",
                telefono = u.PhoneNumber ?? "",
                foto = await db.Adjuntos.AnyAsync(a => a.Clave == "avatar", ct),
            });
        });

        g.MapPut("/", async (PerfilEntrada e, ClaimsPrincipal user, UserManager<IdentityUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var u = await users.GetUserAsync(user);
            if (u is null) return Results.Unauthorized();
            var errores = new Dictionary<string, string[]>();
            var nombre = e.Nombre?.Trim() ?? "";
            var apellidos = e.Apellidos?.Trim() ?? "";
            var email = e.Email?.Trim() ?? "";
            var telefono = Regex.Replace(e.Telefono?.Trim() ?? "", @"\s+", " ");
            if (nombre.Length > 60) errores["nombre"] = ["Máximo 60 caracteres."];
            if (apellidos.Length > 100) errores["apellidos"] = ["Máximo 100 caracteres."];
            if (email.Length == 0 || email.Length > 200 || !MailAddress.TryCreate(email, out _)) errores["email"] = ["Escribe un correo válido."];
            if (telefono.Length > 0 && !Telefono().IsMatch(telefono)) errores["telefono"] = ["Escribe un teléfono válido (9 cifras, con prefijo si quieres)."];
            if (errores.Count > 0) return Results.ValidationProblem(errores);

            // se cambian los campos sin tocar el sello de seguridad: así no se cierran las sesiones abiertas
            u.Email = email;
            u.NormalizedEmail = users.NormalizeEmail(email);
            u.PhoneNumber = telefono.Length > 0 ? telefono : null;
            var r = await users.UpdateAsync(u);
            if (!r.Succeeded) return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = r.Errors.Select(x => x.Description).ToArray() });
            await Guardar(db, ClaveNombre, nombre, ct);
            await Guardar(db, ClaveApellidos, apellidos, ct);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapGet("/foto", async (AppDbContext db, CancellationToken ct) =>
            await db.Adjuntos.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == "avatar", ct) is { } a ? Results.File(a.Datos, a.Tipo) : Results.NotFound());

        g.MapPut("/foto", async (HttpRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var (datos, tipo, error) = await Adjunto.Leer(req, ct);
            if (error is not null) return error;
            var a = await db.Adjuntos.FindAsync(["avatar"], ct);
            if (a is null) db.Adjuntos.Add(a = new Adjunto { Clave = "avatar" });
            a.Datos = datos!;
            a.Tipo = tipo;
            a.Creado = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).DisableAntiforgery();

        g.MapDelete("/foto", async (AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Adjuntos.FindAsync(["avatar"], ct) is { } a) { db.Adjuntos.Remove(a); await db.SaveChangesAsync(ct); }
            return Results.NoContent();
        });
    }

    private static async Task Guardar(AppDbContext db, string clave, string valor, CancellationToken ct)
    {
        var a = await db.Ajustes.FindAsync([clave], ct);
        if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = clave });
        a.Valor = valor;
    }
}
