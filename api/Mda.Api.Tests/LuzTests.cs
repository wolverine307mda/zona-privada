using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Mda.Api.Agenda;
using Mda.Api.Data;
using Mda.Api.Luz;
using Mda.Api.Seguridad;
using Mda.Api.Tiempo;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = ahora;
    public override DateTimeOffset GetUtcNow() => Ahora.ToUniversalTime();
}

// Open-Meteo de mentira: las nubes por horas de 4 días desde «desde», todas iguales
public class NubesFalsas(DateOnly desde, int total, int baja, int media, int alta, int lluvia, int horasTotales = 96) : HttpMessageHandler, IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(this, false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var horas = Enumerable.Range(0, horasTotales).Select(i => desde.ToDateTime(TimeOnly.MinValue).AddHours(i)).ToList();
        JsonArray A<T>(Func<DateTime, T> f) => new(horas.Select(h => JsonValue.Create(f(h))).ToArray<JsonNode?>());
        var json = new JsonObject
        {
            ["hourly"] = new JsonObject
            {
                ["time"] = A(h => h.ToString("yyyy-MM-ddTHH:mm")), ["cloud_cover"] = A(_ => total), ["cloud_cover_low"] = A(_ => baja),
                ["cloud_cover_mid"] = A(_ => media), ["cloud_cover_high"] = A(_ => alta), ["precipitation_probability"] = A(_ => lluvia),
                ["visibility"] = A(_ => 24000.0), ["wind_speed_10m"] = A(_ => 8.0),
            },
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") });
    }
}

public class LuzTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
    private static readonly DateOnly Solsticio = new(2026, 6, 21);

    private static DateTimeOffset Local(int h, int m = 0)
    {
        var n = Solsticio.ToDateTime(new TimeOnly(h, m));
        return new DateTimeOffset(n, Madrid.GetUtcOffset(n));
    }

    private async Task<(AvisosLuz Servicio, RelojFijo Reloj)> Montar(NubesFalsas red, AjustesLuz ajustes, int hora, int minuto)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await scope.ServiceProvider.GetRequiredService<ServicioTiempo>().GuardarLugar(db, new Lugar("Madrid", 40.4168, -3.7038), default);
            await ServicioLuz.Guardar(db, ajustes, default);
            db.Ajustes.RemoveRange(db.Ajustes.Where(a => a.Clave == ServicioLuz.ClaveAvisado || a.Clave == Horario.Clave));
            await db.SaveChangesAsync();
        }
        var reloj = new RelojFijo(Local(hora, minuto));
        var zona = factory.Services.GetRequiredService<Zona>();
        var luz = new ServicioLuz(red, new MemoryCache(new MemoryCacheOptions()), zona, NullLogger<ServicioLuz>.Instance);
        var avisos = new AvisosLuz(factory.Services.GetRequiredService<IServiceScopeFactory>(), luz, factory.Services.GetRequiredService<ServicioTiempo>(),
            factory.Avisos, zona, reloj, NullLogger<AvisosLuz>.Instance);
        return (avisos, reloj);
    }

    [Fact]
    public async Task Avisa_de_la_hora_dorada_una_sola_vez_cuando_el_cielo_promete()
    {
        factory.Avisos.Enviados.Clear();
        // nubes altas y nada de nube baja: tarde bonita; la hora dorada empieza hacia las 21:10, se avisa 90 min antes
        var (servicio, reloj) = await Montar(new NubesFalsas(Solsticio, 45, 0, 40, 60, 0), new AjustesLuz(Activa: true, Umbral: 6, Astro: false), 19, 50);
        await servicio.Revisar(default);
        Assert.Contains(factory.Avisos.Enviados, e => e.StartsWith("push:🌇 Hora dorada"));
        Assert.Contains(factory.Avisos.Enviados, e => e.StartsWith("telegram:🌇 Hora dorada"));
        var enviados = factory.Avisos.Enviados.Count;
        reloj.Ahora = Local(20, 10);
        await servicio.Revisar(default);
        Assert.Equal(enviados, factory.Avisos.Enviados.Count); // no se repite el mismo día
    }

    [Fact]
    public async Task No_avisa_si_la_tarde_esta_tapada_si_aun_es_pronto_o_si_esta_apagado()
    {
        factory.Avisos.Enviados.Clear();
        var (tapada, _) = await Montar(new NubesFalsas(Solsticio, 98, 95, 60, 40, 60), new AjustesLuz(Activa: true, Umbral: 6, Astro: false), 19, 50);
        await tapada.Revisar(default);
        var (pronto, _) = await Montar(new NubesFalsas(Solsticio, 45, 0, 40, 60, 0), new AjustesLuz(Activa: true, Umbral: 6, Astro: false), 15, 0);
        await pronto.Revisar(default);
        var (apagado, _) = await Montar(new NubesFalsas(Solsticio, 45, 0, 40, 60, 0), new AjustesLuz(Activa: false, Astro: false), 19, 50);
        await apagado.Revisar(default);
        Assert.Empty(factory.Avisos.Enviados);
    }

    [Fact]
    public async Task No_avisa_si_estas_trabajando_a_esa_hora()
    {
        factory.Avisos.Enviados.Clear();
        var (servicio, _) = await Montar(new NubesFalsas(Solsticio, 45, 0, 40, 60, 0), new AjustesLuz(Activa: true, Umbral: 6, Astro: false), 19, 50);
        using (var scope = factory.Services.CreateScope())
            await Horario.Guardar(scope.ServiceProvider.GetRequiredService<AppDbContext>(), new Horario(true, "Turno de tarde", [1, 2, 3, 4, 5, 6, 7], "12:00", "23:00"), default);
        await servicio.Revisar(default);
        Assert.Empty(factory.Avisos.Enviados);
    }

    [Fact]
    public async Task Las_nubes_de_open_meteo_se_leen_por_horas_y_se_guardan()
    {
        var luz = new ServicioLuz(new NubesFalsas(Solsticio, 30, 5, 20, 40, 10), new MemoryCache(new MemoryCacheOptions()), factory.Services.GetRequiredService<Zona>(), NullLogger<ServicioLuz>.Instance);
        var horas = await luz.Nubes(new Lugar("Madrid", 40.4168, -3.7038), default);
        Assert.Equal(96, horas!.Count);
        Assert.Equal((30, 5, 20, 40, 10, 24.0), (horas[0].Total, horas[0].Baja, horas[0].Media, horas[0].Alta, horas[0].Lluvia, horas[0].VisibilidadKm));
    }

    [Fact]
    public async Task La_pantalla_de_luz_pide_sesion_y_los_ajustes_se_acotan()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/luz")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ServicioLuz.Guardar(db, new AjustesLuz(Activa: true, Umbral: 99, AntelacionMin: 1, Bortle: 0), default);
        var a = await ServicioLuz.Leer(db, default);
        Assert.Equal((10, 15, 1), (a.Umbral, a.AntelacionMin, a.Bortle));
    }
}
