using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Tiempo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mda.Api.Tests;

// La API con Open-Meteo de mentira: ni red ni nubes reales
public class LuzFactory : ModulosFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        var hoy = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IHttpClientFactory>(new NubesFalsas(hoy, 30, 5, 30, 50, 0, 168))));
    }
}

public class LuzPantallaTests(LuzFactory factory) : IClassFixture<LuzFactory>
{
    private async Task GuardarLugar()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ServicioTiempo>().GuardarLugar(scope.ServiceProvider.GetRequiredService<AppDbContext>(), new Lugar("Madrid", 40.4168, -3.7038), default);
    }

    [Fact]
    public async Task Sin_sesion_no_se_ve_nada()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/luz/dia")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/luz/semana")).StatusCode);
    }

    [Fact]
    public async Task El_dia_trae_efemerides_y_144_puntos_con_acimut_sombra_y_nota()
    {
        await GuardarLugar();
        var c = await factory.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/luz/dia");
        Assert.False(j.GetProperty("sinLugar").GetBoolean());
        Assert.Equal("Madrid", j.GetProperty("lugar").GetString());
        var serie = j.GetProperty("serie");
        Assert.Equal(145, serie.GetArrayLength()); // de 00:00 a 24:00 cada 10 min
        var mediodia = serie.EnumerateArray().First(p => p.GetProperty("min").GetInt32() == 13 * 60);
        Assert.True(mediodia.GetProperty("sol").GetProperty("alt").GetDouble() > 20);
        Assert.NotEqual(JsonValueKind.Null, mediodia.GetProperty("sombra").ValueKind);
        Assert.Equal(30, mediodia.GetProperty("nubes").GetProperty("total").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, mediodia.GetProperty("nota").ValueKind);
        var medianoche = serie[0];
        Assert.Contains(medianoche.GetProperty("etapa").GetString(), new[] { "noche", "astronomico" }); // en verano en Madrid no oscurece del todo
        Assert.Equal(JsonValueKind.Null, medianoche.GetProperty("sombra").ValueKind); // de noche no hay sombra
        Assert.True(j.GetProperty("efemerides").GetProperty("amanecer").ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task Una_fecha_fuera_de_rango_o_mal_escrita_no_rompe()
    {
        await GuardarLugar();
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/luz/dia?fecha=2099-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/luz/dia?fecha=no-es-una-fecha")).StatusCode); // vuelve a hoy
    }

    [Fact]
    public async Task La_semana_trae_siete_dias_y_los_mejores_momentos_ordenados()
    {
        await GuardarLugar();
        var c = await factory.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/luz/semana");
        Assert.Equal(7, j.GetProperty("dias").GetArrayLength());
        var notas = j.GetProperty("mejores").EnumerateArray().Select(m => m.GetProperty("puntuacion").GetInt32()).ToList();
        Assert.True(notas.Count <= 5);
        Assert.Equal(notas.OrderByDescending(n => n), notas);
        Assert.All(j.GetProperty("dias").EnumerateArray().Take(3), d => Assert.True(d.GetProperty("ventanas").GetArrayLength() >= 1));
    }
}
