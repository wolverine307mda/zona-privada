using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Luz;

namespace Mda.Api.Tests;

public class PuntosCalibracionTests
{
    private static SalidaFoto S(string tipo, int? prev, int res) => new() { Tipo = tipo, Prevision = prev, Resultado = res };

    [Fact]
    public void Necesita_tres_salidas_con_prevision_de_cada_tipo()
    {
        Assert.Empty(Puntos.Calibrar([S("noche", 8, 5), S("noche", 8, 5), S("atardecer", 5, 3)]));
        Assert.Empty(Puntos.Calibrar([S("noche", null, 5), S("noche", null, 5), S("noche", null, 5)]));
    }

    [Fact]
    public void Si_sales_mejor_que_la_prevision_lo_dice_y_si_promete_de_mas_tambien()
    {
        var mejor = Assert.Single(Puntos.Calibrar([S("noche", 4, 5), S("noche", 5, 5), S("noche", 6, 5)]));      // 10-4, 10-5, 10-6 → +5
        Assert.Equal(5, mejor.Diferencia);
        Assert.Contains("más de lo que promete", mejor.Texto);
        var peor = Assert.Single(Puntos.Calibrar([S("atardecer", 9, 2), S("atardecer", 8, 2), S("atardecer", 9, 3)]));
        Assert.True(peor.Diferencia < 0);
        Assert.Contains("promete", peor.Texto);
        Assert.Contains("acierta", Assert.Single(Puntos.Calibrar([S("amanecer", 6, 3), S("amanecer", 8, 4), S("amanecer", 5, 3)])).Texto);
    }
}

public class PuntosApiTests(LuzFactory factory) : IClassFixture<LuzFactory>
{
    [Fact]
    public async Task Sin_sesion_no_se_ve_nada() => Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/luz/puntos")).StatusCode);

    [Fact]
    public async Task Crea_valida_lista_y_borra_puntos()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/luz/puntos", new { nombre = "", lat = 95, lon = 0, orientacion = "X" })).StatusCode);
        var r = await c.PostAsJsonAsync("/api/luz/puntos", new { nombre = "Faro", lat = 43.5, lon = -8.2, orientacion = "NO", notas = "con marea baja" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Contains((await c.GetFromJsonAsync<JsonElement>("/api/luz/puntos")).EnumerateArray(), p => p.GetProperty("nombre").GetString() == "Faro");
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/luz/puntos/{id}", new { nombre = "Faro viejo", lat = 43.5, lon = -8.2, orientacion = "N" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/luz/puntos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/luz/puntos/{id}")).StatusCode);
    }

    [Fact]
    public async Task El_diario_valida_guarda_y_calibra()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/luz/salidas", new { fecha = "ayer", tipo = "tarde", resultado = 9 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/luz/salidas", new { fecha = "2026-11-01", tipo = "noche", resultado = 4, puntoId = 99999 })).StatusCode);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/luz/salidas", new { fecha = $"2026-11-0{i + 1}", tipo = "noche", prevision = 5, resultado = 5, nota = "increíble" })).StatusCode);
        var l = await c.GetFromJsonAsync<JsonElement>("/api/luz/salidas");
        Assert.True(l.GetProperty("salidas").GetArrayLength() >= 3);
        Assert.Contains(l.GetProperty("calibracion").EnumerateArray(), x => x.GetProperty("tipo").GetString() == "noche");
    }

    [Fact]
    public async Task Un_punto_cambia_el_lugar_del_dia()
    {
        var c = await factory.Dentro();
        var id = (await (await c.PostAsJsonAsync("/api/luz/puntos", new { nombre = "Montaña", lat = 42.8, lon = -0.6 })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var d = await c.GetFromJsonAsync<JsonElement>($"/api/luz/dia?punto={id}");
        Assert.False(d.GetProperty("sinLugar").GetBoolean());
        Assert.Equal("Montaña", d.GetProperty("lugar").GetString());
    }
}
