using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Pc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

// el historial de latencia del router: lecturas de cada minuto, resumidas por horas al pasar 48 h
public class HistorialRedTests(RedFactory factory) : IClassFixture<RedFactory>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Agrupar_cuenta_las_lecturas_las_caidas_y_la_latencia_media_y_maxima()
    {
        var h = HistorialRed.Agrupar(7, HistorialRed.Hora(Ahora),
        [
            new() { EnLinea = true, Ms = 10 }, new() { EnLinea = true, Ms = 30 }, new() { EnLinea = false }, new() { EnLinea = true, Ms = 20 },
        ]);
        Assert.Equal(4, h.Muestras); Assert.Equal(1, h.Caidas); Assert.Equal(20, h.MediaMs); Assert.Equal(30, h.MaxMs);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero), h.Hora);
        Assert.Null(HistorialRed.Agrupar(7, Ahora, [new() { EnLinea = false }]).MediaMs);   // una hora entera caída no tiene latencia
    }

    [Fact]
    public async Task Compactar_resume_en_horas_completas_lo_de_mas_de_48_horas_y_conserva_lo_reciente()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.LecturasRed.AddRange(
            new LecturaRed { DispositivoId = 501, Hora = Ahora.AddHours(-60), EnLinea = true, Ms = 10 },
            new LecturaRed { DispositivoId = 501, Hora = Ahora.AddHours(-60).AddMinutes(5), EnLinea = false },
            new LecturaRed { DispositivoId = 501, Hora = Ahora.AddHours(-1), EnLinea = true, Ms = 12 });
        db.LecturasHora.Add(new LecturaHora { DispositivoId = 501, Hora = Ahora.AddDays(-40), Muestras = 60 });   // más de 30 días: se borra
        await db.SaveChangesAsync();

        await HistorialRed.Compactar(db, Ahora);

        Assert.Equal(1, await db.LecturasRed.CountAsync(l => l.DispositivoId == 501));                      // la de hace una hora sigue en bruto
        var hora = await db.LecturasHora.AsNoTracking().SingleAsync(h => h.DispositivoId == 501);
        Assert.Equal(2, hora.Muestras); Assert.Equal(1, hora.Caidas); Assert.Equal(10, hora.MediaMs);
    }

    [Fact]
    public async Task El_vigilante_guarda_lecturas_solo_de_los_aparatos_con_historial_y_la_gráfica_las_devuelve()
    {
        var c = await factory.Dentro();
        RedFactory.EnLinea["192.168.1.1"] = true; RedFactory.EnLinea["192.168.1.2"] = true;
        var router = (await (await c.PostAsJsonAsync("/api/red", new { nombre = "Router", host = "192.168.1.1", puerto = 80, icono = "router", historial = true })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var otro = (await (await c.PostAsJsonAsync("/api/red", new { nombre = "Sin historial", host = "192.168.1.2", puerto = 80 })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var vigilante = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<VigilanteRed>().Single();

        await vigilante.Revisar(default); await vigilante.Revisar(default);
        RedFactory.EnLinea["192.168.1.1"] = false;
        await vigilante.Revisar(default);

        var j = await c.GetFromJsonAsync<JsonElement>($"/api/red/{router}/historial?horas=24");
        var resumen = j.GetProperty("resumen");
        Assert.Equal(3, resumen.GetProperty("muestras").GetInt32());
        Assert.Equal(1, resumen.GetProperty("caidas").GetInt32());
        Assert.Equal(66.67, resumen.GetProperty("disponibilidad").GetDouble());
        Assert.Equal(3, resumen.GetProperty("mediaMs").GetInt32());                                         // el comprobador falso responde en 3 ms
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>($"/api/red/{otro}/historial")).GetProperty("resumen").GetProperty("muestras").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/red/99999/historial")).StatusCode);
        RedFactory.EnLinea["192.168.1.1"] = true;
    }
}

// su propia clase y su propia base: las demás pruebas también crean aparatos con historial
public class HistorialLimiteTests(RedFactory factory) : IClassFixture<RedFactory>
{
    [Fact]
    public async Task El_historial_es_para_tres_aparatos_como_mucho()
    {
        var c = await factory.Dentro();
        var ok = 0;
        for (var i = 0; i < 5; i++)
        {
            var r = await c.PostAsJsonAsync("/api/red", new { nombre = $"Con historial {i}", host = "192.168.1.50", puerto = 80, historial = true });
            if (r.StatusCode == HttpStatusCode.OK) ok++;
        }
        Assert.True(ok <= HistorialRed.MaxPorDispositivo);
    }
}
