using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class CobroUnitTests
{
    private static Movimiento I(string fecha, decimal importe, string concepto = "Nómina") =>
        new() { Tipo = TipoMovimiento.Ingreso, Concepto = concepto, Importe = importe, Fecha = DateOnly.Parse(fecha) };

    private static readonly DateOnly Hoy = new(2026, 11, 10);

    [Fact]
    public void Una_nomina_regular_da_su_dia_y_es_fiable()
    {
        var d = DiaDeCobro.Detectar([I("2026-06-28", 1800), I("2026-07-28", 1800), I("2026-08-28", 1800), I("2026-09-28", 1810), I("2026-10-28", 1800)], Hoy)!;
        Assert.Equal(28, d.Dia);
        Assert.True(d.Fiable);
        Assert.Equal(5, d.Meses);
        Assert.Equal(1800, d.Tipico);
    }

    [Fact]
    public void Los_ingresos_pequenos_no_la_confunden()
    {
        var d = DiaDeCobro.Detectar([I("2026-07-28", 1800), I("2026-07-03", 40, "Bizum"), I("2026-08-28", 1800), I("2026-08-15", 25, "Bizum"),
            I("2026-09-28", 1800), I("2026-10-28", 1800), I("2026-10-01", 60, "Bizum")], Hoy)!;
        Assert.Equal(28, d.Dia);
        Assert.Equal(4, d.Meses);
    }

    [Fact]
    public void Un_mes_sin_nomina_no_cuenta_como_dia_de_cobro()
    {
        // en agosto solo entró un bizum de 30 €
        var d = DiaDeCobro.Detectar([I("2026-07-28", 1800), I("2026-08-14", 30, "Bizum"), I("2026-09-28", 1800), I("2026-10-28", 1800)], Hoy)!;
        Assert.Equal(28, d.Dia);
        Assert.Equal(3, d.Meses);
        Assert.True(d.Fiable);
    }

    [Fact]
    public void Con_menos_de_tres_meses_no_es_fiable_y_sin_ingresos_no_hay_nada()
    {
        var d = DiaDeCobro.Detectar([I("2026-09-28", 1800), I("2026-10-28", 1800)], Hoy)!;
        Assert.False(d.Fiable);
        Assert.Null(DiaDeCobro.Detectar([], Hoy));
        Assert.Null(DiaDeCobro.Detectar([new Movimiento { Tipo = TipoMovimiento.Gasto, Importe = 50, Fecha = new DateOnly(2026, 10, 1) }], Hoy));
    }

    [Fact]
    public void Si_el_dia_cambia_mucho_de_un_mes_a_otro_no_es_fiable()
    {
        var d = DiaDeCobro.Detectar([I("2026-06-05", 1800), I("2026-07-20", 1800), I("2026-08-12", 1800), I("2026-09-28", 1800), I("2026-10-02", 1800)], Hoy)!;
        Assert.False(d.Fiable);
    }

    [Fact]
    public void Un_cobro_en_fin_de_semana_se_adelanta_al_viernes_y_un_31_en_febrero_es_el_28()
    {
        Assert.Equal(new DateOnly(2026, 11, 27), DiaDeCobro.Pagado(2026, 11, 28));   // sábado
        Assert.Equal(new DateOnly(2026, 11, 27), DiaDeCobro.Pagado(2026, 11, 29));   // domingo
        Assert.Equal(new DateOnly(2026, 11, 30), DiaDeCobro.Pagado(2026, 11, 30));   // lunes
        Assert.Equal(new DateOnly(2026, 2, 27), DiaDeCobro.Pagado(2026, 2, 31));     // 28 de febrero de 2026 es sábado
        Assert.Equal(new DateOnly(2026, 10, 30), DiaDeCobro.Pagado(2026, 11, 1));    // el 1 de noviembre es domingo: se cobra el viernes 30 de octubre
    }

    [Fact]
    public void El_proximo_cobro_es_el_de_este_mes_si_no_ha_pasado_y_el_del_siguiente_si_ya_cobraste()
    {
        Assert.Equal((new DateOnly(2026, 11, 27), false), DiaDeCobro.Proximo(new DateOnly(2026, 11, 10), 28, false));
        Assert.Equal((new DateOnly(2026, 12, 28), false), DiaDeCobro.Proximo(new DateOnly(2026, 11, 29), 28, true));
        Assert.Equal((new DateOnly(2026, 12, 28), false), DiaDeCobro.Proximo(new DateOnly(2026, 11, 28), 28, true));
    }

    [Fact]
    public void Si_hoy_es_el_dia_o_ya_paso_y_no_ha_entrado_el_cobro_es_hoy_y_si_paso_se_marca_retrasado()
    {
        Assert.Equal((new DateOnly(2026, 11, 27), false), DiaDeCobro.Proximo(new DateOnly(2026, 11, 27), 28, false));
        Assert.Equal((new DateOnly(2026, 11, 30), true), DiaDeCobro.Proximo(new DateOnly(2026, 11, 30), 28, false));
    }

    [Fact]
    public void Gana_lo_manual_despues_el_fijo_y_despues_lo_detectado()
    {
        var det = new CobroDetectado(28, 5, true, 1800);
        var movs = new List<Movimiento>();
        Assert.Equal("manual", DiaDeCobro.Estado(Hoy, 15, (1, 1500), det, movs, 600).Fuente);
        Assert.Equal(15, DiaDeCobro.Estado(Hoy, 15, (1, 1500), det, movs, 600).Dia);
        Assert.Equal("fijo", DiaDeCobro.Estado(Hoy, null, (1, 1500), det, movs, 600).Fuente);
        Assert.Equal("detectado", DiaDeCobro.Estado(Hoy, null, null, det, movs, 600).Fuente);
        Assert.Null(DiaDeCobro.Estado(Hoy, null, null, det with { Fiable = false }, movs, 600).Dia);
    }

    [Fact]
    public void El_dinero_hasta_cobrar_se_reparte_entre_los_dias_que_faltan()
    {
        var e = DiaDeCobro.Estado(new DateOnly(2026, 11, 18), 27, null, null, [], 340);   // el viernes 27: faltan 9 días
        Assert.Equal(new DateOnly(2026, 11, 27), e.Proximo);
        Assert.Equal(9, e.DiasHasta);
        Assert.Equal(37.78m, e.PorDia);
        Assert.False(e.Retrasado);
    }

    [Fact]
    public void Sin_dinero_libre_no_queda_nada_por_dia_y_el_mismo_dia_cuenta_como_un_dia()
    {
        Assert.Equal(0, DiaDeCobro.Estado(Hoy, 27, null, null, [], -20).PorDia);
        var hoyEsElDia = DiaDeCobro.Estado(new DateOnly(2026, 11, 27), 27, null, null, [], 50);
        Assert.Equal(0, hoyEsElDia.DiasHasta);
        Assert.Equal(50, hoyEsElDia.PorDia);   // se reparte en un día, no entre cero
    }

    [Fact]
    public void Haber_cobrado_este_mes_es_tener_un_ingreso_parecido_a_la_nomina()
    {
        Assert.True(DiaDeCobro.HaCobrado([I("2026-11-02", 1790)], Hoy, 1800));
        Assert.False(DiaDeCobro.HaCobrado([I("2026-11-02", 100, "Bizum")], Hoy, 1800));
        Assert.False(DiaDeCobro.HaCobrado([I("2026-10-28", 1800)], Hoy, 1800));          // el de octubre no cuenta
        Assert.True(DiaDeCobro.HaCobrado([I("2026-11-02", 5, "Algo")], Hoy, 0));          // sin nómina conocida, cualquier ingreso
    }
}

