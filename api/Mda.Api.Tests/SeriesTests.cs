using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Series;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Tests;

public class SeriesLogicaTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Lee_una_ficha_de_tvmaze_con_el_siguiente_episodio_en_hora_local()
    {
        var f = SeriesLogica.Interpretar(J("""
            {"id":82,"name":"Juego de Tronos","status":"Ended","averageRuntime":60,"genres":["Drama","Fantasy","Adventure","Action","Extra"],"network":{"name":"HBO"},
             "image":{"medium":"http://static.tvmaze.com/a.jpg"},"_embedded":{"nextepisode":{"season":3,"number":5,"airdate":"2026-11-08","airstamp":"2026-11-07T23:30:00+00:00"}}}
            """), Madrid)!;
        Assert.Equal((82, "Juego de Tronos", "HBO", 60, true), (f.Id, f.Nombre, f.Plataforma, f.Minutos, f.Terminada));
        Assert.Equal(4, f.Generos.Length);
        Assert.Equal("https://static.tvmaze.com/a.jpg", f.Imagen);
        // 23:30 UTC del 7 ya es el 8 en Madrid (invierno, UTC+1): la fecha cambia
        Assert.Equal(new ProximoEp(new DateOnly(2026, 11, 8), 3, 5), f.Proximo);
    }

    [Fact]
    public void Sin_airstamp_vale_la_fecha_y_sin_siguiente_episodio_no_hay_proximo()
    {
        var f = SeriesLogica.Interpretar(J("""{"id":1,"name":"X","webChannel":{"name":"Netflix"},"_embedded":{"nextepisode":{"season":1,"number":2,"airdate":"2026-12-01"}}}"""), Madrid)!;
        Assert.Equal(new DateOnly(2026, 12, 1), f.Proximo!.Fecha); Assert.Equal("Netflix", f.Plataforma); Assert.False(f.Terminada);
        Assert.Null(SeriesLogica.Interpretar(J("""{"id":2,"name":"Y"}"""), Madrid)!.Proximo);
        Assert.Null(SeriesLogica.Interpretar(J("""{"id":2,"name":"Y","_embedded":{"nextepisode":{"season":1,"number":2}}}"""), Madrid)!.Proximo);      // sin fecha
    }

    [Fact]
    public void Una_respuesta_rara_no_da_ficha()
    {
        Assert.Null(SeriesLogica.Interpretar(J("[]"), Madrid)); Assert.Null(SeriesLogica.Interpretar(J("{}"), Madrid)); Assert.Null(SeriesLogica.Interpretar(J("""{"id":"x","name":"A"}"""), Madrid));
        Assert.Null(SeriesLogica.Interpretar(J("""{"id":3,"name":"  "}"""), Madrid));
    }

    [Fact]
    public void El_aviso_es_el_dia_antes_y_el_mismo_dia()
    {
        var hoy = new DateOnly(2026, 11, 7);
        Assert.Equal(0, SeriesLogica.NivelAviso(null, hoy)); Assert.Equal(0, SeriesLogica.NivelAviso(hoy.AddDays(2), hoy)); Assert.Equal(0, SeriesLogica.NivelAviso(hoy.AddDays(-1), hoy));
        Assert.Equal(1, SeriesLogica.NivelAviso(hoy.AddDays(1), hoy)); Assert.Equal(2, SeriesLogica.NivelAviso(hoy, hoy));
        var t = new Titulo { Nombre = "Serie", ProximaTemporada = 3, ProximoEpisodio = 5 };
        Assert.Equal("Mañana sale el 3×05 de «Serie».", SeriesLogica.TextoAviso(t, 1));
        Assert.Equal("Hoy sale el 3×05 de «Serie».", SeriesLogica.TextoAviso(t, 2));
        Assert.Equal("Hoy se estrena «Peli».", SeriesLogica.TextoAviso(new Titulo { Tipo = "pelicula", Nombre = "Peli" }, 2));
    }

    [Fact]
    public void El_resumen_semanal_lista_lo_que_sale_en_siete_dias_por_orden_y_sin_lo_visto()
    {
        var hoy = new DateOnly(2026, 11, 8);
        var l = new[]
        {
            new Titulo { Nombre = "B", Proximo = hoy.AddDays(3), ProximaTemporada = 1, ProximoEpisodio = 2 }, new Titulo { Nombre = "A", Proximo = hoy, ProximaTemporada = 2, ProximoEpisodio = 7 },
            new Titulo { Nombre = "Lejos", Proximo = hoy.AddDays(7) }, new Titulo { Nombre = "Vista", Estado = "vista", Proximo = hoy.AddDays(1) }, new Titulo { Nombre = "Sin fecha" },
            new Titulo { Tipo = "pelicula", Nombre = "Peli", Proximo = hoy.AddDays(5) },
        };
        var s = SeriesLogica.Semanal(l, hoy)!;
        Assert.True(s.IndexOf("A 2×07") < s.IndexOf("B 1×02") && s.IndexOf("B 1×02") < s.IndexOf("Peli"));
        Assert.DoesNotContain("Lejos", s); Assert.DoesNotContain("Vista", s); Assert.DoesNotContain("Sin fecha", s);
        Assert.Null(SeriesLogica.Semanal([], hoy));
    }

    [Fact]
    public void Ver_un_episodio_sube_uno_pasa_a_viendo_y_limpia_el_proximo_si_ya_estaba_visto()
    {
        var hoy = new DateOnly(2026, 11, 8);
        var t = new Titulo { Estado = "por-ver", Temporada = 0, Episodio = 0 };
        SeriesLogica.Siguiente(t, hoy);
        Assert.Equal((1, 1, "viendo"), (t.Temporada, t.Episodio, t.Estado));
        t.Temporada = 3; t.Episodio = 4; t.Proximo = hoy; t.ProximaTemporada = 3; t.ProximoEpisodio = 5; t.AvisadoEstreno = hoy; t.AvisadoNivel = 2;
        SeriesLogica.Siguiente(t, hoy);
        Assert.Equal(5, t.Episodio); Assert.Null(t.Proximo); Assert.Null(t.AvisadoEstreno);
        t.Proximo = hoy.AddDays(3); t.ProximoEpisodio = 7;
        SeriesLogica.Siguiente(t, hoy);
        Assert.NotNull(t.Proximo);                                   // aún no ha salido el 7: el proximo se queda
    }

    [Fact]
    public void Las_estadisticas_del_año_cuentan_horas_generos_y_notas()
    {
        var l = new[]
        {
            new Titulo { Tipo = "serie", Estado = "viendo", Episodio = 12, Minutos = 50, Generos = "Drama,Sci-Fi", Nota = 8 },
            new Titulo { Tipo = "pelicula", Estado = "vista", Minutos = 120, Generos = "Drama", Terminado = new DateOnly(2026, 3, 1), Nota = 6 },
            new Titulo { Tipo = "pelicula", Estado = "vista", Minutos = 90, Terminado = new DateOnly(2025, 3, 1) },
            new Titulo { Tipo = "serie", Estado = "por-ver", Minutos = 40 },
        };
        var j = JsonSerializer.SerializeToElement(SeriesLogica.Estadisticas(l, 2026));
        Assert.Equal(1, j.GetProperty("vistasEsteAnio").GetInt32());
        Assert.Equal(((12 * 50) + 120 + 90) / 60.0, j.GetProperty("horas").GetDouble(), 1);
        Assert.Equal("Drama", j.GetProperty("generos")[0].GetProperty("genero").GetString());
        Assert.Equal(7.0, j.GetProperty("notaMedia").GetDouble());
        Assert.Equal(1, j.GetProperty("porVer").GetInt32());
    }
}

