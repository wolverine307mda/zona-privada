using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Papeles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class GarantiasLogicaTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 7);

    private static Garantia G(DateOnly hasta, string estado = "vigente") => new() { Producto = "Tele", FechaCompra = Hoy.AddDays(-100), GarantiaHasta = hasta, Estado = estado };

    [Fact]
    public void Por_defecto_son_tres_anios_de_garantia_y_catorce_dias_de_devolucion()
    {
        Assert.Equal(new DateOnly(2029, 11, 7), Garantias.GarantiaPorDefecto(Hoy));
        Assert.Equal(new DateOnly(2026, 11, 21), Garantias.DevolucionPorDefecto(Hoy));
        Assert.Equal(new DateOnly(2027, 2, 28), Garantias.GarantiaPorDefecto(new DateOnly(2024, 2, 29)).AddDays(0));   // el 29 de febrero cae en 28 en años no bisiestos
    }

    [Theory]
    [InlineData(400, "vigente")]
    [InlineData(61, "vigente")]
    [InlineData(60, "por-caducar")]
    [InlineData(0, "por-caducar")]
    [InlineData(-1, "caducada")]
    public void La_situacion_sale_de_los_dias_que_quedan(int dias, string esperado) =>
        Assert.Equal(esperado, Garantias.Situacion(G(Hoy.AddDays(dias)), Hoy));

    [Fact]
    public void Lo_devuelto_o_reclamado_no_cuenta_como_caducado_ni_por_caducar()
    {
        Assert.Equal("devuelta", Garantias.Situacion(G(Hoy.AddDays(-10), "devuelta"), Hoy));
        Assert.Equal("reclamada", Garantias.Situacion(G(Hoy.AddDays(3), "reclamada"), Hoy));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(61, 0)]
    [InlineData(60, 1)]
    [InlineData(31, 1)]
    [InlineData(30, 2)]
    [InlineData(8, 2)]
    [InlineData(7, 3)]
    [InlineData(0, 3)]
    [InlineData(-1, 0)]
    public void El_escalon_de_aviso_es_60_30_y_7_dias(int dias, int escalon) => Assert.Equal(escalon, Garantias.Escalon(dias));

    [Fact]
    public void La_validacion_pide_producto_y_fecha_y_rechaza_fechas_y_estados_raros()
    {
        Assert.Equal(2, Garantias.Validar(new GarantiaEntrada(null, null, null, null, null, null, null, null, null), true).Count);
        Assert.Empty(Garantias.Validar(new GarantiaEntrada("Tele", "Mediamarkt", "2026-11-01", null, null, null, null, null, null), true));
        var mal = Garantias.Validar(new GarantiaEntrada("Tele", null, "ayer", "2029-13-45", "mañana", null, "perdida", new string('x', 501), null), true);
        Assert.Contains("fechaCompra", mal.Keys); Assert.Contains("garantiaHasta", mal.Keys); Assert.Contains("devolucionHasta", mal.Keys);
        Assert.Contains("estado", mal.Keys); Assert.Contains("notas", mal.Keys);
        Assert.Empty(Garantias.Validar(new GarantiaEntrada(null, null, null, null, null, null, "devuelta", null, null), false));     // al editar solo se valida lo que llega
    }
}

public class GarantiasAvisosTests
{
    private sealed class AvisosFalsos : IAvisos
    {
        public readonly List<string> Telegram = [];
        public Canales Disponibles() => throw new NotImplementedException();
        public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorTelegram(string texto, CancellationToken ct = default) { Telegram.Add(texto); return Task.FromResult(true); }
    }

    private static readonly DateOnly Hoy = new(2026, 11, 7);

    private static async Task<(Mda.Api.Data.AppDbContext Db, AvisosFalsos Av)> Con(params Garantia[] g)
    {
        var db = BaseDeDatos.Nueva();
        db.Garantias.AddRange(g);
        await db.SaveChangesAsync();
        return (db, new AvisosFalsos());
    }

    private static Garantia G(int diasGarantia, int? diasDevolucion = null, string estado = "vigente") => new()
    {
        Producto = "Aspiradora", Comercio = "PcComponentes", FechaCompra = Hoy.AddDays(-5), GarantiaHasta = Hoy.AddDays(diasGarantia), Estado = estado,
        DevolucionHasta = diasDevolucion is { } d ? Hoy.AddDays(d) : null,
    };

