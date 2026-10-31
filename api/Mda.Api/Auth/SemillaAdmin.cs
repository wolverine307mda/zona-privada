using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Auth;

// Al arrancar: aplica las migraciones y deja el usuario administrador tal y como dice la configuración
// (Admin__Usuario, Admin__Email, Admin__Password), nunca el código. Si ya existe y cambias el usuario
// o la contraseña en el .env, se actualiza al reiniciar (la contraseña, solo si cambia en el .env).
public class SemillaAdmin(IServiceProvider services, IConfiguration config, ILogger<SemillaAdmin> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational()) await db.Database.MigrateAsync(ct);
        else await db.Database.EnsureCreatedAsync(ct);

        var email = config["Admin:Email"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            log.LogWarning("Sin Admin:Email o Admin:Password: no se crea el usuario administrador");
            return;
        }
        // nombre con el que se entra; si no hay, se entra con el email
        var usuario = string.IsNullOrWhiteSpace(config["Admin:Usuario"]) ? email : config["Admin:Usuario"]!;

        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var admin = await users.FindByEmailAsync(email) ?? await users.FindByNameAsync(usuario);
        if (admin is null)
        {
            admin = new IdentityUser { UserName = usuario, Email = email, EmailConfirmed = true };
            Informar(await users.CreateAsync(admin, password), "Usuario administrador creado");
            return;
        }

        if (admin.UserName != usuario)
            Informar(await users.SetUserNameAsync(admin, usuario), "Nombre de usuario actualizado");

        // La contraseña del .env solo se impone cuando CAMBIA en el .env: se guarda una huella salada de la última que se
        // aplicó. Así una contraseña cambiada desde Perfil no se deshace en cada reinicio, y cambiar ADMIN_PASSWORD
        // sigue sirviendo para recuperar el acceso.
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<IdentityUser>>();
        var huella = await db.Ajustes.FindAsync([ClaveHuella], ct);
        var sinCambios = huella is not null && hasher.VerifyHashedPassword(admin, huella.Valor, password) != PasswordVerificationResult.Failed;
        if (sinCambios) return;

        var vale = await users.CheckPasswordAsync(admin, password);
        if (!vale)
        {
            var token = await users.GeneratePasswordResetTokenAsync(admin);
            var r = await users.ResetPasswordAsync(admin, token, password);
            Informar(r, "Contraseña del administrador actualizada desde el .env");
            vale = r.Succeeded;
        }
        // si no se pudo aplicar (p. ej. tiene menos de 12 caracteres) no se anota: se reintentará y el error saldrá en los registros
        if (!vale) return;
        if (huella is null) db.Ajustes.Add(huella = new Ajuste { Clave = ClaveHuella });
        huella.Valor = hasher.HashPassword(admin, password);
        await db.SaveChangesAsync(ct);
    }

    private const string ClaveHuella = "admin-clave-env";

    private void Informar(IdentityResult r, string ok)
    {
        if (r.Succeeded) log.LogInformation(ok);
        else log.LogError("Usuario administrador: {Errores}", string.Join("; ", r.Errors.Select(e => e.Description)));
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