public class SeriesAvisosTests
{
    private sealed class AvisosFalsos : IAvisos
    {
        public readonly List<string> Telegram = [];
        public Canales Disponibles() => throw new NotImplementedException();
        public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorTelegram(string texto, CancellationToken ct = default) { Telegram.Add(texto); return Task.FromResult(true); }
    }

    [Fact]
    public async Task Aviso_de_vispera_y_de_dia_sin_repetir_y_sin_avisar_de_lo_ya_visto()
    {
        var db = BaseDeDatos.Nueva();
        db.Titulos.AddRange(new Titulo { Nombre = "Sigo", Estado = "viendo", Proximo = new DateOnly(2026, 11, 8), ProximaTemporada = 2, ProximoEpisodio = 3 },
                            new Titulo { Nombre = "Vista", Estado = "vista", Proximo = new DateOnly(2026, 11, 8) });
        await db.SaveChangesAsync();
        var av = new AvisosFalsos();
        var zona = new Zona(TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid"));
        async Task Pasada(DateOnly hoy, int hora = 12) => await SeriesServicio.Revisar(db, null!, av, zona, hoy, hoy.ToDateTime(new TimeOnly(hora, 0)), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default);

        // marcar el refresco como hecho hoy para no llamar a TVMaze (en las pruebas no hay red ni servicio)
        db.Ajustes.Add(new Mda.Api.Seguridad.Ajuste { Clave = SeriesServicio.ClaveRefresco, Valor = new DateOnly(2026, 11, 7).ToString("O") }); await db.SaveChangesAsync();
        await Pasada(new DateOnly(2026, 11, 7)); await Pasada(new DateOnly(2026, 11, 7));
        Assert.Single(av.Telegram); Assert.Contains("Mañana sale el 2×03 de «Sigo»", av.Telegram[0]);
        (await db.Ajustes.FirstAsync(a => a.Clave == SeriesServicio.ClaveRefresco)).Valor = new DateOnly(2026, 11, 8).ToString("O"); await db.SaveChangesAsync();
        await Pasada(new DateOnly(2026, 11, 8));
        Assert.Equal(2, av.Telegram.Count); Assert.Contains("Hoy sale", av.Telegram[1]);
    }
}

public class SeriesApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Apuntar_a_mano_ver_episodios_valorar_y_estadisticas()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/series", new { tipo = "documental", nombre = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/series", new { tipo = "serie", nombre = "" })).StatusCode);

        var r = await c.PostAsJsonAsync("/api/series", new { tipo = "serie", nombre = "Mi serie", plataforma = "Netflix", minutos = "45" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var v = await (await c.PostAsync($"/api/series/{id}/siguiente", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("viendo", v.GetProperty("estado").GetString()); Assert.Equal("1×01", v.GetProperty("visto").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/series/{id}", new { nota = 11 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/series/{id}", new { estado = "abandonada" })).StatusCode);
        var fin = await (await c.PutAsJsonAsync($"/api/series/{id}", new { estado = "vista", nota = 9 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(9, fin.GetProperty("nota").GetInt32());

        var peli = await c.PostAsJsonAsync("/api/series", new { tipo = "pelicula", nombre = "Peli futura", estreno = "2099-01-01", minutos = "100" });
        Assert.Equal(HttpStatusCode.Created, peli.StatusCode);
        var est = await c.GetFromJsonAsync<JsonElement>("/api/series/estadisticas");
        Assert.True(est.GetProperty("vistasEsteAnio").GetInt32() >= 1);

        var lista = await c.GetFromJsonAsync<JsonElement>("/api/series");
        Assert.Equal("Peli futura", lista.EnumerateArray().First().GetProperty("nombre").GetString());          // lo que tiene fecha de estreno va primero

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/series/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync($"/api/series/{id}/siguiente", null)).StatusCode);
    }

    [Fact]
    public async Task La_busqueda_pide_texto_y_hace_falta_entrar()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/series/buscar?q=")).StatusCode);
        using var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/series")).StatusCode);
    }
}
