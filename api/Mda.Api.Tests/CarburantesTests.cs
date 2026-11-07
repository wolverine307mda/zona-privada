using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Mda.Api.Tiempo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class CarburantesUnitTests
{
    // un trozo del JSON real del Ministerio: comas decimales, «Dirección», «Longitud (WGS84)», precios vacíos y una gasolinera de socios (Tipo Venta R)
    private const string Ejemplo = """
    {"Fecha":"07/11/2026 10:05:11","ListaEESSPrecio":[
      {"C.P.":"28001","Dirección":"CALLE MAYOR 1","Latitud":"40,416800","Localidad":"MADRID","Longitud (WGS84)":"-3,703800","Precio Gasoleo A":"1,379","Precio Gasoleo Premium":"","Precio Gasolina 95 E5":"1,459","Precio Gasolina 98 E5":"1,569","Provincia":"MADRID","Rótulo":"REPSOL","Tipo Venta":"P"},
      {"C.P.":"28002","Dirección":"AVENIDA DE AMERICA 10","Latitud":"40,440000","Localidad":"MADRID","Longitud (WGS84)":"-3,670000","Precio Gasoleo A":"1,359","Precio Gasolina 95 E5":"1,419","Provincia":"MADRID","Rótulo":"BALLENOIL","Tipo Venta":"P"},
      {"C.P.":"28003","Dirección":"POLIGONO 3","Latitud":"40,430000","Localidad":"MADRID","Longitud (WGS84)":"-3,690000","Precio Gasolina 95 E5":"1,100","Provincia":"MADRID","Rótulo":"COOPERATIVA","Tipo Venta":"R"},
      {"C.P.":"28004","Dirección":"SIN PRECIOS","Latitud":"40,420000","Localidad":"MADRID","Longitud (WGS84)":"-3,700000","Precio Gasolina 95 E5":"","Provincia":"MADRID","Rótulo":"CERRADA","Tipo Venta":"P"},
      {"C.P.":"08001","Dirección":"RAMBLA 5","Latitud":"41,380000","Localidad":"BARCELONA","Longitud (WGS84)":"2,170000","Precio Gasolina 95 E5":"1,489","Provincia":"BARCELONA","Rótulo":"SHELL","Tipo Venta":"P"},
      {"C.P.":"00000","Dirección":"COORDENADAS RARAS","Latitud":"0,000000","Localidad":"NADA","Longitud (WGS84)":"0,000000","Precio Gasolina 95 E5":"1,300","Provincia":"NADA","Rótulo":"X","Tipo Venta":"P"}
    ],"ResultadoConsulta":"OK"}
    """;

    [Fact]
    public void Lee_el_formato_del_ministerio_con_comas_y_descarta_lo_que_no_vale()
    {
        var l = Carburantes.Parsear(Ejemplo);
        Assert.Equal(3, l.Count);                                       // sin la de socios, sin la que no tiene precios y sin la de coordenadas 0,0
        var repsol = l.First(e => e.Rotulo == "REPSOL");
        Assert.Equal(1.459m, repsol.Precios["gasolina95"]);
        Assert.Equal(1.379m, repsol.Precios["diesel"]);
        Assert.False(repsol.Precios.ContainsKey("dieselplus"));          // venía vacío
        Assert.Equal(40.4168, repsol.Lat);
        Assert.Equal("CALLE MAYOR 1", repsol.Direccion);
    }

    [Fact]
    public void Aguanta_una_respuesta_con_marca_de_orden_de_bytes_y_una_lista_vacia_o_rara()
    {
        Assert.Equal(3, Carburantes.Parsear("﻿" + Ejemplo).Count);
        Assert.Empty(Carburantes.Parsear("{}"));
        Assert.Empty(Carburantes.Parsear("""{"ListaEESSPrecio":[]}"""));
        Assert.ThrowsAny<JsonException>(() => Carburantes.Parsear("no es json"));
    }

    [Fact]
    public void La_distancia_en_linea_recta_es_razonable()
    {
        Assert.Equal(0, Carburantes.Distancia(40.4, -3.7, 40.4, -3.7), 3);
        Assert.InRange(Carburantes.Distancia(40.4168, -3.7038, 41.3851, 2.1734), 500, 510);    // Madrid–Barcelona, unos 505 km
        Assert.InRange(Carburantes.Distancia(40.4168, -3.7038, 40.44, -3.67), 3, 5);
    }

    [Fact]
    public void Las_mas_baratas_son_las_del_radio_ordenadas_por_precio_y_despues_por_cercania()
    {
        var l = Carburantes.Parsear(Ejemplo);
        var c = Carburantes.Cercanas(l, 40.4168, -3.7038, 10, "gasolina95");
        Assert.Equal(["BALLENOIL", "REPSOL"], c.Select(x => x.Rotulo));
        Assert.Equal(1.419m, c[0].Precio);
        Assert.Single(Carburantes.Cercanas(l, 40.4168, -3.7038, 1, "gasolina95"));              // con 1 km solo cae la de la Plaza Mayor
        Assert.Empty(Carburantes.Cercanas(l, 40.4168, -3.7038, 10, "gasolina98").Where(x => x.Rotulo == "BALLENOIL"));
        Assert.Equal("MADRID", Carburantes.ProvinciaDe(l, 40.42, -3.70));
        Assert.Equal("BARCELONA", Carburantes.ProvinciaDe(l, 41.4, 2.2));
    }

    [Fact]
    public void La_media_de_la_provincia_es_la_mediana_y_no_se_deja_engañar_por_un_precio_raro()
    {
        Estacion E(decimal p) => new("X", "", "", "MADRID", 40, -3, new Dictionary<string, decimal> { ["diesel"] = p });
        Assert.Equal(1.40m, Carburantes.MediaProvincia([E(1.38m), E(1.40m), E(1.45m)], "MADRID", "diesel"));
        Assert.Equal(1.40m, Carburantes.MediaProvincia([E(1.38m), E(1.40m), E(1.42m), E(3.9m)], "MADRID", "diesel") - 0.01m);   // la mediana de 4 es 1,41; el 3,9 casi no pesa
        Assert.Null(Carburantes.MediaProvincia([E(1.4m)], "OTRA", "diesel"));
    }

    [Fact]
    public void Sin_gasolineras_en_el_radio_no_hay_veredicto()
    {
        Assert.Equal("sin-datos", Carburantes.Valorar(null, 1.45m, 1.50m, 1.46m).Nivel);
    }

    [Fact]
    public void Barata_frente_a_la_media_y_a_lo_que_pagaste_y_sin_subida_es_buen_momento()
    {
        var v = Carburantes.Valorar(1.40m, 1.46m, 1.48m, 1.40m);
        Assert.Equal("bueno", v.Nivel);
        Assert.Equal(3, v.Razones.Count);
        Assert.Contains("más barata que la media", v.Razones[0]);
        Assert.Contains("más barata que en tu último repostaje", v.Razones[1]);
        Assert.Contains("estable", v.Razones[2]);
    }

    [Fact]
    public void Si_esta_subiendo_conviene_repostar_pronto_y_si_baja_se_puede_esperar()
    {
        Assert.Equal("sube", Carburantes.Valorar(1.45m, 1.46m, 1.47m, 1.40m).Nivel);
        Assert.Equal("baja", Carburantes.Valorar(1.45m, 1.45m, 1.45m, 1.52m).Nivel);
    }

    [Fact]
    public void Mas_cara_que_la_media_y_que_tu_ultimo_repostaje_es_cara_y_sin_referencias_es_normal()
    {
        Assert.Equal("caro", Carburantes.Valorar(1.55m, 1.50m, 1.48m, null).Nivel);
        Assert.Equal("normal", Carburantes.Valorar(1.50m, null, null, null).Nivel);
        Assert.Equal("normal", Carburantes.Valorar(1.46m, 1.46m, 1.47m, 1.46m).Nivel);
    }

    [Fact]
    public void El_ahorro_del_deposito_es_la_diferencia_por_tus_litros_y_nunca_negativo()
    {
        Assert.Equal(2.4m, Carburantes.Ahorro(40, 1.46m, 1.40m));
        Assert.Equal(0, Carburantes.Ahorro(40, 1.40m, 1.46m));
        Assert.Equal(0, Carburantes.Ahorro(40, null, 1.40m));
    }

    [Fact]
    public void Los_litros_tipicos_son_la_mediana_de_tus_repostajes_y_sin_ellos_40()
    {
        Movimiento R(decimal l) => new() { Litros = l };
        Assert.Equal(40, Carburantes.LitrosTipicos([]));
        Assert.Equal(38, Carburantes.LitrosTipicos([R(30), R(38), R(50)]));
        Assert.Equal(40, Carburantes.LitrosTipicos([R(2), R(3)]));        // menos de 5 litros no es un repostaje
    }

    [Fact]
    public void Los_ajustes_se_normalizan()
    {
        var a = Carburantes.Normalizar(new AjustesCombustible("raro", 999, new PuntoCombustible("Casa", 95, 0), true));
        Assert.Equal("gasolina95", a.Combustible);
        Assert.Equal(Carburantes.RadioMax, a.Radio);
        Assert.Null(a.Punto);                                              // una latitud imposible se descarta
        Assert.Equal(Carburantes.RadioMin, Carburantes.Normalizar(new AjustesCombustible("diesel", -5)).Radio);
    }
}

