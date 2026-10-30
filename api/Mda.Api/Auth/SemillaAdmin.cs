using Mda.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Auth;

// Al arrancar: aplica las migraciones y deja el usuario administrador tal y como dice la configuración
// (Admin__Usuario, Admin__Email, Admin__Password), nunca el código. Si ya existe y cambias el usuario
// o la contraseña en el .env, se actualiza al reiniciar.
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
        if (!await users.CheckPasswordAsync(admin, password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(admin);
            Informar(await users.ResetPasswordAsync(admin, token, password), "Contraseña del administrador actualizada");
        }
    }

    private void Informar(IdentityResult r, string ok)
    {
        if (r.Succeeded) log.LogInformation(ok);
        else log.LogError("Usuario administrador: {Errores}", string.Join("; ", r.Errors.Select(e => e.Description)));
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
