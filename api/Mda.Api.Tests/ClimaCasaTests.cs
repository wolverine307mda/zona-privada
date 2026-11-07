using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Domotica;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class ClimaCasaLogicaTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
    private static DateTimeOffset U(int d, int h, int m = 0) => new(2026, 11, d, h, m, 0, TimeSpan.Zero);

    [Fact]
    public void Agrega_por_dia_local_con_minimo_media_y_maximo()
    {
        var dias = ClimaCasa.Agregar([new(U(3, 9), 18), new(U(3, 12), 22), new(U(3, 15), 20), new(U(4, 9), 25)], Madrid);
        Assert.Equal(2, dias.Count);
        Assert.Equal(new DiaResumen(new DateOnly(2026, 11, 3), 18, 20, 22, 3), dias[0]);
        Assert.Equal(1, dias[1].Muestras);
        Assert.Equal(25, dias[1].Min); Assert.Equal(25, dias[1].Max);          // un solo dato: mín, media y máx coinciden
    }

    [Fact]
    public void La_medianoche_local_manda_y_no_la_utc()
    {
        // 23:30 UTC del día 3 ya es el día 4 en Madrid (invierno: UTC+1)
        var dias = ClimaCasa.Agregar([new(U(3, 22, 30), 20), new(U(3, 23, 30), 21)], Madrid);
        Assert.Equal([new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 4)], dias.Select(d => d.Dia));
    }

    [Fact]
    public void El_dia_del_cambio_de_hora_no_pierde_ni_duplica_lecturas()
    {
        // el 25 de octubre de 2026 se retrasa el reloj: ese día tiene 25 horas y sigue siendo UN día
        var lecturas = Enumerable.Range(0, 25).Select(h => new Muestra(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero).AddHours(h), 20 + h % 3)).ToList();
        var dias = ClimaCasa.Agregar(lecturas, Madrid);
        Assert.Single(dias, d => d.Dia == new DateOnly(2026, 10, 25));
        Assert.Equal(25, dias.Single(d => d.Dia == new DateOnly(2026, 10, 25)).Muestras);
    }

    [Fact]
    public void Sin_datos_no_hay_serie_ni_extremos_y_un_dato_sirve_de_todo()
    {
        var vacia = ClimaCasa.Serie([], TimeSpan.FromHours(1), null);
        Assert.Empty(vacia.Puntos); Assert.Null(vacia.Min); Assert.Null(vacia.MasAlto);
        var una = ClimaCasa.Serie([new(U(3, 9), 21.37)], TimeSpan.FromHours(1), 21.4);
        Assert.Single(una.Puntos);
        Assert.Equal(21.4, una.Min); Assert.Equal(21.4, una.Max); Assert.Equal(21.4, una.Actual);
    }

    [Fact]
    public void Los_extremos_dicen_cuando_fue_lo_mas_frio_y_lo_mas_calido()
    {
        var s = ClimaCasa.Serie([new(U(3, 6), 16.5), new(U(3, 12), 21), new(U(3, 16), 24.2)], TimeSpan.FromHours(1), null);
        Assert.Equal(U(3, 6), s.MasBajo!.T); Assert.Equal(16.5, s.MasBajo.V);
        Assert.Equal(U(3, 16), s.MasAlto!.T); Assert.Equal(24.2, s.MasAlto.V);
        Assert.Equal(20.6, s.Media);
    }

    [Fact]
    public void Los_cubos_promedian_las_lecturas_de_varios_sensores_en_el_mismo_tramo()
    {
        var c = ClimaCasa.Cubos([new(U(3, 10, 5), 20), new(U(3, 10, 40), 22), new(U(3, 11, 10), 30)], TimeSpan.FromHours(1));
        Assert.Equal(2, c.Count);
        Assert.Equal(U(3, 10), c[0].T); Assert.Equal(21, c[0].V);
        Assert.Equal(U(3, 11), c[1].T);
    }

    [Theory]
    [InlineData(1, 15)] [InlineData(2, 15)] [InlineData(7, 60)] [InlineData(8, 60)] [InlineData(30, 240)]
    public void El_tramo_depende_de_lo_que_se_mira(int dias, int minutos) => Assert.Equal(TimeSpan.FromMinutes(minutos), ClimaCasa.CuboPara(dias));

    [Fact]
    public void El_csv_usa_punto_y_coma_comillas_y_la_hora_local()
    {
        var csv = ClimaCasa.Csv([("Salón; grande", "Termo \"1\"", "temperatura", U(3, 11, 30), 21.5)], Madrid);
        Assert.Equal("estancia;sensor;dato;fecha;valor\n\"Salón; grande\";\"Termo \"\"1\"\"\";temperatura;2026-11-03 12:30;21,5\n", csv);
    }
}