// quien da las gasolineras, de mentira
public class CarburantesFalso : ICarburantes
{
    public List<Estacion>? Lista;
    public bool Antiguo;
    public Task<(List<Estacion> Lista, DateTimeOffset Hora, bool Antiguo)?> Obtener(CancellationToken ct) =>
        Task.FromResult<(List<Estacion>, DateTimeOffset, bool)?>(Lista is null ? null : (Lista, DateTimeOffset.UtcNow, Antiguo));
}

public class CarburantesFactory : ModulosFactory
{
    public CarburantesFalso Falso { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<ICarburantes>(Falso)));
    }
}

public class CarburantesApiTests(CarburantesFactory factory) : IClassFixture<CarburantesFactory>
{
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= factory.Dentro();

    private static Estacion E(string rotulo, double lat, double lon, decimal precio) =>
        new(rotulo, "CALLE", "MADRID", "MADRID", lat, lon, new Dictionary<string, decimal> { ["gasolina95"] = precio });

    [Fact]
    public async Task Sin_punto_ni_lugar_del_tiempo_pide_un_punto_y_con_uno_propio_lista_las_mas_baratas()
    {
        var c = await Dentro();

        factory.Falso.Lista = [E("A", 40.417, -3.704, 1.50m), E("B", 40.42, -3.70, 1.42m), E("C", 40.43, -3.69, 1.46m), E("LEJOS", 41.38, 2.17, 1.10m)];
        var ok = await c.PutAsJsonAsync("/api/gastos/combustible", new { combustible = "gasolina95", radio = 10, punto = new { nombre = "Casa", lat = 40.4168, lon = -3.7038 }, aviso = false });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var r = await c.GetFromJsonAsync<JsonElement>("/api/gastos/combustible");
        Assert.Equal("propio", r.GetProperty("origen").GetString());
        var est = r.GetProperty("estaciones").EnumerateArray().Select(x => x.GetProperty("rotulo").GetString()).ToList();
        Assert.Equal(["B", "C", "A"], est);                                // de más barata a más cara y sin la de Barcelona
        Assert.False(r.GetProperty("antiguo").GetBoolean());
        Assert.Equal("Gasolina 95", r.GetProperty("nombreCombustible").GetString());
        Assert.NotEqual(JsonValueKind.Null, r.GetProperty("valoracion").ValueKind);
        Assert.Equal(40, r.GetProperty("litrosTipicos").GetDecimal());     // sin repostajes apuntados
    }

