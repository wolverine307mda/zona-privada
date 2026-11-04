using Mda.Api.Agenda;
using Mda.Api.Pc;
using Mda.Api.Sistema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class TemperaturaCpuTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static SaludPc Con(double? cpu, DateTimeOffset hora) => new(hora, [], [], false, cpu);

    [Fact]
    public void Solo_avisa_si_lleva_cinco_minutos_o_mas_por_encima_de_85()
    {
        Assert.DoesNotContain("cpu-temp", ReglasSalud.Evaluar(Con(90, T0), null, T0, null, calienteDesde: null).Keys);                       // primera lectura caliente: aún no
        Assert.DoesNotContain("cpu-temp", ReglasSalud.Evaluar(Con(90, T0), null, T0, null, calienteDesde: T0.AddMinutes(-4)).Keys);          // 4 minutos: un pico
        var activas = ReglasSalud.Evaluar(Con(90, T0), null, T0, null, calienteDesde: T0.AddMinutes(-6));
        Assert.Contains("Ahorro", activas["cpu-temp"]);                                                                                      // aconseja el plan de ahorro
        Assert.Contains("90", activas["cpu-temp"]);
        Assert.DoesNotContain("cpu-temp", ReglasSalud.Evaluar(Con(80, T0), null, T0, null, calienteDesde: T0.AddMinutes(-30)).Keys);        // ya bajó
        Assert.DoesNotContain("cpu-temp", ReglasSalud.Evaluar(Con(null, T0), null, T0, null, calienteDesde: T0.AddMinutes(-30)).Keys);      // el equipo no da temperatura
    }

    [Fact]
    public void Dice_cuando_se_arregla() => Assert.Equal("✅ El procesador ha bajado de temperatura", ReglasSalud.Resuelto("cpu-temp"));

    [Fact]
    public async Task El_servicio_apunta_desde_cuando_esta_caliente_avisa_y_avisa_de_que_se_arregla()
    {
        factory.Avisos.Enviados.Clear();
        var reloj = new RelojFijo(T0);
        var salud = new EstadoSalud(reloj);
        var servicio = new AvisosSalud(factory.Services.GetRequiredService<IServiceScopeFactory>(), salud, new EstadoSistema(), factory.Avisos,
            factory.Services.GetRequiredService<Zona>(), reloj, NullLogger<AvisosSalud>.Instance);

        salud.Recibir(Con(92, reloj.Ahora));
        await servicio.Revisar(default);
        Assert.DoesNotContain(factory.Avisos.Enviados, e => e.Contains("procesador"));                  // primera vez que se ve caliente: se apunta

        reloj.Ahora = reloj.Ahora.AddMinutes(10);
        salud.Recibir(Con(93, reloj.Ahora));
        await servicio.Revisar(default);
        Assert.Single(factory.Avisos.Enviados, e => e.StartsWith("telegram:💻 🟠 El procesador lleva más de 5 minutos"));

        reloj.Ahora = reloj.Ahora.AddMinutes(10);
        salud.Recibir(Con(60, reloj.Ahora));
        await servicio.Revisar(default);
        Assert.Contains(factory.Avisos.Enviados, e => e.Contains("✅ El procesador ha bajado de temperatura"));
    }
}
