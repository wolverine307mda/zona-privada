using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Conexiones;

namespace Mda.Api.Tests;

public class MomentosLogicaTests
{
    [Theory]
    [InlineData("spotify:playlist:37i9dQZF1DXcBWIGoYBM5M", "spotify:playlist:37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=abc123", "spotify:playlist:37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("https://open.spotify.com/intl-es/playlist/37i9dQZF1DXcBWIGoYBM5M", "spotify:playlist:37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("  spotify:playlist:37i9dQZF1DXcBWIGoYBM5M  ", "spotify:playlist:37i9dQZF1DXcBWIGoYBM5M")]
    public void Acepta_la_uri_y_la_direccion_de_una_lista(string entrada, string esperada) => Assert.Equal(esperada, Momentos.NormalizarLista(entrada));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("spotify:track:37i9dQZF1DXcBWIGoYBM5M")] [InlineData("spotify:playlist:corta")] [InlineData("https://evil.com/playlist/37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("spotify:playlist:37i9dQZF1DXcBWIGoYBM5M; rm -rf")] [InlineData("https://open.spotify.com/album/37i9dQZF1DXcBWIGoYBM5M")]
    public void Rechaza_lo_que_no_es_una_lista_de_spotify(string? entrada) => Assert.Null(Momentos.NormalizarLista(entrada));

    private static MomentoMusical M(int orden, string nombre, string? desde, string? hasta, string lista = "spotify:playlist:37i9dQZF1DXcBWIGoYBM5M") =>
        new() { Id = orden + 1, Orden = orden, Nombre = nombre, Desde = desde, Hasta = hasta, PlaylistUri = lista };

    [Fact]
    public void Sugiere_el_momento_cuya_franja_contiene_la_hora_incluso_pasando_de_medianoche()
    {
        var l = new[] { M(0, "Concentración", "09:00", "13:00"), M(1, "Dormir", "22:30", "02:00"), M(2, "Fiesta", null, null) };
        Assert.Equal("Concentración", Momentos.Sugerido(l, new TimeOnly(10, 0))!.Nombre);
        Assert.Equal("Dormir", Momentos.Sugerido(l, new TimeOnly(23, 0))!.Nombre);
        Assert.Equal("Dormir", Momentos.Sugerido(l, new TimeOnly(1, 0))!.Nombre);
        Assert.Null(Momentos.Sugerido(l, new TimeOnly(15, 0)));
    }

    [Fact]
    public void Sin_lista_elegida_o_sin_franja_no_se_sugiere()
    {
        Assert.Null(Momentos.Sugerido([M(0, "Concentración", "09:00", "13:00", lista: "")], new TimeOnly(10, 0)));
        Assert.Null(Momentos.Sugerido([M(0, "Fiesta", null, null)], new TimeOnly(10, 0)));
    }

    [Fact]
    public void Gana_el_primero_por_orden_si_se_solapan()
    {
        var l = new[] { M(1, "Segundo", "09:00", "13:00"), M(0, "Primero", "10:00", "11:00") };
        Assert.Equal("Primero", Momentos.Sugerido(l, new TimeOnly(10, 30))!.Nombre);
    }

    [Fact]
    public void La_validacion_comprueba_nombre_lista_horas_y_volumen()
    {
        Assert.Empty(Momentos.Validar(new MomentoEntrada("Estudio", "📚", "spotify:playlist:37i9dQZF1DXcBWIGoYBM5M", null, "09:00", "12:00", 30, true), true));
        var mal = Momentos.Validar(new MomentoEntrada("", null, "https://otra.com/x", null, "25:00", "no", 150, null), true);
        foreach (var k in new[] { "nombre", "playlistUri", "desde", "hasta", "volumen" }) Assert.Contains(k, mal.Keys);
        Assert.Empty(Momentos.Validar(new MomentoEntrada(null, null, null, null, null, null, null, null), false));
    }

    [Fact]
    public void Los_de_serie_son_cinco_con_su_volumen_y_franjas_validas()
    {
        Assert.Equal(["Concentración", "Cocina", "Entrenar", "Dormir", "Fiesta"], Momentos.DeSerie.Select(m => m.Nombre));
        foreach (var m in Momentos.DeSerie) { Assert.True(m.Desde is null || Mda.Api.Contexto.ServicioContexto.HoraValida(m.Desde)); Assert.InRange(m.Volumen ?? 0, 0, 100); }
    }
}

public class MomentosApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Los_momentos_de_serie_aparecen_se_edita_la_lista_y_se_puede_crear_y_borrar()
    {
        var c = await factory.Dentro();
        var l = await c.GetFromJsonAsync<JsonElement>("/api/musica/momentos");
        var momentos = l.GetProperty("momentos");
        Assert.True(momentos.GetArrayLength() >= 5);
        var dormir = momentos.EnumerateArray().First(m => m.GetProperty("nombre").GetString() == "Dormir");
        var id = dormir.GetProperty("id").GetInt32();

        var mala = await c.PutAsJsonAsync($"/api/musica/momentos/{id}", new { playlistUri = "https://otra.com/x" });
        Assert.Equal(HttpStatusCode.BadRequest, mala.StatusCode);
        var ok = await c.PutAsJsonAsync($"/api/musica/momentos/{id}", new { playlistUri = "https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=x", playlistNombre = "Sueño", volumen = 10 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var e = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("spotify:playlist:37i9dQZF1DXcBWIGoYBM5M", e.GetProperty("playlistUri").GetString());
        Assert.Equal(10, e.GetProperty("volumen").GetInt32());

        var nuevo = await c.PostAsJsonAsync("/api/musica/momentos", new { nombre = "Lectura", icono = "📖" });
        Assert.Equal(HttpStatusCode.Created, nuevo.StatusCode);
        var nid = (await nuevo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/musica/momentos/{nid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/musica/momentos/{nid}")).StatusCode);
    }

    [Fact]
    public async Task Sin_spotify_conectado_o_sin_lista_el_momento_lo_explica()
    {
        var c = await factory.Dentro();
        var l = await c.GetFromJsonAsync<JsonElement>("/api/musica/momentos");
        var id = l.GetProperty("momentos")[0].GetProperty("id").GetInt32();
        var r = await c.PostAsync($"/api/musica/momento/{id}", null);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);                     // sin lista elegida (o sin Spotify): 409 con explicación
        Assert.False(string.IsNullOrWhiteSpace((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detalle").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/musica/momento/99999", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync("/api/musica/playlists")).StatusCode);
    }

    [Fact]
    public async Task Hace_falta_entrar()
    {
        using var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/musica/momentos")).StatusCode);
    }
}
