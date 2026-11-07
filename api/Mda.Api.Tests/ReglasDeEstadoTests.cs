using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Domotica;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class ReglasDeEstadoTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("true", 1.0)]
    [InlineData("false", 0.0)]
    [InlineData("21,5", null)]
    [InlineData("21.5", 21.5)]
    [InlineData(null, null)]
    [InlineData("abierto", null)]
    public void Un_estado_se_convierte_en_uno_o_cero_y_un_numero_sigue_siendo_numero(string? valor, double? esperado) =>
        Assert.Equal(esperado, EvaluadorReglas.Numero(valor));

    [Fact]
    public void Solo_abierto_y_encendido_son_estados_vigilables()
    {
        Assert.True(EvaluadorReglas.EsEstado("puerta", "abierto"));
        Assert.True(EvaluadorReglas.EsEstado("enchufe", "encendido"));
        Assert.False(EvaluadorReglas.EsEstado("altavoz", "reproduciendo"));
        Assert.False(EvaluadorReglas.EsEstado("sensor", "temperatura"));
    }

    private static ReglaCasa Puerta(int minutos = 10) => new() { Operador = ">", Umbral = 0.5, Minutos = minutos, EsperaMinutos = 30, SoloAvisar = true, SensorClave = "abierto" };

    [Fact]
    public void Una_puerta_que_se_abre_y_se_cierra_antes_de_tiempo_no_avisa()
    {
        var r = Puerta();
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 1, T0));
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 1, T0.AddMinutes(9)));
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 0, T0.AddMinutes(9.5)));      // se cerró justo antes
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 1, T0.AddMinutes(10)));       // y al abrirse de nuevo empieza de cero
        Assert.False(r.Disparada);
    }

    [Fact]
    public void Una_puerta_que_se_queda_abierta_avisa_una_vez_por_episodio_y_se_rearma_al_cerrar()
    {
        var r = Puerta();
        EvaluadorReglas.Evaluar(r, 1, T0);
        Assert.Equal(PasoRegla.Disparar, EvaluadorReglas.Evaluar(r, 1, T0.AddMinutes(10)));
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 1, T0.AddMinutes(40)));       // sigue abierta: no repite
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 0, T0.AddMinutes(45)));       // se cierra: se rearma sin avisar
        Assert.False(r.Disparada);
        EvaluadorReglas.Evaluar(r, 1, T0.AddMinutes(100));
        Assert.Equal(PasoRegla.Disparar, EvaluadorReglas.Evaluar(r, 1, T0.AddMinutes(110)));  // otro episodio, otro aviso
    }

    [Fact]
    public void El_resumen_de_una_regla_de_estado_habla_de_abierta_o_encendido() =>
        Assert.Equal("Puerta de la calle: abierta más de 10 min → avisar", EvaluadorReglas.Resumen(Puerta(), "Puerta de la calle", "", estado: true));

    [Fact]
    public async Task Se_crea_una_regla_de_estado_con_el_operador_y_el_umbral_puestos_por_el_sistema_y_las_plantillas_estan()
    {
        var c = await factory.Dentro();
        var r0 = await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre = "Puerta calle", tipo = "puerta", driver = "demo" });
        var puerta = (await r0.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        // el cliente manda un operador y un umbral cualesquiera: en estados no cuentan
        var alta = await c.PostAsJsonAsync("/api/domotica/reglas", new { nombre = "Puerta abierta", sensorId = puerta, sensorClave = "abierto", operador = "=", umbral = 99, minutos = 10, destinoId = 0, accion = "encender", revertir = false, histeresis = 0, esperaMinutos = 30, soloAvisar = true });
        Assert.Equal(HttpStatusCode.OK, alta.StatusCode);
        var l = await c.GetFromJsonAsync<JsonElement>("/api/domotica/reglas");
        var regla = l.EnumerateArray().First(x => x.GetProperty("nombre").GetString() == "Puerta abierta");
        Assert.True(regla.GetProperty("estado").GetBoolean());
        Assert.Equal(">", regla.GetProperty("operador").GetString());
        Assert.Equal(0.5, regla.GetProperty("umbral").GetDouble());
        Assert.Contains("abierta más de 10 min", regla.GetProperty("resumen").GetString());

        var plantillas = await c.GetFromJsonAsync<JsonElement>("/api/domotica/reglas/plantillas");
        var ids = plantillas.EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains("puerta-abierta", ids); Assert.Contains("enchufe-encendido", ids); Assert.Contains("luz-encendida", ids);
    }

    [Fact]
    public async Task El_motor_avisa_de_la_puerta_abierta_y_lo_apunta_en_el_registro()
    {
        var c = await factory.Dentro();
        var r0 = await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre = "Puerta motor", tipo = "puerta", driver = "demo" });
        var puerta = (await r0.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await c.GetAsync("/api/domotica");   // la puerta crea sus datos en la primera lectura
        await c.PostAsJsonAsync("/api/domotica/reglas", new { nombre = "Puerta motor abierta", sensorId = puerta, sensorClave = "abierto", operador = ">", umbral = 0.5, minutos = 0, destinoId = 0, accion = "encender", revertir = false, histeresis = 0, esperaMinutos = 1, soloAvisar = true });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = db.Entidades.First(x => x.AparatoId == puerta && x.Clave == "abierto");
            e.Valor = "true"; e.Actualizado = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await factory.Services.GetRequiredService<MotorReglas>().Vuelta(db, CancellationToken.None);
        }
        var registro = await c.GetFromJsonAsync<JsonElement>("/api/domotica/reglas/registro");
        Assert.Contains(registro.EnumerateArray(), x => x.GetProperty("texto").GetString()!.Contains("Puerta motor lleva abierta"));
    }
}
