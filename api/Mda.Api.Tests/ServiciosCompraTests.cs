using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Mda.Api.Compra;
using Mda.Api.Servicios;

namespace Mda.Api.Tests;

public class ServiciosVigiladosTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private static Comprobacion Bien(int ms = 40) => new(true, ms, null);
    private static Comprobacion Mal(string e = "sin respuesta") => new(false, null, e);

    [Fact]
    public void Un_solo_fallo_no_avisa_pero_dos_seguidos_si()
    {
        var s = new ServicioVigilado { Nombre = "web" };
        Assert.False(ServicioServicios.Aplicar(s, Bien(), T0).Cayo);
        Assert.Equal("ok", s.Estado);
        Assert.False(ServicioServicios.Aplicar(s, Mal(), T0.AddMinutes(2)).Cayo);   // el primer fallo: todavía "ok"
        Assert.Equal("ok", s.Estado);
        var c = ServicioServicios.Aplicar(s, Mal("timeout"), T0.AddMinutes(4));
        Assert.True(c.Cayo);
        Assert.Equal("caido", s.Estado);
        Assert.Equal(1, s.Caidas);
        Assert.Equal("timeout", s.UltimoError);
        Assert.False(ServicioServicios.Aplicar(s, Mal(), T0.AddMinutes(6)).Cayo);    // ya estaba caído: no se avisa otra vez
    }

    [Fact]
    public void Al_volver_dice_cuanto_estuvo_caido()
    {
        var s = new ServicioVigilado { Nombre = "nas" };
        ServicioServicios.Aplicar(s, Bien(), T0);
        ServicioServicios.Aplicar(s, Mal(), T0.AddMinutes(2));
        ServicioServicios.Aplicar(s, Mal(), T0.AddMinutes(4));
        var c = ServicioServicios.Aplicar(s, Bien(), T0.AddMinutes(34));
        Assert.True(c.Volvio);
        Assert.Equal(TimeSpan.FromMinutes(30), c.Duracion);
        Assert.Equal("ok", s.Estado);
        Assert.Equal(0, s.FallosSeguidos);
    }

    [Theory]
    [InlineData("http", "https://ejemplo.com", true)] [InlineData("http", "http://192.168.1.1:8080/ruta", true)] [InlineData("http", "ftp://ejemplo.com", false)]
    [InlineData("http", "https://u:p@ejemplo.com", false)] [InlineData("http", "ejemplo.com", false)] [InlineData("http", "https://a b.com", false)]
    [InlineData("tcp", "192.168.1.1:80", true)] [InlineData("tcp", "mi-nas.local:5000", true)] [InlineData("tcp", "host", false)] [InlineData("tcp", "host:0", false)]
    [InlineData("tcp", "host:99999", false)] [InlineData("tcp", "ho st:80", false)] [InlineData("tcp", "https://ejemplo.com", false)]
    public void Valida_la_direccion_segun_el_tipo(string tipo, string d, bool valida) => Assert.Equal(valida, ServicioServicios.DireccionValida(tipo, d));

    [Theory]
    [InlineData(20, "menos de un minuto")] [InlineData(600, "10 min")] [InlineData(5400, "1 h 30 min")] [InlineData(100000, "1 d 3 h")]
    public void Dice_la_duracion_en_claro(int segundos, string texto) => Assert.Equal(texto, ServicioServicios.Duracion(TimeSpan.FromSeconds(segundos)));

    [Fact]
    public async Task Crear_comprobar_un_puerto_abierto_y_uno_cerrado_y_borrar()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var escucha = new TcpListener(IPAddress.Loopback, 0);
        escucha.Start();
        try
        {
            var puerto = ((IPEndPoint)escucha.LocalEndpoint).Port;
            var abierto = await (await c.PostAsJsonAsync("/api/servicios", new { nombre = "Abierto", tipo = "tcp", direccion = $"127.0.0.1:{puerto}" })).Content.ReadFromJsonAsync<JsonElement>();
            var r = await (await c.PostAsync($"/api/servicios/{abierto.GetProperty("id").GetInt32()}/comprobar", null)).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("ok", r.GetProperty("estado").GetString());
            Assert.NotEqual(JsonValueKind.Null, r.GetProperty("ms").ValueKind);
        }
        finally { escucha.Stop(); }

        var libre = new TcpListener(IPAddress.Loopback, 0); libre.Start(); var cerrado = ((IPEndPoint)libre.LocalEndpoint).Port; libre.Stop();   // un puerto que acaba de quedar libre
        var id = (await (await c.PostAsJsonAsync("/api/servicios", new { nombre = "Cerrado", tipo = "tcp", direccion = $"127.0.0.1:{cerrado}" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var mal = await (await c.PostAsync($"/api/servicios/{id}/comprobar", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual("ok", mal.GetProperty("estado").GetString());       // un fallo todavía no lo da por caído
        Assert.NotEqual(JsonValueKind.Null, mal.GetProperty("ultimoError").ValueKind);

        Assert.Equal(2, (await c.GetFromJsonAsync<JsonElement>("/api/servicios")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/servicios/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/servicios/{id}")).StatusCode);
    }

    [Fact]
    public async Task Validaciones_de_la_api()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/servicios", new { nombre = "", direccion = "https://ejemplo.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/servicios", new { nombre = "x", direccion = "no es una web" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/servicios", new { nombre = "x", tipo = "udp", direccion = "a:1" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/servicios/999", new { nombre = "y" })).StatusCode);
    }
}

public class CompraTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 6);

    [Theory]
    [InlineData("Leche", "leche")] [InlineData("  Leche   entera ", "leche entera")] [InlineData("Jamón York", "jamon york")] [InlineData("ÁÉÍÓÚ", "aeiou")]
    public void La_clave_ignora_mayusculas_tildes_y_espacios(string nombre, string clave) => Assert.Equal(clave, ServicioCompra.Clave(nombre));

    [Fact]
    public void Aprende_cada_cuanto_lo_compras_y_dice_cuando_toca()
    {
        var p = new ProductoHabitual { Clave = "leche", Nombre = "Leche" };
        ServicioCompra.Aprender(p, Hoy.AddDays(-21));
        ServicioCompra.Aprender(p, Hoy.AddDays(-14));
        Assert.False(ServicioCompra.Toca(p, Hoy));                  // solo 2 compras: aún no se fía
        ServicioCompra.Aprender(p, Hoy.AddDays(-7));
        Assert.Equal(3, p.Veces);
        Assert.Equal(7, p.IntervaloDias, 3);
        Assert.False(ServicioCompra.Toca(p, Hoy.AddDays(-2)));      // han pasado 5 días de 7
        Assert.True(ServicioCompra.Toca(p, Hoy.AddDays(-1)));       // 6 días ≥ 90 % de 7
        Assert.True(ServicioCompra.Toca(p, Hoy));
    }

    [Fact]
    public void Dos_compras_el_mismo_dia_cuentan_como_una()
    {
        var p = new ProductoHabitual { Clave = "pan", Nombre = "Pan" };
        ServicioCompra.Aprender(p, Hoy); ServicioCompra.Aprender(p, Hoy);
        Assert.Equal(1, p.Veces);
        Assert.Equal(0, p.IntervaloDias);
    }

    [Fact]
    public async Task Anadir_varias_marcar_y_ver_que_no_se_repite()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var e = await (await c.PostAsJsonAsync("/api/compra", new { nombre = "Leche, pan ; huevos" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, e.GetProperty("pendientes").GetArrayLength());
        var otra = await (await c.PostAsJsonAsync("/api/compra", new { nombre = "LECHE" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, otra.GetProperty("pendientes").GetArrayLength());       // la misma leche: no se duplica

        var id = e.GetProperty("pendientes")[0].GetProperty("id").GetInt32();
        var tras = await (await c.PutAsJsonAsync($"/api/compra/{id}", new { comprado = true })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, tras.GetProperty("pendientes").GetArrayLength());
        Assert.Equal(1, tras.GetProperty("comprados").GetArrayLength());
        Assert.Contains("Leche", tras.GetProperty("habituales").EnumerateArray().Select(x => x.GetString()));   // ya sabe de la leche
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/compra/habituales/leche")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/compra/9999", new { comprado = true })).StatusCode);
    }

    [Fact]
    public async Task Validaciones()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/compra", new { nombre = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/compra", new { nombre = new string('x', 61) })).StatusCode);
    }
}