    [Fact]
    public async Task Si_el_servicio_no_responde_lo_dice_en_vez_de_fallar()
    {
        var c = await Dentro();
        await c.PutAsJsonAsync("/api/gastos/combustible", new { combustible = "diesel", radio = 5, punto = new { nombre = "Casa", lat = 40.4168, lon = -3.7038 } });
        factory.Falso.Lista = null;
        var r = await c.GetFromJsonAsync<JsonElement>("/api/gastos/combustible");
        Assert.True(r.GetProperty("sinDatos").GetBoolean());
        factory.Falso.Lista = [E("A", 40.417, -3.704, 1.50m)];
    }

    [Fact]
    public async Task El_radio_y_el_combustible_se_corrigen_si_vienen_mal()
    {
        var c = await Dentro();
        var a = await (await c.PutAsJsonAsync("/api/gastos/combustible", new { combustible = "agua", radio = 500 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("gasolina95", a.GetProperty("combustible").GetString());
        Assert.Equal(Carburantes.RadioMax, a.GetProperty("radio").GetInt32());
    }
}

public class CarburantesSinPuntoTests(CarburantesFactory factory) : IClassFixture<CarburantesFactory>
{
    [Fact]
    public async Task Sin_punto_ni_lugar_del_tiempo_pide_un_punto()
    {
        var c = await factory.Dentro();
        var sin = await c.GetFromJsonAsync<JsonElement>("/api/gastos/combustible");
        Assert.True(sin.GetProperty("sinPunto").GetBoolean());
    }
}

public class CarburantesAvisosTests
{
    private sealed class AvisosFalsos : IAvisos
    {
        public readonly List<string> Telegram = [];
        public Canales Disponibles() => throw new NotImplementedException();
        public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorTelegram(string texto, CancellationToken ct = default) { lock (Telegram) Telegram.Add(texto); return Task.FromResult(true); }
    }

    private static readonly DateOnly Hoy = new(2026, 11, 7);

    private static Task<(AppDbContext Db, CarburantesFalso Datos)> Preparar(decimal precioHoy, bool aviso)
    {
        var db = BaseDeDatos.Nueva();
        db.Ajustes.Add(new Mda.Api.Seguridad.Ajuste { Clave = Carburantes.Clave, Valor = System.Text.Json.JsonSerializer.Serialize(new AjustesCombustible("gasolina95", 10, new PuntoCombustible("Casa", 40.4168, -3.7038), aviso), new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        for (var i = 1; i <= 7; i++) db.PreciosCarburante.Add(new PrecioCarburante { Fecha = Hoy.AddDays(-i), Combustible = "gasolina95", Media = 1.46m, MinimoCerca = 1.50m });
        var datos = new CarburantesFalso { Lista = [new Estacion("REPSOL", "CALLE", "MADRID", "MADRID", 40.42, -3.70, new Dictionary<string, decimal> { ["gasolina95"] = precioHoy })] };
        return db.SaveChangesAsync().ContinueWith(_ => (db, datos));
    }

    private static ServicioTiempo Tiempo(AppDbContext db) => null!;   // con punto propio no se consulta el lugar del tiempo

    private static async Task Esperar(AvisosFalsos a, int n) { for (var i = 0; i < 100 && a.Telegram.Count < n; i++) await Task.Delay(20); }

    [Fact]
    public async Task Una_bajada_clara_de_la_ultima_semana_avisa_una_vez_al_dia_si_lo_pediste()
    {
        var (db, datos) = await Preparar(1.44m, aviso: true);                // 6 céntimos menos que la media de la semana (1,50)
        var a = new AvisosFalsos();
        await CarburantesApi.Revisar(db, datos, Tiempo(db), a, Hoy, NullLogger.Instance, default);
        await CarburantesApi.Revisar(db, datos, Tiempo(db), a, Hoy, NullLogger.Instance, default);
        await Esperar(a, 1); await Task.Delay(100);
        Assert.Single(a.Telegram);
        Assert.Contains("1,440", a.Telegram[0]);
        Assert.Contains("REPSOL", a.Telegram[0]);
        Assert.NotNull(await db.PreciosCarburante.FindAsync(Hoy, "gasolina95"));        // y queda la foto de hoy
    }

    [Fact]
    public async Task Sin_pedirlo_o_con_una_bajada_pequena_no_avisa_pero_guarda_la_foto()
    {
        var (db, datos) = await Preparar(1.44m, aviso: false);
        var a = new AvisosFalsos();
        await CarburantesApi.Revisar(db, datos, Tiempo(db), a, Hoy, NullLogger.Instance, default);
        var (db2, datos2) = await Preparar(1.48m, aviso: true);              // solo 2 céntimos menos
        await CarburantesApi.Revisar(db2, datos2, Tiempo(db2), a, Hoy, NullLogger.Instance, default);
        await Task.Delay(100);
        Assert.Empty(a.Telegram);
        Assert.NotNull(await db.PreciosCarburante.FindAsync(Hoy, "gasolina95"));
    }

    [Fact]
    public async Task Con_datos_antiguos_no_avisa()
    {
        var (db, datos) = await Preparar(1.40m, aviso: true);
        datos.Antiguo = true;
        var a = new AvisosFalsos();
        await CarburantesApi.Revisar(db, datos, Tiempo(db), a, Hoy, NullLogger.Instance, default);
        await Task.Delay(100);
        Assert.Empty(a.Telegram);
    }
}
