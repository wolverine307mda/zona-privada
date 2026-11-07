using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Tiempo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class RopaLluviaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 7, 10, 7, 0, DateTimeKind.Utc);
    private static PuntoLluvia P(int minutosDesdeLas10, double mm) => new(new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc).AddMinutes(minutosDesdeLas10), mm);

    [Fact]
    public void Un_chubasco_que_viene_dice_en_cuantos_minutos_empieza()
    {
        var l = Ropa.EnMinutos([P(-15, 0), P(0, 0), P(15, 0), P(30, 1.2), P(45, 2.0)], Ahora);
        Assert.NotNull(l);
        Assert.Equal(23, l!.Minutos);                // el tramo de las 10:30 empieza 23 minutos después de las 10:07
        Assert.Equal(1.2, l.Mm);
    }

    [Fact]
    public void Si_ya_esta_lloviendo_son_cero_minutos()
    {
        var l = Ropa.EnMinutos([P(0, 0.8), P(15, 0.5)], Ahora);
        Assert.Equal(0, l!.Minutos);
    }

    [Fact]
    public void Un_tramo_suelto_de_llovizna_es_falsa_alarma_pero_dos_seguidos_no()
    {
        Assert.Null(Ropa.EnMinutos([P(0, 0), P(15, 0.1), P(30, 0), P(45, 0)], Ahora));
        Assert.NotNull(Ropa.EnMinutos([P(0, 0), P(15, 0.1), P(30, 0.1), P(45, 0)], Ahora));
        Assert.Null(Ropa.EnMinutos([P(0, 0), P(15, 0.05), P(30, 0.05)], Ahora));      // menos de 0,1 mm no moja
    }

    [Fact]
    public void Sin_lluvia_a_la_vista_o_sin_datos_no_hay_aviso()
    {
        Assert.Null(Ropa.EnMinutos([P(0, 0), P(15, 0), P(30, 0)], Ahora));
        Assert.Null(Ropa.EnMinutos([], Ahora));
        Assert.Null(Ropa.EnMinutos([new PuntoLluvia(Ahora.AddHours(5), 3)], Ahora));      // llueve, pero dentro de 5 horas
    }

    [Fact]
    public void La_lluvia_ya_pasada_no_cuenta()
    {
        Assert.Null(Ropa.EnMinutos([P(-60, 3), P(-45, 3), P(-30, 2), P(-15, 1)], Ahora));
    }

    [Fact]
    public void La_frase_es_prudente()
    {
        Assert.Equal("Probablemente llueva en ~25 min: recoge la ropa", Ropa.Frase(new LluviaProxima(25, 1)));
        Assert.Equal("Está lloviendo (o a punto): recoge la ropa", Ropa.Frase(new LluviaProxima(0, 1)));
    }
}

public class RopaVigilanciaTests
{
    private sealed class LluviaFalsa(params PuntoLluvia[] datos) : ILluvia
    {
        public Task<IReadOnlyList<PuntoLluvia>?> Proxima(Lugar lugar, CancellationToken ct) => Task.FromResult<IReadOnlyList<PuntoLluvia>?>(datos);
    }

    private sealed class AvisosFalsos : IAvisos
    {
        public readonly List<string> Push = [];
        public Canales Disponibles() => throw new NotImplementedException();
        public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorTelegram(string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorPush(string titulo, string texto, string? url = null, CancellationToken ct = default) { Push.Add(texto); return Task.FromResult(true); }
    }

    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 10, 7, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sin_ropa_tendida_no_avisa_aunque_llueva()
    {
        var db = BaseDeDatos.Nueva();
        // sin lugar guardado ni bot el aviso no sale; lo que comprobamos es que ni se mira
        Assert.Null(await Ropa.Desde(db, Ahora, default));
    }

    [Fact]
    public async Task La_marca_dura_diez_horas_y_se_quita_al_recoger()
    {
        var db = BaseDeDatos.Nueva();
        await Ropa.Tender(db, Ahora, default);
        Assert.Equal(Ahora, await Ropa.Desde(db, Ahora.AddHours(9), default));
        Assert.Null(await Ropa.Desde(db, Ahora.AddHours(10).AddMinutes(1), default));
        await Ropa.Recoger(db, default);
        Assert.Null(await Ropa.Desde(db, Ahora, default));
    }

    [Fact]
    public async Task Volver_a_tender_empieza_un_episodio_nuevo()
    {
        var db = BaseDeDatos.Nueva();
        await Ropa.Tender(db, Ahora, default);
        db.Ajustes.Add(new Mda.Api.Seguridad.Ajuste { Clave = Ropa.ClaveAvisada, Valor = "ya" }); await db.SaveChangesAsync();
        await Ropa.Tender(db, Ahora.AddHours(1), default);
        Assert.Null(await db.Ajustes.FindAsync(Ropa.ClaveAvisada));
    }
}

public class RopaFactory : ModulosFactory
{
    public List<PuntoLluvia> Datos { get; } = [];
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<ILluvia>(new LluviaFija(Datos))));
    }
    private sealed class LluviaFija(List<PuntoLluvia> datos) : ILluvia
    {
        public Task<IReadOnlyList<PuntoLluvia>?> Proxima(Lugar lugar, CancellationToken ct) => Task.FromResult<IReadOnlyList<PuntoLluvia>?>(datos.ToList());
    }
}

public class RopaApiTests(RopaFactory factory) : IClassFixture<RopaFactory>
{
    [Fact]
    public async Task Se_tiende_se_consulta_con_la_lluvia_y_se_recoge()
    {
        var c = await factory.Dentro();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await scope.ServiceProvider.GetRequiredService<ServicioTiempo>().GuardarLugar(db, new Lugar("Casa", 40.4, -3.7), default);
        }
        var inicio = await c.GetFromJsonAsync<JsonElement>("/api/tiempo/ropa");
        Assert.False(inicio.GetProperty("tendida").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/tiempo/ropa/tender", null)).StatusCode);
        factory.Datos.Clear();
        factory.Datos.Add(new PuntoLluvia(DateTime.UtcNow.AddMinutes(20), 1.5));
        var r = await c.GetFromJsonAsync<JsonElement>("/api/tiempo/ropa");
        Assert.True(r.GetProperty("tendida").GetBoolean());
        Assert.InRange(r.GetProperty("lluvia").GetProperty("minutos").GetInt32(), 1, 25);
        Assert.Contains("recoge la ropa", r.GetProperty("aviso").GetString());

        await c.PostAsync("/api/tiempo/ropa/recoger", null);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/tiempo/ropa")).GetProperty("tendida").GetBoolean());
    }

    [Fact]
    public async Task El_atajo_acepta_tendida_y_recogida_con_su_token_y_rechaza_otra_cosa()
    {
        var c = await factory.Dentro();
        var token = (await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "Ropa", alcances = new[] { "casa" } })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var movil = factory.CreateClient();
        movil.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await movil.PostAsJsonAsync("/api/atajos/ropa", new { estado = "tendida" })).StatusCode);
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/tiempo/ropa")).GetProperty("tendida").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await movil.PostAsJsonAsync("/api/atajos/ropa", new { estado = "recogida" })).StatusCode);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/tiempo/ropa")).GetProperty("tendida").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await movil.PostAsJsonAsync("/api/atajos/ropa", new { estado = "quizá" })).StatusCode);
    }
}
