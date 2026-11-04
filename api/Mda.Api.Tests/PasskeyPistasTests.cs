using System.Text.Json.Nodes;
using Mda.Api.Auth;

namespace Mda.Api.Tests;

public class PasskeyPistasTests
{
    [Fact]
    public void Al_entrar_se_pide_la_llave_del_dispositivo_sin_tocar_el_resto()
    {
        var o = JsonNode.Parse(PasskeysEndpoints.ConPistas("""{"challenge":"abc","rpId":"x.ts.net"}""", registro: false))!.AsObject();
        Assert.Equal("client-device", (string?)o["hints"]![0]);
        Assert.Equal("abc", (string?)o["challenge"]);
        Assert.Null(o["authenticatorSelection"]);
    }

    [Fact]
    public void Al_registrar_se_exige_autenticador_de_plataforma_y_se_conserva_lo_que_ya_habia()
    {
        var o = JsonNode.Parse(PasskeysEndpoints.ConPistas("""{"authenticatorSelection":{"residentKey":"required"}}""", registro: true))!.AsObject();
        Assert.Equal("platform", (string?)o["authenticatorSelection"]!["authenticatorAttachment"]);
        Assert.Equal("required", (string?)o["authenticatorSelection"]!["residentKey"]);
    }

    [Fact]
    public void Si_el_json_no_vale_se_devuelve_tal_cual()
    {
        Assert.Equal("no es json", PasskeysEndpoints.ConPistas("no es json", registro: true));
    }
}