public class CobroAvisosTests
{
    private sealed class Avisos : IAvisos
    {
        public readonly List<string> Telegram = [];
        public Canales Disponibles() => throw new NotImplementedException();
        public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorTelegram(string texto, CancellationToken ct = default) { lock (Telegram) Telegram.Add(texto); return Task.FromResult(true); }
    }

    private static async Task<AppDbContext> ConNomina28(params string[] meses)
    {
        var db = BaseDeDatos.Nueva();
        foreach (var f in meses) db.Movimientos.Add(new Movimiento { Tipo = TipoMovimiento.Ingreso, Concepto = "Nómina", Importe = 1800, Categoria = "nomina", Fecha = DateOnly.Parse(f) });
        db.Ajustes.Add(new Mda.Api.Seguridad.Ajuste { Clave = DiaDeCobro.ClaveDia, Valor = "27" });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task Esperar(Avisos a, int n) { for (var i = 0; i < 100 && a.Telegram.Count < n; i++) await Task.Delay(20); }

    [Fact]
    public async Task Avisa_una_sola_vez_si_llega_el_dia_de_cobro_y_no_ha_entrado_la_nomina()
    {
        var db = await ConNomina28("2026-07-28", "2026-08-28", "2026-09-28", "2026-10-28");
        var a = new Avisos();
        var tarde = new DateTime(2026, 11, 27, 21, 0, 0);       // viernes 27, 21:00
        await DiaDeCobro.Comprobar(db, a, tarde, NullLogger.Instance, default);
        await DiaDeCobro.Comprobar(db, a, tarde.AddHours(1), NullLogger.Instance, default);
        await Esperar(a, 1);
        Assert.Single(a.Telegram);
        Assert.Contains("nómina", a.Telegram[0]);
    }

    [Fact]
    public async Task No_avisa_antes_de_las_20_ni_antes_del_dia_ni_si_ya_entro()
    {
        var db = await ConNomina28("2026-07-28", "2026-08-28", "2026-09-28", "2026-10-28");
        var a = new Avisos();
        await DiaDeCobro.Comprobar(db, a, new DateTime(2026, 11, 27, 12, 0, 0), NullLogger.Instance, default);   // de día
        await DiaDeCobro.Comprobar(db, a, new DateTime(2026, 11, 26, 21, 0, 0), NullLogger.Instance, default);   // un día antes
        db.Movimientos.Add(new Movimiento { Tipo = TipoMovimiento.Ingreso, Concepto = "Nómina", Importe = 1800, Categoria = "nomina", Fecha = new DateOnly(2026, 11, 27) });
        await db.SaveChangesAsync();
        await DiaDeCobro.Comprobar(db, a, new DateTime(2026, 11, 27, 21, 0, 0), NullLogger.Instance, default);   // ya entró
        await Task.Delay(100);
        Assert.Empty(a.Telegram);
    }

    [Fact]
    public async Task Al_llegar_una_nomina_avisa_para_repartirla_una_vez_al_mes_y_un_bizum_no_avisa()
    {
        var db = await ConNomina28("2026-07-28", "2026-08-28", "2026-09-28", "2026-10-28");
        var a = new Avisos();
        var bizum = new Movimiento { Id = 900, Tipo = TipoMovimiento.Ingreso, Concepto = "Bizum", Importe = 30, Fecha = new DateOnly(2026, 11, 5) };
        await DiaDeCobro.AlRecibirIngreso(db, a, bizum, NullLogger.Instance, default);
        var nomina = new Movimiento { Id = 901, Tipo = TipoMovimiento.Ingreso, Concepto = "Nómina", Importe = 1800, Fecha = new DateOnly(2026, 11, 27) };
        await DiaDeCobro.AlRecibirIngreso(db, a, nomina, NullLogger.Instance, default);
        await DiaDeCobro.AlRecibirIngreso(db, a, nomina, NullLogger.Instance, default);        // el mismo mes otra vez: no repite
        await Esperar(a, 1);
        await Task.Delay(100);
        Assert.Single(a.Telegram);
        Assert.Contains("Nómina recibida", a.Telegram[0]);
    }
}

public class CobroApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= factory.Dentro();

    [Fact]
    public async Task Sin_datos_no_hay_dia_y_se_puede_poner_a_mano_y_volver_al_automatico()
    {
        var c = await Dentro();
        var vacio = await c.GetFromJsonAsync<JsonElement>("/api/gastos/cobro");
        Assert.Equal(JsonValueKind.Null, vacio.GetProperty("dia").ValueKind);

        var manual = await (await c.PutAsJsonAsync("/api/gastos/cobro", new { dia = 15 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(15, manual.GetProperty("dia").GetInt32());
        Assert.Equal("manual", manual.GetProperty("fuente").GetString());
        Assert.NotEqual(JsonValueKind.Null, manual.GetProperty("proximo").ValueKind);

        var auto = await (await c.PutAsJsonAsync("/api/gastos/cobro", new { dia = (int?)null })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, auto.GetProperty("dia").ValueKind);
    }

    [Fact]
    public async Task Rechaza_un_dia_fuera_del_mes()
    {
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/gastos/cobro", new { dia = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/gastos/cobro", new { dia = 32 })).StatusCode);
    }
}
