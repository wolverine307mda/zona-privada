using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Pc;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class PresenciaTests(RedFactory factory) : IClassFixture<RedFactory>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static (bool?, DateTimeOffset?) Movil(bool? enLinea, int minutosDesdeElCambio) => (enLinea, Ahora.AddMinutes(-minutosDesdeElCambio));

    [Fact]
    public void Con_el_movil_en_la_red_estas_en_casa_y_sin_el_te_has_ido_solo_pasado_un_rato()
    {
        Assert.Null(PresenciaCasa.Estado([], Ahora));                                   // sin móviles marcados, no se sabe nada
        Assert.Null(PresenciaCasa.Estado([(null, null)], Ahora));                        // sin primera lectura
        Assert.Equal("llegada", PresenciaCasa.Estado([Movil(true, 0)], Ahora));          // llegar es inmediato
        Assert.Null(PresenciaCasa.Estado([Movil(false, 3)], Ahora));                     // 3 minutos fuera: el móvil puede estar dormido
        Assert.Equal("salida", PresenciaCasa.Estado([Movil(false, 10)], Ahora));         // 10 minutos: te has ido
    }

    [Fact]
    public void Con_dos_moviles_basta_uno_en_casa_y_para_irse_tienen_que_irse_los_dos()
    {
        Assert.Equal("llegada", PresenciaCasa.Estado([Movil(false, 60), Movil(true, 1)], Ahora));
        Assert.Null(PresenciaCasa.Estado([Movil(false, 60), Movil(false, 4)], Ahora));
        Assert.Equal("salida", PresenciaCasa.Estado([Movil(false, 60), Movil(false, 30)], Ahora));
    }

    [Fact]
    public async Task El_vigilante_apunta_el_primer_estado_sin_hacer_nada_y_aplica_los_cambios_despues()
    {
        var c = await factory.Dentro();
        await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/salud")
        {
            Headers = { { "X-Agente-Token", ApiFactory.Token } },
            Content = JsonContent.Create(new { hora = DateTimeOffset.UtcNow, reinicioPendiente = false, discos = Array.Empty<object>(), volumenes = Array.Empty<object>() }),
        });
        RedFactory.EnLinea["192.168.1.77"] = true;
        var r = await c.PostAsJsonAsync("/api/red", new { nombre = "Móvil de Mario", host = "192.168.1.77", puerto = 62078, icono = "movil", presencia = true });
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var vigilante = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<VigilanteRed>().Single();
        factory.Avisos.Enviados.Clear();

        await vigilante.Revisar(default);                                                // primera lectura: en casa, pero no pasa nada
        Assert.DoesNotContain(factory.Avisos.Enviados, m => m.Contains("Llegaste") || m.Contains("Te vas"));

        RedFactory.EnLinea["192.168.1.77"] = false;
        await vigilante.Revisar(default); await vigilante.Revisar(default);               // ya figura fuera, pero hace menos de 10 minutos
        Assert.DoesNotContain(factory.Avisos.Enviados, m => m.Contains("Te vas de casa"));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.DispositivosRed.FindAsync(id))!.Cambio = DateTimeOffset.UtcNow.AddMinutes(-20); // lleva 20 minutos fuera
            await db.SaveChangesAsync();
        }
        await vigilante.Revisar(default);
        Assert.Single(factory.Avisos.Enviados, m => m.Contains("Te vas de casa"));
        await vigilante.Revisar(default);
        Assert.Single(factory.Avisos.Enviados, m => m.Contains("Te vas de casa"));        // no se repite

        RedFactory.EnLinea["192.168.1.77"] = true;
        await vigilante.Revisar(default); await vigilante.Revisar(default);
        Assert.Single(factory.Avisos.Enviados, m => m.Contains("Llegaste a casa"));
        RedFactory.EnLinea["192.168.1.77"] = true;
    }
}
