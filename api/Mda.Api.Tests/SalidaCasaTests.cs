using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Contexto;
using Mda.Api.Data;
using Mda.Api.Domotica;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class SalidaCasaLogicaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private static Aparato A(int id, string nombre, string tipo) => new() { Id = id, Nombre = nombre, Tipo = tipo };
    private static Entidad E(int aparato, string clave, string? valor, int haceMin = 1) => new() { AparatoId = aparato, Clave = clave, Valor = valor, Actualizado = Ahora.AddMinutes(-haceMin) };

    [Fact]
    public void Dice_que_puertas_estan_abiertas_y_que_aparatos_vigilados_siguen_encendidos()
    {
        var aparatos = new[] { A(1, "Ventana salón", "puerta"), A(2, "Puerta calle", "puerta"), A(3, "Plancha", "enchufe"), A(4, "Nevera", "enchufe"), A(5, "Luz cocina", "luz") };
        var entidades = new[] { E(1, "abierto", "true", 7), E(2, "abierto", "false"), E(3, "encendido", "true", 20), E(4, "encendido", "true"), E(5, "encendido", "false") };
        var (abiertos, encendidos) = SalidaCasa.Revisar(aparatos, entidades, new AjustesSalida(false, [3, 5]), Ahora);
        Assert.Equal(["Ventana salón"], abiertos.Select(x => x.Nombre));
        Assert.Equal(7, abiertos[0].HaceMinutos);
        Assert.Equal(["Plancha"], encendidos.Select(x => x.Nombre));        // la nevera está encendida pero no la vigilas; la luz está apagada
    }

    [Fact]
    public void Un_dato_viejo_o_vacio_no_cuenta_porque_el_sensor_puede_estar_caido()
    {
        var aparatos = new[] { A(1, "Ventana", "puerta"), A(3, "Plancha", "enchufe") };
        var entidades = new[] { E(1, "abierto", "true", 90), E(3, "encendido", null) };
        var (abiertos, encendidos) = SalidaCasa.Revisar(aparatos, entidades, new AjustesSalida(false, [3]), Ahora);
        Assert.Empty(abiertos); Assert.Empty(encendidos);
    }

    [Fact]
    public void Sin_nada_vigilado_solo_se_miran_las_puertas() =>
        Assert.Empty(SalidaCasa.Revisar([A(3, "Plancha", "enchufe")], [E(3, "encendido", "true")], new AjustesSalida(), Ahora).Encendidos);

    [Fact]
    public void La_frase_cuenta_lo_abierto_lo_encendido_lo_apagado_y_lo_que_fallo()
    {
        Assert.Equal("Hasta luego. Todo cerrado.", SalidaCasa.Frase(new RevisionSalida([], [], [], [])));
        var r = new RevisionSalida([new(1, "ventana del salón", 5)], [new(3, "plancha", 20)], [], []);
        Assert.Equal("Hasta luego. Ojo: ventana del salón abierta; plancha encendido.", SalidaCasa.Frase(r));
        var apagado = new RevisionSalida([], [], [new(3, "Plancha", 1)], ["Estufa"]);
        Assert.Equal("Hasta luego. Todo cerrado. He apagado: Plancha. No pude apagar: Estufa.", SalidaCasa.Frase(apagado));
    }

    [Fact]
    public void Los_ajustes_se_limpian_sin_repetidos_y_con_tope()
    {
        var a = SalidaCasa.Normalizar(new AjustesSalida(true, [1, 1, 2, .. Enumerable.Range(10, 100)]));
        Assert.True(a.Apagar);
        Assert.Equal(SalidaCasa.MaxVigilados, a.Vigilar!.Count);
        Assert.Equal(a.Vigilar.Count, a.Vigilar.Distinct().Count());
        Assert.Empty(SalidaCasa.Normalizar(null).Vigilar!);
    }
}

public class SalidaCasaApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static async Task<int> Crear(HttpClient c, string nombre, string tipo)
    {
        var r = await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre, tipo, driver = "demo" });
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Guarda_solo_aparatos_que_existen_y_se_encienden_y_la_prueba_dice_lo_que_hay()
    {
        var c = await factory.Dentro();
        var enchufe = await Crear(c, "Plancha salida", "enchufe");
        var sensor = await Crear(c, "Termo salida", "sensor");       // un sensor no se enciende ni se apaga: no vale
        await c.GetAsync("/api/domotica");
        var r = await c.PutAsJsonAsync("/api/domotica/salida", new { apagar = false, vigilar = new[] { enchufe, sensor, 99999 } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var a = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([enchufe], a.GetProperty("vigilar").EnumerateArray().Select(x => x.GetInt32()));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = db.Entidades.First(x => x.AparatoId == enchufe && x.Clave == "encendido");
            e.Valor = "true"; e.Actualizado = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        var prueba = await c.GetFromJsonAsync<JsonElement>("/api/domotica/salida/probar");
        Assert.Contains("Plancha salida encendido", prueba.GetProperty("frase").GetString());

        var todo = await c.GetFromJsonAsync<JsonElement>("/api/domotica/salida");
        Assert.Contains(todo.GetProperty("candidatos").EnumerateArray(), x => x.GetProperty("nombre").GetString() == "Plancha salida");
        Assert.DoesNotContain(todo.GetProperty("candidatos").EnumerateArray(), x => x.GetProperty("nombre").GetString() == "Termo salida");
    }

    [Fact]
    public async Task Al_salir_el_atajo_responde_con_la_comprobacion_apaga_lo_marcado_y_lo_deja_en_el_historial()
    {
        var c = await factory.Dentro();
        var enchufe = await Crear(c, "Estufa salida", "enchufe");
        await c.GetAsync("/api/domotica");
        await c.PutAsJsonAsync("/api/domotica/salida", new { apagar = true, vigilar = new[] { enchufe } });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = db.Entidades.First(x => x.AparatoId == enchufe && x.Clave == "encendido");
            e.Valor = "true"; e.Actualizado = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        string mensaje;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            mensaje = await SalidaCasa.Ejecutar(db, scope.ServiceProvider.GetRequiredService<ServicioCasa>(), scope.ServiceProvider.GetRequiredService<Mda.Api.Telegram.Bot>(), DateTimeOffset.UtcNow, CancellationToken.None);
        }
        Assert.Contains("He apagado: Estufa salida", mensaje);
        var todo = await c.GetFromJsonAsync<JsonElement>("/api/domotica/salida");
        var h = todo.GetProperty("historial")[0];
        Assert.Contains("Estufa salida", h.GetProperty("apagados").EnumerateArray().Select(x => x.GetString()));
        // y el aparato quedó apagado de verdad
        var casa = await c.GetFromJsonAsync<JsonElement>("/api/domotica");
        var ap = casa.GetProperty("aparatos").EnumerateArray().First(x => x.GetProperty("id").GetInt32() == enchufe);
        Assert.Equal("false", ap.GetProperty("entidades").EnumerateArray().First(x => x.GetProperty("clave").GetString() == "encendido").GetProperty("valor").GetString());
    }
}
