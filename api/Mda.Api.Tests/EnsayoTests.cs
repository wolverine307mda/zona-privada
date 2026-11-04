using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class EnsayoFactory : ModulosFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Escritorio:Ensayo", "true");
    }
}

// Modo ensayo: la copia de escritorio que corre a la vez que Docker no carga las claves de lo que habla con el exterior
public class EnsayoTests(EnsayoFactory ensayo, ModulosFactory normal) : IClassFixture<EnsayoFactory>, IClassFixture<ModulosFactory>
{
    private static async Task<IConfiguration> ConClaves(ModulosFactory f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var secretos = f.Services.GetRequiredService<Secretos>();
        await secretos.Poner(db, "telegram", "Token", "123:token-de-prueba", TimeProvider.System);
        await secretos.Poner(db, "tuya", "AccessId", "id-de-prueba", TimeProvider.System);
        await secretos.Poner(db, "spotify", "ClientId", "spotify-de-prueba", TimeProvider.System);
        return f.Services.GetRequiredService<IConfiguration>();
    }

    [Fact]
    public async Task En_ensayo_las_claves_de_telegram_tuya_y_spotify_no_se_cargan()
    {
        var c = await ConClaves(ensayo);
        Assert.True(string.IsNullOrEmpty(c["Telegram:Token"]));
        Assert.True(string.IsNullOrEmpty(c["Tuya:AccessId"]));
        Assert.True(string.IsNullOrEmpty(c["Spotify:ClientId"]));
    }

    [Fact]
    public async Task Sin_ensayo_se_cargan_como_siempre()
    {
        var c = await ConClaves(normal);
        Assert.Equal("123:token-de-prueba", c["Telegram:Token"]);
        Assert.Equal("id-de-prueba", c["Tuya:AccessId"]);
    }

    [Fact]
    public void Las_claves_silenciadas_son_las_de_servicios_que_hablan_hacia_fuera()
    {
        foreach (var s in new[] { "telegram", "whatsapp", "correo", "google", "tuya", "homeassistant" }) Assert.Contains(s, Secretos.Silenciables);
        Assert.DoesNotContain("vigilancia-local", Secretos.Silenciables);
    }
}
