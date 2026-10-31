using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Mda.Api.Tests;

// Clase aparte (su propia API de prueba): activa la verificación en dos pasos del usuario
public class DosPasosTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Activarla_la_deja_activada()
    {
        var c = await factory.Dentro();
        var d = await (await c.PostAsJsonAsync("/api/auth/manage/2fa", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var clave = d.GetProperty("sharedKey").GetString()!;
        var r = await c.PostAsJsonAsync("/api/auth/manage/2fa", new { enable = true, twoFactorCode = Totp(clave) });
        r.EnsureSuccessStatusCode();

        var estado = await (await c.GetAsync("/api/seguridad/estado")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(estado.GetProperty("dosPasos").GetBoolean());
        // y al volver a entrar la pide
        var otro = factory.CreateClient();
        var sin = await otro.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password });
        Assert.False(sin.IsSuccessStatusCode);
        var con = await otro.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password, twoFactorCode = Totp(clave) });
        Assert.True(con.IsSuccessStatusCode);
    }

    // el código de 6 cifras que daría la app del móvil (RFC 6238, base32)
    private static string Totp(string base32)
    {
        const string abc = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32.TrimEnd('=').ToUpperInvariant().Select(ch => Convert.ToString(abc.IndexOf(ch), 2).PadLeft(5, '0')));
        var clave = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var paso = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(paso);
        var h = HMACSHA1.HashData(clave, paso);
        var o = h[^1] & 0xf;
        var n = ((h[o] & 0x7f) << 24 | h[o + 1] << 16 | h[o + 2] << 8 | h[o + 3]) % 1_000_000;
        return n.ToString("D6");
    }
}
