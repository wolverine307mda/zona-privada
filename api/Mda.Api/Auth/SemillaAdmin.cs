using Mda.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Auth;

// Al arrancar: aplica las migraciones y crea el usuario administrador si no existe.
// Email y contraseña salen de la configuración (Admin__Email / Admin__Password), nunca del código.
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

        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        if (await users.FindByEmailAsync(email) is not null) return;

        var admin = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(admin, password);
        if (result.Succeeded) log.LogInformation("Usuario administrador creado");
        else log.LogError("No se pudo crear el administrador: {Errores}", string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
