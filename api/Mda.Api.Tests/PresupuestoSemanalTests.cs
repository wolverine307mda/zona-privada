using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class PresupuestoSemanalUnitTests
{
    [Theory]
    [InlineData("2026-11-02", "2026-11-02")]   // lunes
    [InlineData("2026-11-08", "2026-11-02")]   // domingo
    [InlineData("2026-11-04", "2026-11-02")]   // miércoles
    [InlineData("2026-11-09", "2026-11-09")]   // el lunes siguiente
    public void La_semana_empieza_el_lunes(string dia, string lunes) => Assert.Equal(DateOnly.Parse(lunes), Semana.Lunes(DateOnly.Parse(dia)));

    [Theory]
    [InlineData("2026-11-02", 7)]   // lunes: quedan los siete días
    [InlineData("2026-11-05", 4)]
    [InlineData("2026-11-08", 1)]   // domingo: solo hoy
    public void Los_dias_que_quedan_cuentan_hoy(string dia, int quedan) => Assert.Equal(quedan, Semana.DiasQuedan(DateOnly.Parse(dia)));

    [Fact]
    public void Una_semana_que_cruza_el_anio_tiene_una_sola_clave()
    {
        // el lunes 29 de diciembre de 2025 abre la semana que acaba el domingo 4 de enero de 2026
        Assert.Equal(Semana.Clave(new DateOnly(2025, 12, 29)), Semana.Clave(new DateOnly(2026, 1, 3)));
        Assert.Equal("2026-W01", Semana.Clave(new DateOnly(2025, 12, 31)));
        Assert.NotEqual(Semana.Clave(new DateOnly(2026, 1, 4)), Semana.Clave(new DateOnly(2026, 1, 5)));
    }

    [Fact]
    public void El_estado_dice_el_porcentaje_los_dias_y_lo_que_puedes_gastar_al_dia()
    {
        var e = PresupuestosSemana.Estado("ocio", 100, false, 60, 0, new DateOnly(2026, 11, 5)); // jueves: quedan 4 días
        Assert.Equal(60, e.Porcentaje);
        Assert.Equal(4, e.DiasRestantes);
        Assert.Equal(10, e.PorDia);                          // 40 € entre 4 días
        Assert.Equal(new DateOnly(2026, 11, 9), e.Reinicio);
    }

    [Fact]
    public void Pasado_el_limite_no_queda_nada_por_dia_y_el_porcentaje_sigue_subiendo()
    {
        var e = PresupuestosSemana.Estado("ocio", 100, false, 130, 0, new DateOnly(2026, 11, 3));
        Assert.Equal(130, e.Porcentaje);
        Assert.Equal(0, e.PorDia);
    }

    [Theory]
    [InlineData(79.99, 0)]
    [InlineData(80, 80)]
    [InlineData(99.99, 80)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void Los_niveles_de_aviso_son_80_y_100(double gastado, int nivel) => Assert.Equal(nivel, PresupuestosSemana.Nivel((decimal)gastado, 100));

    [Fact]
    public void Lo_que_sobra_de_la_semana_pasada_se_arrastra_con_tope_de_la_mitad()
    {
        Assert.Equal(0, PresupuestosSemana.Arrastre(100, 20, false));        // sin la opción, nada
        Assert.Equal(30, PresupuestosSemana.Arrastre(100, 70, true));        // sobraron 30
        Assert.Equal(50, PresupuestosSemana.Arrastre(100, 0, true));         // sobraron 100, pero el tope es la mitad
        Assert.Equal(0, PresupuestosSemana.Arrastre(100, 140, true));        // te pasaste: no se resta de la siguiente
        var e = PresupuestosSemana.Estado("ocio", 100, true, 75, 70, new DateOnly(2026, 11, 4));
        Assert.Equal(130, e.LimiteEfectivo);
        Assert.Equal(57.7, e.Porcentaje);
    }
}

public class PresupuestoSemanalTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= factory.Dentro();

    [Fact]
    public async Task Se_crea_se_lee_y_se_quita_un_presupuesto_semanal()
    {
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/gastos/presupuestos-semana/ocio", new { limite = 40, arrastrar = true })).StatusCode);
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/gastos/presupuestos-semana");
        var ocio = lista.GetProperty("presupuestos").EnumerateArray().Single(p => p.GetProperty("categoria").GetString() == "ocio");
        Assert.Equal(40, ocio.GetProperty("limite").GetDecimal());
        Assert.True(ocio.GetProperty("arrastrar").GetBoolean());
        Assert.Matches(@"^\d{4}-W\d{2}$", lista.GetProperty("semana").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/gastos/presupuestos-semana/ocio")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/gastos/presupuestos-semana/ocio")).StatusCode);
    }

    [Fact]
    public async Task Rechaza_una_categoria_que_no_existe_y_un_limite_a_cero()
    {
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/gastos/presupuestos-semana/inventada", new { limite = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/gastos/presupuestos-semana/ocio", new { limite = 0 })).StatusCode);
    }

    [Fact]
    public async Task Lo_gastado_esta_semana_cuenta_y_lo_de_otra_semana_no()
    {
        var c = await Dentro();
        await c.PutAsJsonAsync("/api/gastos/presupuestos-semana/ropa", new { limite = 100 });
        var hoy = DateOnly.FromDateTime(DateTime.Now);
        // un gasto de hoy y otro de hace 20 días (otra semana)
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = hoy.ToString("yyyy-MM-dd"), importe = 30, categoria = "ropa", concepto = "Camiseta" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = hoy.AddDays(-20).ToString("yyyy-MM-dd"), importe = 55, categoria = "ropa", concepto = "Pantalón" })).StatusCode);
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/gastos/presupuestos-semana");
        var ropa = lista.GetProperty("presupuestos").EnumerateArray().Single(p => p.GetProperty("categoria").GetString() == "ropa");
        Assert.Equal(30, ropa.GetProperty("gastado").GetDecimal());
    }
}

