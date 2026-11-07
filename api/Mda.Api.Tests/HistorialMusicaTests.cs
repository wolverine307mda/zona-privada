using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Conexiones;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class HistorialMusicaLogicaTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
    private static Escucha E(string id, string nombre, string artistas, DateTimeOffset fecha, int minutos = 3) =>
        new() { CancionId = id, Nombre = nombre, Artistas = artistas, Fecha = fecha, Duracion = minutos * 60000 };
    private static DateTimeOffset F(int d, int h = 12) => new(2026, 11, d, h, 0, 0, TimeSpan.Zero);

    [Fact]
    public void El_top_de_canciones_cuenta_repeticiones_y_desempata_por_nombre()
    {
        var l = new[] { E("a", "Zeta", "X", F(1)), E("a", "Zeta", "X", F(2)), E("b", "Beta", "Y", F(1)), E("c", "Alfa", "Y", F(1)), E("a", "Zeta", "X", F(3)) };
        var top = EstadisticasMusica.TopCanciones(l, 3);
        Assert.Equal(["Zeta", "Alfa", "Beta"], top.Select(t => t.Nombre));
        Assert.Equal(3, top[0].Veces);
    }

    [Fact]
    public void Una_colaboracion_cuenta_para_cada_artista()
    {
        var top = EstadisticasMusica.TopArtistas([E("a", "T1", "Rosalía, Bad Bunny", F(1)), E("b", "T2", "Bad Bunny", F(2))]);
        Assert.Equal(("Bad Bunny", 2), (top[0].Nombre, top[0].Veces));
        Assert.Equal(("Rosalía", 1), (top[1].Nombre, top[1].Veces));
    }

    [Fact]
    public void Las_horas_se_cuentan_en_hora_local_y_suman_veinticuatro_casillas()
    {
        var h = EstadisticasMusica.Horas([E("a", "T", "X", F(3, 21)), E("b", "T", "X", F(3, 22)), E("c", "T", "X", F(3, 22))], Madrid);   // en noviembre Madrid es UTC+1
        Assert.Equal(24, h.Length);
        Assert.Equal(1, h[22]); Assert.Equal(2, h[23]);
    }

    [Fact]
    public void La_racha_cuenta_dias_seguidos_hasta_hoy_o_hasta_ayer()
    {
        var hoy = new DateOnly(2026, 11, 7);
        var seguidos = new[] { E("a", "T", "X", F(7)), E("b", "T", "X", F(6)), E("c", "T", "X", F(5)), E("d", "T", "X", F(2)) };
        Assert.Equal(3, EstadisticasMusica.Racha(seguidos, Madrid, hoy));
        Assert.Equal(2, EstadisticasMusica.Racha(seguidos.Skip(1), Madrid, hoy));          // hoy todavía no ha sonado nada: cuenta desde ayer
        Assert.Equal(0, EstadisticasMusica.Racha([E("d", "T", "X", F(2))], Madrid, hoy));
        Assert.Equal(0, EstadisticasMusica.Racha([], Madrid, hoy));
    }

    [Fact]
    public void Los_artistas_nuevos_son_los_que_no_sonaban_antes_del_periodo()
    {
        var todas = new List<Escucha> { E("a", "T", "Vieja", F(1)), E("b", "T", "Vieja, Nueva", F(5)), E("c", "T", "nueva", F(6)), E("d", "T", "Otra", F(6)) };
        Assert.Equal(["Nueva", "Otra"], EstadisticasMusica.ArtistasNuevos(todas, F(4)));
    }

    [Fact]
    public void El_total_son_minutos_y_canciones()
    {
        Assert.Equal((9L, 3), EstadisticasMusica.Total([E("a", "T", "X", F(1)), E("b", "T", "X", F(1)), E("c", "T", "X", F(1))]));
        Assert.Equal((0L, 0), EstadisticasMusica.Total([]));
    }

    [Fact]
    public void El_inicio_del_periodo_es_hora_local()
    {
        var ahora = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(1)), EstadisticasMusica.Desde("mes", ahora, Madrid));
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)), EstadisticasMusica.Desde("anio", ahora, Madrid));
        Assert.Equal(ahora.AddDays(-7), EstadisticasMusica.Desde("semana", ahora, Madrid));
        Assert.Null(EstadisticasMusica.Desde("todo", ahora, Madrid));
    }

    [Fact]
    public void Interpreta_recently_played_y_se_salta_lo_que_no_tiene_id_o_fecha()
    {
        var json = """
        {"items":[
          {"played_at":"2026-11-07T10:00:00.123Z","track":{"id":"t1","name":"Canción","duration_ms":200000,"artists":[{"name":"A"},{"name":"B"}],"album":{"name":"Disco","images":[{"url":"grande"},{"url":"mediana"}]}}},
          {"played_at":"2026-11-07T09:00:00Z","track":{"id":null,"name":"Local","artists":[]}},
          {"played_at":"no-es-fecha","track":{"id":"t2","name":"X","artists":[{"name":"A"}]}},
          {"track":{"id":"t3","name":"Sin fecha","artists":[{"name":"A"}]}}
        ]}
        """;
        var l = RecolectorMusica.Interpretar(JsonDocument.Parse(json).RootElement);
        var e = Assert.Single(l);
        Assert.Equal("t1", e.CancionId); Assert.Equal("A, B", e.Artistas); Assert.Equal("mediana", e.Imagen); Assert.Equal(200000, e.Duracion);
        Assert.Equal(TimeSpan.Zero, e.Fecha.Offset);
        Assert.Empty(RecolectorMusica.Interpretar(JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public async Task Guardar_no_repite_la_misma_cancion_a_la_misma_hora()
    {
        var db = BaseDeDatos.Nueva();
        var a = E("t1", "Canción", "A", F(7, 10));
        Assert.Equal(1, await RecolectorMusica.Guardar(db, [a, E("t1", "Canción", "A", F(7, 10))], default));        // un duplicado dentro de la misma tanda
        Assert.Equal(1, await RecolectorMusica.Guardar(db, [E("t1", "Canción", "A", F(7, 10)), E("t1", "Canción", "A", F(7, 11))], default));   // solo la de las 11 es nueva
        Assert.Equal(0, await RecolectorMusica.Guardar(db, [], default));
        Assert.Equal(2, await db.Escuchas.CountAsync());
    }
}

public class HistorialMusicaApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Sin_datos_responde_vacio_y_con_datos_da_las_cuentas_y_se_puede_borrar_todo()
    {
        var c = await factory.Dentro();
        var vacio = await c.GetFromJsonAsync<JsonElement>("/api/musica/historial?periodo=mes");
        Assert.False(vacio.GetProperty("hayDatos").GetBoolean());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ahora = DateTimeOffset.UtcNow;
            db.Escuchas.AddRange(
                new Escucha { CancionId = "a", Nombre = "Uno", Artistas = "Grupo", Duracion = 180000, Fecha = ahora.AddHours(-1) },
                new Escucha { CancionId = "a", Nombre = "Uno", Artistas = "Grupo", Duracion = 180000, Fecha = ahora.AddHours(-2) },
                new Escucha { CancionId = "b", Nombre = "Dos", Artistas = "Otro", Duracion = 240000, Fecha = ahora.AddHours(-3) });
            await db.SaveChangesAsync();
        }
        var r = await c.GetFromJsonAsync<JsonElement>("/api/musica/historial?periodo=semana");
        Assert.True(r.GetProperty("hayDatos").GetBoolean());
        Assert.Equal(3, r.GetProperty("canciones").GetInt32());
        Assert.Equal(10, r.GetProperty("minutos").GetInt32());
        Assert.Equal("Uno", r.GetProperty("topCanciones")[0].GetProperty("nombre").GetString());
        Assert.Equal(24, r.GetProperty("horas").GetArrayLength());
        Assert.Equal("Uno", r.GetProperty("cancionDeLaSemana").GetProperty("nombre").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/musica/historial")).StatusCode);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/musica/historial")).GetProperty("hayDatos").GetBoolean());
    }

    [Fact]
    public async Task Hace_falta_entrar()
    {
        using var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/musica/historial")).StatusCode);
    }
}
