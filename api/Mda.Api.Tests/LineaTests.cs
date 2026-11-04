using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Gastos;
using Mda.Api.Luz;
using Mda.Api.Pc;
using Mda.Api.Seguridad;

namespace Mda.Api.Tests;

public class LineaTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Junta_todo_lo_del_dia_por_orden_de_hora_mas_reciente_primero()
    {
        var l = LineaVida.Construir(
            [new Movimiento { Tipo = TipoMovimiento.Gasto, Concepto = "Mercadona", Importe = 40.5m, Categoria = "supermercado", Creado = T.AddHours(1) }],
            [new SalidaFoto { Fecha = new DateOnly(2026, 11, 3), Tipo = "noche", Resultado = 4, Prevision = 7, Nota = "Vía Láctea" }],
            [new OrdenPc { Tipo = "energia", Parametro = "ahorro", Estado = "hecha", Creada = T.AddHours(3) }],
            [new Acceso { Fecha = T, Exito = false, Ip = "1.2.3.4", Motivo = "contraseña" }], TimeZoneInfo.Utc);
        Assert.Equal(4, l.Count);
        Assert.Equal(["pc", "foto", "dinero", "alerta"], l.Select(x => x.Tipo));
        Assert.Contains("−40,50 €", l.Single(x => x.Tipo == "dinero").Detalle);
        Assert.Contains("prevista 7/10", l.Single(x => x.Tipo == "foto").Detalle);
    }

    [Fact]
    public void Un_dia_sin_nada_es_una_lista_vacia() => Assert.Empty(LineaVida.Construir([], [], [], [], TimeZoneInfo.Utc));
}

public class LineaApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Pide_sesion_valida_la_fecha_y_devuelve_los_sucesos_del_dia()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/inicio/linea")).StatusCode);
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/inicio/linea?fecha=2999-01-01")).StatusCode);
        var j = await c.GetFromJsonAsync<JsonElement>("/api/inicio/linea");
        Assert.True(j.GetProperty("sucesos").GetArrayLength() >= 1);     // al menos la entrada de esta sesión
    }
}