public class PresupuestoSemanalAvisosTests
{
    private sealed class Avisos : IAvisos
    {
        public readonly List<string> Telegram = [];
        public Canales Disponibles() => throw new NotImplementedException();
        public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorTelegram(string texto, CancellationToken ct = default) { lock (Telegram) Telegram.Add(texto); return Task.FromResult(true); }
    }

    private static readonly DateOnly Jueves = new(2026, 11, 5);

    private static async Task<(AppDbContext Db, Avisos A)> Preparar(decimal limite)
    {
        var db = BaseDeDatos.Nueva();
        db.PresupuestosSemanales.Add(new PresupuestoSemanal { Categoria = "ocio", Limite = limite });
        await db.SaveChangesAsync();
        return (db, new Avisos());
    }

    private static async Task Gastar(AppDbContext db, decimal importe, DateOnly fecha)
    {
        db.Movimientos.Add(new Movimiento { Tipo = TipoMovimiento.Gasto, Categoria = "ocio", Concepto = "Cine", Importe = importe, Fecha = fecha });
        await db.SaveChangesAsync();
    }

    private static async Task Esperar(Avisos a, int n) { for (var i = 0; i < 100 && a.Telegram.Count < n; i++) await Task.Delay(20); }

    [Fact]
    public async Task Avisa_al_80_y_otra_vez_al_100_pero_no_repite_el_mismo_nivel()
    {
        var (db, a) = await Preparar(100);
        await Gastar(db, 85, Jueves);
        await PresupuestosSemana.Comprobar(db, a, "ocio", Jueves, Jueves, NullLogger.Instance, default);
        await PresupuestosSemana.Comprobar(db, a, "ocio", Jueves, Jueves, NullLogger.Instance, default);   // el barrido del vigilante vuelve a mirar
        await Esperar(a, 1);
        Assert.Single(a.Telegram);
        Assert.Contains("85 %", a.Telegram[0]);
        await Gastar(db, 20, Jueves);
        await PresupuestosSemana.Comprobar(db, a, "ocio", Jueves, Jueves, NullLogger.Instance, default);
        await Esperar(a, 2);
        Assert.Equal(2, a.Telegram.Count);
        Assert.Contains("pasado", a.Telegram[1]);
    }

    [Fact]
    public async Task La_semana_siguiente_vuelve_a_avisar()
    {
        var (db, a) = await Preparar(100);
        await Gastar(db, 120, Jueves);
        await PresupuestosSemana.Comprobar(db, a, "ocio", Jueves, Jueves, NullLogger.Instance, default);
        await Esperar(a, 1);
        var lunes = Jueves.AddDays(4);                    // 9 de noviembre: otra semana
        await Gastar(db, 120, lunes);
        await PresupuestosSemana.Comprobar(db, a, "ocio", lunes, lunes, NullLogger.Instance, default);
        await Esperar(a, 2);
        Assert.Equal(2, a.Telegram.Count);
    }

    [Fact]
    public async Task Un_gasto_de_otra_semana_no_avisa_de_esta()
    {
        var (db, a) = await Preparar(100);
        await Gastar(db, 500, Jueves.AddDays(-10));
        await PresupuestosSemana.Comprobar(db, a, "ocio", Jueves.AddDays(-10), Jueves, NullLogger.Instance, default);
        await Task.Delay(100);
        Assert.Empty(a.Telegram);
    }

    [Fact]
    public async Task Por_debajo_del_80_no_avisa()
    {
        var (db, a) = await Preparar(100);
        await Gastar(db, 79, Jueves);
        await PresupuestosSemana.Comprobar(db, a, "ocio", Jueves, Jueves, NullLogger.Instance, default);
        await Task.Delay(100);
        Assert.Empty(a.Telegram);
    }
}
