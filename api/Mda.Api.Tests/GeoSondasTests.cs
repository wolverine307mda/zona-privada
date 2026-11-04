using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Seguridad;

namespace Mda.Api.Tests;

public class GeoSondasTests
{
    [Fact]
    public void Interpreta_el_formato_MaxMind_con_pais_ciudad_y_coordenadas()
    {
        var d = new Dictionary<string, object>
        {
            ["country"] = new Dictionary<string, object> { ["iso_code"] = "US", ["names"] = new Dictionary<string, object> { ["en"] = "United States" } },
            ["city"] = new Dictionary<string, object> { ["names"] = new Dictionary<string, object> { ["en"] = "Columbus" } },
            ["location"] = new Dictionary<string, object> { ["latitude"] = 39.96, ["longitude"] = -83.0 },
        };
        var g = GeoIp.Interpretar(d)!;
        Assert.Equal(("US", "Columbus", 39.96, -83.0), (g.Pais, g.Ciudad, g.Lat, g.Lon));
    }

    [Fact]
    public void Sin_ubicacion_o_con_coordenadas_imposibles_no_hay_punto()
    {
        Assert.Null(GeoIp.Interpretar(null));
        Assert.Null(GeoIp.Interpretar(new Dictionary<string, object> { ["country"] = new Dictionary<string, object>() }));
        Assert.Null(GeoIp.Interpretar(new Dictionary<string, object> { ["location"] = new Dictionary<string, object> { ["latitude"] = 95.0, ["longitude"] = 0.0 } }));
    }

    [Fact]
    public void Agrupa_las_sondas_cercanas_en_un_punto_con_sus_rutas_y_cuenta_las_ips()
    {
        var t = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        var l = MapaSondas.Agrupar([
            new SondaRegistrada { Ip = "1.1.1.1", Ruta = "/.env", Pais = "US", Ciudad = "Columbus", Lat = 39.96, Lon = -83.0, Hora = t },
            new SondaRegistrada { Ip = "1.1.1.2", Ruta = "/.env", Pais = "US", Ciudad = "Columbus", Lat = 39.99, Lon = -83.02, Hora = t.AddMinutes(5) },
            new SondaRegistrada { Ip = "2.2.2.2", Ruta = "/wp-login.php", Pais = "DE", Ciudad = "Berlin", Lat = 52.5, Lon = 13.4, Hora = t },
            new SondaRegistrada { Ip = "3.3.3.3", Ruta = "/admin", Lat = null, Lon = null, Hora = t }]);
        Assert.Equal(2, l.Count);
        Assert.Equal(("US", 2, 2), (l[0].Pais, l[0].Sondas, l[0].Ips));
        Assert.Equal("/.env", l[0].Rutas[0]);
        Assert.Equal(t.AddMinutes(5), l[0].Ultima);
    }
}

public class SondasApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Pide_sesion_y_devuelve_el_resumen_aunque_no_haya_base_de_geolocalizacion()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/seguridad/sondas")).StatusCode);
        var j = await (await factory.Dentro()).GetFromJsonAsync<JsonElement>("/api/seguridad/sondas?dias=7");
        Assert.Equal(7, j.GetProperty("dias").GetInt32());
        Assert.Equal(0, j.GetProperty("total").GetInt32());
        Assert.Equal(0, j.GetProperty("puntos").GetArrayLength());
    }
}
