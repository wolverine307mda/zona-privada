using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Conexiones;

namespace Mda.Api.Tests;

public class GoogleCuentasUnitTests
{
    private static CalendarioGoogle Cal(string id, bool principal = false, bool escribible = true) => new(id, id, null, principal, escribible);

    private static readonly IReadOnlyList<CuentaGoogle> Cuentas =
    [
        new("google", "yo@gmail.com", [Cal("yo@gmail.com", true), Cal("trabajo"), Cal("festivos", escribible: false)]),
        new("google-a1b2c3", "pareja@gmail.com", [Cal("pareja@gmail.com", true), Cal("casa")]),
    ];

    [Fact]
    public void Sin_ajustes_se_ve_solo_el_calendario_principal_de_cada_cuenta()
    {
        Assert.Equal(["yo@gmail.com"], GoogleAjustes.Mostrados(Cuentas[0], AjustesGoogle.Vacios).Select(c => c.Id));
        Assert.Equal(["pareja@gmail.com"], GoogleAjustes.Mostrados(Cuentas[1], AjustesGoogle.Vacios).Select(c => c.Id));
    }

    [Fact]
    public void Si_has_elegido_calendarios_se_ven_esos()
    {
        var a = new AjustesGoogle(null, new() { ["google"] = ["trabajo", "festivos"] });
        Assert.Equal(["trabajo", "festivos"], GoogleAjustes.Mostrados(Cuentas[0], a).Select(c => c.Id));
        Assert.Equal(["pareja@gmail.com"], GoogleAjustes.Mostrados(Cuentas[1], a).Select(c => c.Id)); // la otra cuenta no se toca
    }

    [Fact]
    public void Un_evento_nuevo_va_al_calendario_por_defecto()
    {
        var a = new AjustesGoogle(new DestinoGoogle("google-a1b2c3", "casa"), []);
        Assert.Equal(new DestinoGoogle("google-a1b2c3", "casa"), GoogleAjustes.Destino(null, null, a, Cuentas));
    }

    [Fact]
    public void Sin_defecto_va_al_principal_de_la_primera_cuenta()
        => Assert.Equal(new DestinoGoogle("google", "yo@gmail.com"), GoogleAjustes.Destino(null, null, AjustesGoogle.Vacios, Cuentas));

    [Fact]
    public void Lo_que_pides_manda_sobre_el_defecto_si_existe_y_se_puede_escribir()
    {
        var a = new AjustesGoogle(new DestinoGoogle("google", "trabajo"), []);
        Assert.Equal(new DestinoGoogle("google-a1b2c3", "casa"), GoogleAjustes.Destino("google-a1b2c3", "casa", a, Cuentas));
        // una cuenta sin calendario concreto: el principal de esa cuenta
        Assert.Equal(new DestinoGoogle("google-a1b2c3", "pareja@gmail.com"), GoogleAjustes.Destino("google-a1b2c3", null, a, Cuentas));
        // un calendario de solo lectura o que no existe no vale: se usa el defecto
        Assert.Equal(new DestinoGoogle("google", "trabajo"), GoogleAjustes.Destino("google", "festivos", a, Cuentas));
        Assert.Equal(new DestinoGoogle("google", "trabajo"), GoogleAjustes.Destino("google-nada", "x", a, Cuentas));
    }

    [Fact]
    public void Si_el_defecto_ya_no_existe_se_cae_al_principal_y_sin_cuentas_no_hay_destino()
    {
        var a = new AjustesGoogle(new DestinoGoogle("google-quitada", "x"), []);
        Assert.Equal(new DestinoGoogle("google", "yo@gmail.com"), GoogleAjustes.Destino(null, null, a, Cuentas));
        Assert.Null(GoogleAjustes.Destino(null, null, a, []));
    }

    [Fact]
    public void Los_ajustes_solo_aceptan_cuentas_y_calendarios_que_existen()
    {
        Assert.Null(GoogleAjustes.Validar(new AjustesGoogle(new DestinoGoogle("google", "trabajo"), new() { ["google"] = ["trabajo"] }), Cuentas));
        Assert.NotNull(GoogleAjustes.Validar(new AjustesGoogle(null, new() { ["google-otra"] = ["x"] }), Cuentas));
        Assert.NotNull(GoogleAjustes.Validar(new AjustesGoogle(null, new() { ["google"] = ["no-existe"] }), Cuentas));
        Assert.NotNull(GoogleAjustes.Validar(new AjustesGoogle(new DestinoGoogle("google", "festivos"), []), Cuentas)); // solo lectura: no se puede escribir
    }

    [Theory]
    [InlineData("google", true)]
    [InlineData("google-a1b2c3", true)]
    [InlineData("spotify", true)]
    [InlineData("google-", false)]
    [InlineData("spotify-a1b2c3", false)]
    [InlineData("otra", false)]
    public void Las_claves_de_las_cuentas_enlazadas_se_reconocen(string clave, bool existe) => Assert.Equal(existe, ServicioConexiones.Por(clave) is not null);

    [Fact]
    public void Una_clave_de_google_con_sufijo_es_de_google_y_no_de_spotify()
    {
        Assert.Same(ServicioConexiones.Google, ServicioConexiones.Por("google-a1b2c3"));
        Assert.True(ServicioConexiones.EsDe(ServicioConexiones.Google, "google-a1b2c3"));
        Assert.False(ServicioConexiones.EsDe(ServicioConexiones.Spotify, "google-a1b2c3"));
    }
}

public class GoogleCuentasTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static Task<HttpClient>? _entrada;
    private Task<HttpClient> Dentro() => _entrada ??= factory.Dentro();

    [Fact]
    public async Task El_estado_de_la_agenda_enseña_las_cuentas_aunque_no_haya_ninguna()
    {
        var c = await Dentro();
        var g = (await c.GetFromJsonAsync<JsonElement>("/api/agenda/estado")).GetProperty("google");
        Assert.False(g.GetProperty("conectado").GetBoolean());
        Assert.Equal(0, g.GetProperty("cuentas").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, g.GetProperty("defecto").ValueKind);
    }

    [Fact]
    public async Task Los_ajustes_de_Google_piden_sesion_y_rechazan_cuentas_que_no_existen()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PutAsJsonAsync("/api/agenda/google", new { mostrar = new { } })).StatusCode);
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/agenda/google", new { defecto = (object?)null, mostrar = new { } })).StatusCode);
        var mal = await c.PutAsJsonAsync("/api/agenda/google", new { defecto = (object?)null, mostrar = new Dictionary<string, string[]> { ["google"] = ["x"] } });
        Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);
    }

    [Fact]
    public async Task Un_evento_con_copia_en_Google_sin_cuentas_se_guarda_solo_aqui_y_avisa()
    {
        var c = await Dentro();
        var r = await c.PostAsJsonAsync("/api/agenda", new
        {
            tipo = 0, titulo = "Dentista", inicio = DateTimeOffset.UtcNow.AddDays(2), todoElDia = false, color = "", repetir = 0,
            porCorreo = false, porWhatsApp = false, enGoogle = true, googleCuenta = "google-a1b2c3", googleCalendario = "casa",
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Contains("no está enlazado", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("googleError").GetString());
    }

    [Fact]
    public async Task Quitar_una_cuenta_que_no_existe_no_rompe_y_una_clave_rara_es_404()
    {
        var c = await Dentro();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/conexiones/google-a1b2c3")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/conexiones/google-")).StatusCode);
    }
}