public class ClimaCasaApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static async Task<int> Crear(HttpClient c, string nombre, string tipo, int? estancia = null)
    {
        var r = await c.PostAsJsonAsync("/api/domotica/aparatos", new { nombre, tipo, driver = "demo", estanciaId = estancia });
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Agrupa_los_sensores_por_estancia_y_marca_el_frio_y_el_moho()
    {
        var c = await factory.Dentro();
        var e = await c.PostAsJsonAsync("/api/domotica/estancias", new { nombre = "Dormitorio", icono = "🛏️" });
        var estancia = (await e.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var termo = await Crear(c, "Termo dormitorio", "sensor", estancia);
        await c.GetAsync("/api/domotica");      // el sensor lee su primer valor

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var temp = await db.Entidades.FirstAsync(x => x.AparatoId == termo && x.Clave == "temperatura");
            var hum = await db.Entidades.FirstAsync(x => x.AparatoId == termo && x.Clave == "humedad");
            var ahora = DateTimeOffset.UtcNow;
            db.Lecturas.AddRange(new LecturaCasa { EntidadId = temp.Id, Valor = 15.5, Fecha = ahora.AddHours(-3) }, new LecturaCasa { EntidadId = temp.Id, Valor = 19, Fecha = ahora.AddHours(-1) },
                                 new LecturaCasa { EntidadId = hum.Id, Valor = 70, Fecha = ahora.AddHours(-2) });
            await db.SaveChangesAsync();
        }

        var r = await c.GetFromJsonAsync<JsonElement>("/api/domotica/clima?dias=1");
        var est = r.GetProperty("estancias").EnumerateArray().First(x => x.GetProperty("nombre").GetString() == "Dormitorio");
        Assert.True(est.GetProperty("hizoFrio").GetBoolean());
        Assert.True(est.GetProperty("riesgoMoho").GetBoolean());
        Assert.Equal(15.5, est.GetProperty("temperatura").GetProperty("min").GetDouble());
        Assert.Equal("Termo dormitorio", est.GetProperty("sensores")[0].GetProperty("nombre").GetString());
        Assert.Equal(17, r.GetProperty("frioBajo").GetDouble()); Assert.Equal(65, r.GetProperty("humedadAlta").GetDouble());

        var csv = await (await c.GetAsync("/api/domotica/clima/csv?dias=1")).Content.ReadAsStringAsync();
        Assert.StartsWith("estancia;sensor;dato;fecha;valor", csv);
        Assert.Contains("Dormitorio;Termo dormitorio;temperatura;", csv);
    }

    [Fact]
    public async Task Un_aparato_sin_estancia_sale_en_sin_asignar_y_los_dias_se_acotan()
    {
        var c = await factory.Dentro();
        await Crear(c, "Termo suelto", "sensor");
        await c.GetAsync("/api/domotica");
        var r = await c.GetFromJsonAsync<JsonElement>("/api/domotica/clima?dias=99999");
        Assert.Equal(400, r.GetProperty("dias").GetInt32());
        Assert.Contains(r.GetProperty("estancias").EnumerateArray(), x => x.GetProperty("nombre").GetString() == "Sin asignar");
    }

    [Fact]
    public async Task El_resumen_guarda_los_dias_cerrados_sin_duplicar_y_poda_lo_muy_viejo()
    {
        var db = BaseDeDatos.Nueva();
        var zona = TimeZoneInfo.Utc;
        var ahora = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var a = new Aparato { Nombre = "T", Tipo = "sensor" }; db.Aparatos.Add(a); await db.SaveChangesAsync();
        var e = new Entidad { AparatoId = a.Id, Clave = "temperatura", Valor = "20" }; db.Entidades.Add(e); await db.SaveChangesAsync();
        db.Lecturas.AddRange(
            new LecturaCasa { EntidadId = e.Id, Valor = 18, Fecha = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero) },
            new LecturaCasa { EntidadId = e.Id, Valor = 22, Fecha = new DateTimeOffset(2026, 11, 5, 14, 0, 0, TimeSpan.Zero) },
            new LecturaCasa { EntidadId = e.Id, Valor = 30, Fecha = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero) });      // hoy: no se resume
        db.ResumenesCasaDia.Add(new ResumenCasaDia { EntidadId = e.Id, Dia = new DateOnly(2025, 1, 1), Min = 1, Media = 1, Max = 1, Muestras = 1 });   // más de 400 días
        await db.SaveChangesAsync();

        await ClimaCasa.Resumir(db, ahora, zona, default);
        await ClimaCasa.Resumir(db, ahora, zona, default);       // repetirlo no duplica

        var filas = await db.ResumenesCasaDia.AsNoTracking().ToListAsync();
        var f = Assert.Single(filas);
        Assert.Equal(new DateOnly(2026, 11, 5), f.Dia);
        Assert.Equal(18, f.Min); Assert.Equal(20, f.Media); Assert.Equal(22, f.Max); Assert.Equal(2, f.Muestras);
    }
}
