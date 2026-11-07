using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Domotica;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class EvaluadorReglasTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static ReglaCasa Aire() => new() { Operador = ">", Umbral = 26, Minutos = 5, Revertir = true, Histeresis = 2, EsperaMinutos = 15 };

    [Fact]
    public void Solo_dispara_cuando_la_condicion_se_mantiene_el_tiempo_pedido()
    {
        var r = Aire();
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 27, T0));
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 27, T0.AddMinutes(4)));
        Assert.Equal(PasoRegla.Disparar, EvaluadorReglas.Evaluar(r, 27.5, T0.AddMinutes(5)));
        Assert.True(r.Disparada);
    }

    [Fact]
    public void Si_la_lectura_baja_antes_de_tiempo_se_empieza_de_cero()
    {
        var r = Aire();
        EvaluadorReglas.Evaluar(r, 27, T0);
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 25, T0.AddMinutes(3)));
        Assert.Null(r.Desde);
        EvaluadorReglas.Evaluar(r, 27, T0.AddMinutes(4));
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 27, T0.AddMinutes(8)));   // solo han pasado 4 min desde que volvió a subir
        Assert.Equal(PasoRegla.Disparar, EvaluadorReglas.Evaluar(r, 27, T0.AddMinutes(9)));
    }

    [Fact]
    public void Revierte_con_histeresis_y_respetando_la_espera_entre_acciones()
    {
        var r = Aire();
        EvaluadorReglas.Evaluar(r, 27, T0);
        EvaluadorReglas.Evaluar(r, 27, T0.AddMinutes(5));                                          // dispara a las 12:05
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 25, T0.AddMinutes(10)));           // 25 aún no es menor que 26 − 2
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 23.5, T0.AddMinutes(12)));         // ya baja de 24, pero solo han pasado 7 min
        Assert.Equal(PasoRegla.Revertir, EvaluadorReglas.Evaluar(r, 23.5, T0.AddMinutes(21)));     // pasados los 15 min
        Assert.False(r.Disparada);
    }

    [Fact]
    public void Sin_revertir_la_regla_se_rearma_al_salir_de_la_zona_sin_hacer_nada()
    {
        var r = Aire(); r.Revertir = false; r.Minutos = 0; r.EsperaMinutos = 1;
        Assert.Equal(PasoRegla.Disparar, EvaluadorReglas.Evaluar(r, 27, T0));
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 27, T0.AddMinutes(1)));
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 25, T0.AddMinutes(2)));
        Assert.False(r.Disparada);
        Assert.Equal(PasoRegla.Disparar, EvaluadorReglas.Evaluar(r, 27, T0.AddMinutes(3)));
    }

    [Fact]
    public void El_operador_menor_que_funciona_al_reves()
    {
        var r = new ReglaCasa { Operador = "<", Umbral = 18, Minutos = 0, Revertir = true, Histeresis = 2, EsperaMinutos = 1 };
        Assert.Equal(PasoRegla.Disparar, EvaluadorReglas.Evaluar(r, 17, T0));
        Assert.Equal(PasoRegla.Nada, EvaluadorReglas.Evaluar(r, 19, T0.AddMinutes(2)));
        Assert.Equal(PasoRegla.Revertir, EvaluadorReglas.Evaluar(r, 20.5, T0.AddMinutes(3)));
    }
}

public class ReglasCasaTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static async Task<int> Crear(HttpClient c, string nombre, string tipo)
    {
        var r = await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre, tipo, driver = "demo" });
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Una_regla_enciende_el_aparato_cuando_el_sensor_cumple_y_lo_deja_en_el_registro()
    {
        var c = await factory.Dentro();
        var sensor = await Crear(c, "Termómetro regla", "sensor");
        var aire = await Crear(c, "Aire regla", "clima");
        await c.GetAsync("/api/domotica");   // el sensor lee su primer valor
        var alta = await c.PostAsJsonAsync("/api/domotica/reglas", new
        {
            nombre = "Aire si hace calor", sensorId = sensor, sensorClave = "temperatura", operador = ">", umbral = -50, minutos = 0, destinoId = aire,
            accion = "encender", objetivo = 24, modo = "frio", revertir = false, histeresis = 1, esperaMinutos = 1, soloAvisar = false,
        });
        Assert.Equal(HttpStatusCode.OK, alta.StatusCode);

        using (var scope = factory.Services.CreateScope())
            await factory.Services.GetRequiredService<MotorReglas>().Vuelta(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);

        var casa = await c.GetFromJsonAsync<JsonElement>("/api/domotica");
        var a = casa.GetProperty("aparatos").EnumerateArray().First(x => x.GetProperty("id").GetInt32() == aire);
        string? V(string clave) => a.GetProperty("entidades").EnumerateArray().First(e => e.GetProperty("clave").GetString() == clave).GetProperty("valor").GetString();
        Assert.Equal("true", V("encendido"));
        Assert.Equal("24", V("objetivo"));
        Assert.Equal("frio", V("modo"));
        var registro = await c.GetFromJsonAsync<JsonElement>("/api/domotica/reglas/registro");
        Assert.Contains(registro.EnumerateArray(), x => x.GetProperty("texto").GetString()!.Contains("Aire regla"));
    }

    [Fact]
    public async Task Valida_los_campos_y_las_plantillas_existen()
    {
        var c = await factory.Dentro();
        var sensor = await Crear(c, "Termo 2", "sensor");
        var luz = await Crear(c, "Luz 2", "luz");
        var mala = await c.PostAsJsonAsync("/api/domotica/reglas", new { nombre = "", sensorId = 999, sensorClave = "temperatura", operador = "=", umbral = 1, minutos = 5000, destinoId = luz, accion = "volar", revertir = false, histeresis = 1, esperaMinutos = 0, soloAvisar = false });
        Assert.Equal(HttpStatusCode.BadRequest, mala.StatusCode);
        // un dato que no es número ni «abierto»/«encendido» no vale de sensor
        var altavoz = await Crear(c, "Altavoz 2", "altavoz");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/domotica/reglas", new { nombre = "x", sensorId = altavoz, sensorClave = "reproduciendo", operador = ">", umbral = 1, minutos = 0, destinoId = luz, accion = "encender", revertir = false, histeresis = 1, esperaMinutos = 5, soloAvisar = false })).StatusCode);
        var plantillas = await c.GetFromJsonAsync<JsonElement>("/api/domotica/reglas/plantillas");
        Assert.True(plantillas.GetArrayLength() >= 3);
        // solo avisar no necesita aparato de destino
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/domotica/reglas", new { nombre = "Avisa", sensorId = sensor, sensorClave = "temperatura", operador = ">", umbral = 40, minutos = 0, destinoId = 0, accion = "encender", revertir = false, histeresis = 1, esperaMinutos = 5, soloAvisar = true })).StatusCode);
    }
}
