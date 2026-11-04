using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mda.Api.Domotica;

namespace Mda.Api.Tests;

public class TuyaTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static (string, JsonElement) E(string code, string json) => (code, J(json));

    [Fact]
    public void La_firma_es_el_hmac_sha256_en_mayusculas_de_la_cadena_de_tuya()
    {
        var firma = FirmaTuya.Firmar("idcliente", "tok", "1700000000000", "abc", "get", "/v1.0/devices/x/status", "", "secreto");
        var vacio = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"; // sha256 de un cuerpo vacío
        Assert.Equal(vacio, FirmaTuya.Hash(""));
        var esperado = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secreto"),
            Encoding.UTF8.GetBytes("idcliente" + "tok" + "1700000000000" + "abc" + $"GET\n{vacio}\n\n/v1.0/devices/x/status")));
        Assert.Equal(esperado, firma);
        Assert.Equal(firma.ToUpperInvariant(), firma);
    }

    [Fact]
    public void El_token_se_firma_sin_token_y_el_cuerpo_cambia_la_firma()
    {
        var a = FirmaTuya.Firmar("c", null, "1", "n", "GET", "/v1.0/token?grant_type=1", "", "s");
        var b = FirmaTuya.Firmar("c", "t", "1", "n", "GET", "/v1.0/token?grant_type=1", "", "s");
        var c = FirmaTuya.Firmar("c", "t", "1", "n", "POST", "/x", "{\"a\":1}", "s");
        var d = FirmaTuya.Firmar("c", "t", "1", "n", "POST", "/x", "{\"a\":2}", "s");
        Assert.NotEqual(a, b);
        Assert.NotEqual(c, d);
    }

    [Fact]
    public void El_estado_de_un_sensor_lamapara_y_enchufe_se_traduce_al_catalogo()
    {
        var sensor = TraduccionTuya.Traducir("sensor", [E("va_temperature", "163"), E("va_humidity", "55")]);
        Assert.Equal("16.3", sensor["temperatura"]);
        Assert.Equal("55", sensor["humedad"]);

        var luz = TraduccionTuya.Traducir("luz", [E("switch_led", "true"), E("bright_value_v2", "500"), E("colour_data_v2", "\"{\\\"h\\\":0,\\\"s\\\":1000,\\\"v\\\":1000}\"")]);
        Assert.Equal("true", luz["encendido"]);
        Assert.Equal("50", luz["brillo"]);
        Assert.Equal("#ff0000", luz["color"]);

        var enchufe = TraduccionTuya.Traducir("enchufe", [E("switch_1", "false"), E("cur_power", "125")]);
        Assert.Equal("false", enchufe["encendido"]);
        Assert.Equal("13", enchufe["potencia"]);   // 12,5 W redondeado

        Assert.Equal("true", TraduccionTuya.Traducir("puerta", [E("doorcontact_state", "true")])["abierto"]);
        Assert.Empty(TraduccionTuya.Traducir("sensor", [E("codigo_raro", "1")]));
    }

    [Theory]
    [InlineData("#ff0000", 0, 1000, 1000)]
    [InlineData("#00ff00", 120, 1000, 1000)]
    [InlineData("#0000ff", 240, 1000, 1000)]
    [InlineData("#ffffff", 0, 0, 1000)]
    public void El_color_va_y_viene_entre_hexadecimal_y_hsv(string hex, int h, int s, int v)
    {
        Assert.Equal((h, s, v), TraduccionTuya.HexAHsv(hex));
        Assert.Equal(hex, TraduccionTuya.HsvAHex(h, s, v));
    }

    [Fact]
    public void Las_ordenes_de_la_lampara_y_del_enchufe_son_codigos_de_tuya()
    {
        var enciende = TraduccionTuya.Comandos("luz", "encendido", "true")!;
        Assert.Equal("switch_led", enciende[0]["code"]);
        Assert.Equal(true, enciende[0]["value"]);
        Assert.Equal(350, TraduccionTuya.Comandos("luz", "brillo", "35")![0]["value"]);
        Assert.Equal(10, TraduccionTuya.Comandos("luz", "brillo", "0.2")![0]["value"]);   // Tuya no baja de 10
        Assert.Equal("colour", TraduccionTuya.Comandos("luz", "color", "#ff0000")![0]["value"]);
        Assert.Equal("switch_1", TraduccionTuya.Comandos("enchufe", "encendido", "true")![0]["code"]);
        Assert.Null(TraduccionTuya.Comandos("tv", "encendido", "true"));
    }

    [Fact]
    public void El_aire_por_infrarrojos_manda_encendido_modo_y_temperatura()
    {
        var o = TraduccionTuya.OrdenAire(new Dictionary<string, string> { ["encendido"] = "true", ["objetivo"] = "23.5", ["modo"] = "frio" });
        Assert.Equal(1, o["power"]);
        Assert.Equal(0, o["mode"]);
        Assert.Equal(24, o["temp"]);
        var eco = TraduccionTuya.OrdenAire(new Dictionary<string, string> { ["encendido"] = "true", ["objetivo"] = "22", ["modo"] = "eco" });
        Assert.Equal(26, eco["temp"]);
        Assert.Equal(0, TraduccionTuya.OrdenAire(new Dictionary<string, string> { ["encendido"] = "false" })["power"]);
    }

    [Fact]
    public void La_configuracion_del_aparato_va_y_viene_en_json_y_tolera_basura()
    {
        var c = new ConfigTuya("abc123", "ir1", "aire1");
        Assert.Equal(c, ConfigTuya.Leer(c.Json()));
        Assert.Null(ConfigTuya.Leer("{no es json").DeviceId);
        Assert.Null(ConfigTuya.Leer("").DeviceId);
    }
}
