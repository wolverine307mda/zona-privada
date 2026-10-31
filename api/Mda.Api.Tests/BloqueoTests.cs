using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class BloqueoTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private async Task Cambiar(Action<Mda.Api.Seguridad.SesionAbierta> f)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var s in await db.Sesiones.Where(s => s.Cerrada == null).ToListAsync()) f(s);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sin_activar_el_bloqueo_la_sesion_nunca_se_bloquea()
    {
        var c = await factory.Dentro();
        await c.PostAsync("/api/sesion/segundo-plano", null);
        await Cambiar(s => s.UltimaVez = DateTimeOffset.UtcNow.AddHours(-5));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sistema")).StatusCode);
    }

    [Fact]
    public async Task Volver_enseguida_no_bloquea_y_volver_tarde_si()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/sesion/bloqueo", new { segundos = 30 })).StatusCode);

        // sale de la app y vuelve a los pocos segundos: sigue abierta
        await c.PostAsync("/api/sesion/segundo-plano", null);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sistema")).StatusCode);

        // sale y vuelve pasado el umbral: bloqueada
        await c.PostAsync("/api/sesion/segundo-plano", null);
        await Cambiar(s => s.SegundoPlano = DateTimeOffset.UtcNow.AddSeconds(-60));
        var bloqueado = await c.GetAsync("/api/sistema");
        Assert.Equal(HttpStatusCode.Locked, bloqueado.StatusCode);
        Assert.True((await bloqueado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("bloqueada").GetBoolean());
    }

    [Fact]
    public async Task Bloqueada_solo_contesta_lo_de_la_sesion_y_se_desbloquea_con_la_contrasena()
    {
        var c = await factory.Dentro();
        await c.PutAsJsonAsync("/api/sesion/bloqueo", new { segundos = 30 });
        await c.PostAsync("/api/sesion/segundo-plano", null);
        await Cambiar(s => s.SegundoPlano = DateTimeOffset.UtcNow.AddSeconds(-60));

        foreach (var ruta in new[] { "/api/gastos/movimientos", "/api/notas", "/api/archivos/raices", "/api/pc", "/api/galeria/fotos", "/hubs/sistema/negotiate" })
            Assert.Equal(HttpStatusCode.Locked, (c.PostAsync(ruta, null).Result).StatusCode == HttpStatusCode.Locked ? HttpStatusCode.Locked : (await c.GetAsync(ruta)).StatusCode);
        // la sesión sigue ahí y dice que está bloqueada
        var yo = await c.GetFromJsonAsync<JsonElement>("/api/sesion");
        Assert.True(yo.GetProperty("bloqueada").GetBoolean());
        // desde bloqueada no se puede quitar el bloqueo ni cambiarlo
        Assert.Equal(HttpStatusCode.Locked, (await c.PutAsJsonAsync("/api/sesion/bloqueo", new { segundos = (int?)null })).StatusCode);

        // contraseña mala: sigue bloqueada
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/sesion/desbloqueo-clave", new { password = "mala-mala-mala" })).StatusCode);
        Assert.Equal(HttpStatusCode.Locked, (await c.GetAsync("/api/notas")).StatusCode);
        // contraseña buena: se abre
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/sesion/desbloqueo-clave", new { password = ApiFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/notas")).StatusCode);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/sesion")).GetProperty("bloqueada").GetBoolean());
    }

    [Fact]
    public async Task Si_la_app_se_cierra_sin_avisar_el_silencio_largo_tambien_bloquea()
    {
        var c = await factory.Dentro();
        await c.PutAsJsonAsync("/api/sesion/bloqueo", new { segundos = 10 });
        await Cambiar(s => s.UltimaVez = DateTimeOffset.UtcNow.AddMinutes(-10)); // sin latidos desde hace 10 minutos
        Assert.Equal(HttpStatusCode.Locked, (await c.GetAsync("/api/notas")).StatusCode);
    }

    [Fact]
    public async Task Aflojar_el_bloqueo_pide_la_contrasena_otra_vez_y_endurecerlo_no()
    {
        var c = await factory.Dentro();
        await c.PutAsJsonAsync("/api/sesion/bloqueo", new { segundos = 30 });
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/sesion/bloqueo", new { segundos = 10 })).StatusCode); // más estricto: sin pedir nada
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync("/api/sesion/bloqueo", new { segundos = (int?)null })).StatusCode);
        await c.PostAsJsonAsync("/api/seguridad/reautenticar", new { password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/sesion/bloqueo", new { segundos = (int?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/sesion/bloqueo", new { segundos = 1 })).StatusCode);
    }

    [Fact]
    public async Task El_desbloqueo_con_passkey_pide_sesion_y_un_passkey_falso_no_abre()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/sesion/desbloqueo/opciones", null)).StatusCode);
        var c = await factory.Dentro();
        var opciones = await c.PostAsync("/api/sesion/desbloqueo/opciones", null);
        Assert.Equal(HttpStatusCode.OK, opciones.StatusCode);
        Assert.Contains("challenge", await opciones.Content.ReadAsStringAsync());
        var falso = await c.PostAsJsonAsync("/api/sesion/desbloqueo", new { credencial = new { id = "AAAA", type = "public-key", response = new { } } });
        Assert.NotEqual(HttpStatusCode.NoContent, falso.StatusCode);
    }
}
