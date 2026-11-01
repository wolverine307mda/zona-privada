using System.Net;
using System.Net.Http.Json;
using Mda.Api.Auth;
using Mda.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

// La base en memoria no sabe guardar el JSON de un passkey de Identity, así que aquí no se siembran passkeys de verdad:
// lo que depende de leerlos (lista, renombrar, quitar) se prueba contra PostgreSQL real (PostgresTests).
public class PasskeysInfoTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private const string Windows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36";
    private const string Iphone = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1";

    private HttpContext Contexto(string ua)
    {
        var ctx = new DefaultHttpContext { RequestServices = factory.Services };
        ctx.Request.Headers.UserAgent = ua;
        return ctx;
    }

    [Fact]
    public async Task El_alta_guarda_la_ip_el_dispositivo_y_lo_que_manda_el_navegador_y_los_usos_se_anotan()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cred = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var id = PasskeyInfos.IdDe(cred);

        await PasskeyInfos.Alta(db, Contexto(Iphone), "u1", cred, new DispositivoCliente("es-ES", "Europe/Madrid", "390x844"), TimeProvider.System);
        var alta = await db.PasskeysInfo.FindAsync(id);
        Assert.Equal("iPhone · Safari", alta!.Dispositivo);
        Assert.Equal("movil", alta.Tipo);
        Assert.Equal("es-ES", alta.Idioma);
        Assert.Equal("Europe/Madrid", alta.Zona);
        Assert.Equal("390x844", alta.Pantalla);
        Assert.Equal(0, alta.Usos);

        await PasskeyInfos.Uso(db, Contexto(Windows), id, "confirmación", TimeProvider.System);
        var uso = await db.PasskeysInfo.FindAsync(id);
        Assert.Equal(1, uso!.Usos);
        Assert.Equal("confirmación", uso.UltimoMotivo);
        Assert.Equal("Windows · Chrome", uso.UltimoDispositivo);
        Assert.NotNull(uso.UltimoUso);

        await PasskeyInfos.Quitar(db, id);
        Assert.Null(await db.PasskeysInfo.FindAsync(id));
    }

    [Fact]
    public void El_id_se_codifica_en_base64_de_URL_y_se_decodifica_tambien_el_antiguo()
    {
        byte[] cred = [251, 255, 190, 1, 2, 3];
        var id = PasskeyInfos.IdDe(cred);
        Assert.DoesNotContain("+", id);
        Assert.DoesNotContain("/", id);
        Assert.DoesNotContain("=", id);
        Assert.Equal(cred, PasskeyInfos.Decodificar(id));
        Assert.Equal(cred, PasskeyInfos.Decodificar(Convert.ToBase64String(cred))); // el formato que devolvía la versión anterior
        Assert.Null(PasskeyInfos.Decodificar("***"));
    }

    [Fact]
    public async Task Renombrar_valida_el_nombre_y_lo_que_no_existe_da_404()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/passkeys/AAAA", new { nombre = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/passkeys/AAAA", new { nombre = new string('x', 41) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/passkeys/AAAA", new { nombre = "Portátil" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PutAsJsonAsync("/api/passkeys/AAAA", new { nombre = "x" })).StatusCode);
    }

    [Fact]
    public async Task Confirmar_con_passkey_pide_sesion_y_uno_falso_no_vale()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/seguridad/reautenticar/passkey/opciones", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/seguridad/reautenticar/passkey", new { credencial = new { id = "AAAA" } })).StatusCode);
        var c = await factory.Dentro();
        var opciones = await c.PostAsync("/api/seguridad/reautenticar/passkey/opciones", null);
        Assert.Equal(HttpStatusCode.OK, opciones.StatusCode);
        Assert.Contains("challenge", await opciones.Content.ReadAsStringAsync());
        var falso = await c.PostAsJsonAsync("/api/seguridad/reautenticar/passkey", new { credencial = new { id = "AAAA", type = "public-key", response = new { } } });
        Assert.NotEqual(HttpStatusCode.NoContent, falso.StatusCode);
    }
}