    [Fact]
    public async Task Avisa_una_vez_por_escalon_y_no_repite_si_se_vuelve_a_comprobar()
    {
        var (db, av) = await Con(G(50));
        await Garantias.Comprobar(db, av, Hoy, NullLogger.Instance, default);
        await Garantias.Comprobar(db, av, Hoy, NullLogger.Instance, default);
        Assert.Single(av.Telegram);
        Assert.Contains("Aspiradora", av.Telegram[0]);
        Assert.Contains("50 día(s)", av.Telegram[0]);

        await Garantias.Comprobar(db, av, Hoy.AddDays(25), NullLogger.Instance, default);      // quedan 25: salta el de 30
        Assert.Equal(2, av.Telegram.Count);
        await Garantias.Comprobar(db, av, Hoy.AddDays(44), NullLogger.Instance, default);      // quedan 6: salta el de 7
        Assert.Equal(3, av.Telegram.Count);
    }

    [Fact]
    public async Task Una_garantia_lejana_o_ya_cerrada_no_avisa()
    {
        var (db, av) = await Con(G(500), G(20, estado: "devuelta"), G(20, estado: "reclamada"), G(-3));
        await Garantias.Comprobar(db, av, Hoy, NullLogger.Instance, default);
        Assert.Empty(av.Telegram);
    }

    [Fact]
    public async Task Avisa_tres_dias_antes_de_que_acabe_la_devolucion_una_sola_vez()
    {
        var (db, av) = await Con(G(1000, diasDevolucion: 2));
        await Garantias.Comprobar(db, av, Hoy, NullLogger.Instance, default);
        await Garantias.Comprobar(db, av, Hoy, NullLogger.Instance, default);
        Assert.Single(av.Telegram);
        Assert.Contains("devolver", av.Telegram[0]);
        Assert.Contains("PcComponentes", av.Telegram[0]);
    }

    [Fact]
    public async Task Una_devolucion_que_ya_paso_se_da_por_avisada_sin_molestar()
    {
        var (db, av) = await Con(G(1000, diasDevolucion: -2));
        await Garantias.Comprobar(db, av, Hoy, NullLogger.Instance, default);
        Assert.Empty(av.Telegram);
        Assert.True((await db.Garantias.FindAsync(1))!.AvisadoDevolucion);
    }
}

public class GarantiasApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Se_crea_con_las_reglas_por_defecto_se_lista_se_cierra_y_se_borra()
    {
        var c = await factory.Dentro();
        var r = await c.PostAsJsonAsync("/api/garantias", new { producto = "Auriculares", comercio = "Amazon", fechaCompra = "2026-11-01" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var g = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2029-11-01", g.GetProperty("garantiaHasta").GetString());
        Assert.Equal("2026-11-15", g.GetProperty("devolucionHasta").GetString());
        var id = g.GetProperty("id").GetInt32();

        var lista = await c.GetFromJsonAsync<JsonElement>("/api/garantias");
        Assert.Equal(3, lista.GetProperty("reglaAnios").GetInt32());
        Assert.Contains(lista.GetProperty("garantias").EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);

        var p = await c.PutAsJsonAsync($"/api/garantias/{id}", new { estado = "devuelta" });
        Assert.Equal("devuelta", (await p.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("situacion").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/garantias/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/garantias/{id}")).StatusCode);
    }

    [Fact]
    public async Task Se_puede_quitar_la_devolucion_y_cambiar_las_fechas_rearma_los_avisos()
    {
        var c = await factory.Dentro();
        var g = await (await c.PostAsJsonAsync("/api/garantias", new { producto = "Móvil", fechaCompra = "2026-01-10", sinDevolucion = true })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, g.GetProperty("devolucionHasta").ValueKind);
        var id = g.GetProperty("id").GetInt32();

        var e = await (await c.PutAsJsonAsync($"/api/garantias/{id}", new { garantiaHasta = "2031-01-10", devolucionHasta = "2026-02-01" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2031-01-10", e.GetProperty("garantiaHasta").GetString());
        Assert.Equal("2026-02-01", e.GetProperty("devolucionHasta").GetString());
    }

    [Fact]
    public async Task Lo_que_viene_mal_da_un_error_claro()
    {
        var c = await factory.Dentro();
        var r = await c.PostAsJsonAsync("/api/garantias", new { producto = "", fechaCompra = "nunca" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/garantias/99999", new { estado = "devuelta" })).StatusCode);
    }

    [Fact]
    public async Task Hace_falta_entrar()
    {
        using var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/garantias")).StatusCode);
    }
}
