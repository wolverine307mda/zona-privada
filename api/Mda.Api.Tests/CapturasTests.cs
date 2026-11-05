using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Pc;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Mda.Api.Tests;

public class CapturasTests
{
    private static AppDbContext Db() => BaseDeDatos.Nueva();

    // un JPEG de verdad (con Skia) de cualquier tamaño
    public static byte[] Jpeg(int ancho, int alto)
    {
        using var bmp = new SKBitmap(ancho, alto);
        bmp.Erase(new SKColor(30, 120, 200));
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider { public DateTimeOffset Ahora { get; set; } = ahora; public override DateTimeOffset GetUtcNow() => Ahora; }

    // ---- cifrado ----

    [Fact]
    public void Los_bytes_se_cifran_y_descifran_y_no_se_leen_sin_la_clave_ni_con_otro_proposito()
    {
        var datos = Jpeg(40, 30);
        var cifrado = Boveda.CifrarBytes(datos, Capturas.Proposito);
        Assert.NotEqual(datos, cifrado[28..]);
        Assert.Equal(datos.Length + 28, cifrado.Length);
        Assert.Equal(datos, Boveda.DescifrarBytes(cifrado, Capturas.Proposito));
        Assert.ThrowsAny<CryptographicException>(() => Boveda.DescifrarBytes(cifrado, "otra-cosa"));      // cada propósito tiene su subclave
        cifrado[40] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => Boveda.DescifrarBytes(cifrado, Capturas.Proposito)); // y si alguien lo toca, se nota
        Assert.ThrowsAny<CryptographicException>(() => Boveda.DescifrarBytes([1, 2, 3], Capturas.Proposito));
    }

    [Fact]
    public void Cada_cifrado_usa_un_nonce_distinto()
    {
        var d = Jpeg(10, 10);
        Assert.NotEqual(Boveda.CifrarBytes(d, Capturas.Proposito), Boveda.CifrarBytes(d, Capturas.Proposito));
    }

    // ---- la imagen ----

    [Fact]
    public void Normalizar_reduce_a_1280_quita_metadatos_y_rechaza_lo_que_no_es_imagen()
    {
        var (jpeg, ancho, alto) = Capturas.Normalizar(Jpeg(3000, 2000))!.Value;
        Assert.Equal(1280, ancho);
        Assert.Equal(853, alto);
        Assert.True(jpeg.Length < 200_000);
        var (_, a2, h2) = Capturas.Normalizar(Jpeg(400, 300))!.Value;                    // no se agranda
        Assert.Equal((400, 300), (a2, h2));
        Assert.Null(Capturas.Normalizar([1, 2, 3, 4]));
        Assert.Null(Capturas.Normalizar([]));
        Assert.Null(Capturas.Normalizar(new byte[Capturas.MaxBytesEntrada + 1]));
    }

    // ---- la dirección de la cámara IP: solo red local ----

    [Theory]
    [InlineData("http://192.168.1.50/snapshot.jpg", true)]
    [InlineData("http://10.0.0.7:8080/foto", true)]
    [InlineData("https://172.16.4.2/img", true)]
    [InlineData("http://172.31.255.1/x", true)]
    [InlineData("http://169.254.1.1/x", true)]
    [InlineData("http://127.0.0.1:9000/x", true)]
    [InlineData("http://admin:clave@192.168.1.50/snap.jpg", true)]
    [InlineData("http://[fd00::1]/x", true)]
    [InlineData("http://8.8.8.8/x", false)]                 // IP pública
    [InlineData("http://172.32.0.1/x", false)]              // justo fuera del rango privado
    [InlineData("http://camara.local/x", false)]            // nombres, no
    [InlineData("http://localhost/x", false)]
    [InlineData("ftp://192.168.1.50/x", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("192.168.1.50/x", false)]
    [InlineData("", false)]
    public void La_camara_ip_solo_acepta_direcciones_ip_de_la_red_local(string url, bool valida) => Assert.Equal(valida, Capturas.UrlCamaraIpValida(url, out _));

    [Theory]
    [InlineData("pantalla", true, "pantalla", "manual")]
    [InlineData("camara:intruso", true, "camara", "intruso")]
    [InlineData("camara:panico", true, "camara", "panico")]
    [InlineData("camara-ip", false, "camara-ip", "manual")]  // la IP no la hace el agente
    [InlineData("pantalla:otro", false, "pantalla", "otro")]
    [InlineData("pantalla:manual:extra", false, "pantalla", "manual")]
    [InlineData("", false, "", "manual")]
    public void El_parametro_de_la_orden_se_valida(string p, bool valido, string origen, string motivo)
    {
        Assert.Equal(valido, Capturas.ParametroValido(p, out var o, out var m));
        Assert.Equal((origen, motivo), (o, m));
    }

    // ---- guardar y borrar solas ----

    [Fact]
    public async Task Guarda_cifrado_y_borra_lo_que_pasa_de_los_dias()
    {
        using var db = Db();
        var reloj = new Reloj(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
        await Capturas.Poner(db, Capturas.ClaveDias, "7");
        await db.SaveChangesAsync();
        var vieja = await Capturas.Guardar(db, reloj, "pantalla", "manual", Jpeg(100, 80));
        reloj.Ahora = reloj.Ahora.AddDays(8);
        var nueva = await Capturas.Guardar(db, reloj, "camara", "panico", Jpeg(100, 80));
        Assert.NotNull(nueva);
        Assert.Equal([nueva!.Id], await db.CapturasPc.Select(c => c.Id).ToListAsync());      // la de hace 8 días ya no está
        Assert.NotNull(vieja);
        var guardada = await db.CapturasPc.SingleAsync();
        Assert.True(Boveda.DescifrarBytes(guardada.Datos, Capturas.Proposito).AsSpan(0, 2).SequenceEqual(new byte[] { 0xFF, 0xD8 }));
        Assert.False(guardada.Datos.AsSpan(28, 2).SequenceEqual(new byte[] { 0xFF, 0xD8 }));   // en la base no se ve un JPEG
    }

    [Fact]
    public async Task No_guarda_lo_que_no_es_una_imagen_ni_un_origen_raro()
    {
        using var db = Db();
        var reloj = new Reloj(DateTimeOffset.UtcNow);
        Assert.Null(await Capturas.Guardar(db, reloj, "pantalla", "manual", [1, 2, 3]));
        Assert.Null(await Capturas.Guardar(db, reloj, "../etc", "manual", Jpeg(10, 10)));
        Assert.Null(await Capturas.Guardar(db, reloj, "pantalla", "inventado", Jpeg(10, 10)));
        Assert.Empty(await db.CapturasPc.ToListAsync());
    }

    // ---- disparadores ----

    [Fact]
    public async Task El_panico_hace_fotos_solo_si_esta_todo_activado()
    {
        using var db = Db();
        var reloj = new Reloj(DateTimeOffset.UtcNow);
        await Capturas.EnPanico(db, reloj);
        Assert.Empty(await db.OrdenesPc.ToListAsync());                                          // apagado por defecto
        await Capturas.Poner(db, Capturas.ClavePanico, "si"); await db.SaveChangesAsync();
        await Capturas.EnPanico(db, reloj);
        Assert.Empty(await db.OrdenesPc.ToListAsync());                                          // falta el interruptor general
        await Capturas.Poner(db, Capturas.ClaveActivo, "si"); await db.SaveChangesAsync();
        await Capturas.EnPanico(db, reloj);
        Assert.Equal(["camara:panico", "pantalla:panico"], (await db.OrdenesPc.Select(o => o.Parametro).ToListAsync()).Order().ToArray());
        Assert.All(await db.OrdenesPc.ToListAsync(), o => Assert.Equal("captura", o.Tipo));
    }

    [Fact]
    public async Task Tras_tres_fallos_seguidos_hace_una_foto_y_no_otra_en_diez_minutos()
    {
        using var db = Db();
        var ahora = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var reloj = new Reloj(ahora);
        await Capturas.Poner(db, Capturas.ClaveActivo, "si"); await Capturas.Poner(db, Capturas.ClaveIntruso, "si");
        for (var i = 0; i < 2; i++) db.Accesos.Add(new Acceso { Fecha = ahora.AddMinutes(-i), Exito = false, Ip = "1.1.1.1" });
        await db.SaveChangesAsync();
        await Capturas.TrasFallo(db, reloj);
        Assert.Empty(await db.OrdenesPc.ToListAsync());                                          // solo dos fallos
        db.Accesos.Add(new Acceso { Fecha = ahora, Exito = false, Ip = "1.1.1.1" }); await db.SaveChangesAsync();
        await Capturas.TrasFallo(db, reloj);
        Assert.Equal("camara:intruso", (await db.OrdenesPc.SingleAsync()).Parametro);
        db.Accesos.Add(new Acceso { Fecha = ahora, Exito = false, Ip = "1.1.1.1" }); await db.SaveChangesAsync();
        await Capturas.TrasFallo(db, reloj);
        Assert.Single(await db.OrdenesPc.ToListAsync());                                         // otra foto no hasta pasados diez minutos
    }

    [Fact]
    public async Task Los_fallos_viejos_no_cuentan()
    {
        using var db = Db();
        var ahora = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        await Capturas.Poner(db, Capturas.ClaveActivo, "si"); await Capturas.Poner(db, Capturas.ClaveIntruso, "si");
        for (var i = 0; i < 5; i++) db.Accesos.Add(new Acceso { Fecha = ahora.AddHours(-2), Exito = false, Ip = "1.1.1.1" });
        await db.SaveChangesAsync();
        await Capturas.TrasFallo(db, new Reloj(ahora));
        Assert.Empty(await db.OrdenesPc.ToListAsync());
    }

    // ---- la API ----

    [Fact]
    public async Task Sin_sesion_no_hay_nada_y_con_sesion_las_fotos_empiezan_apagadas()
    {
        using var f = new ModulosFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/pc/capturas")).StatusCode);
        var c = await f.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/pc/capturas");
        Assert.False(j.GetProperty("ajustes").GetProperty("activo").GetBoolean());
        Assert.Equal(0, j.GetProperty("capturas").GetArrayLength());
        // apagado: ni una foto a petición
        var r = await c.PostAsJsonAsync("/api/pc/capturas/capturar", new { origen = "pantalla" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Cambiar_los_ajustes_pide_confirmar_que_eres_tu()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var r = await c.PutAsJsonAsync("/api/pc/capturas/ajustes", new { activo = true });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reautenticar").GetBoolean());
    }

    private static HttpRequestMessage DelAgente(byte[] cuerpo, string? token, string consulta = "origen=pantalla&motivo=manual")
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/sistema/agente/captura?" + consulta) { Content = new ByteArrayContent(cuerpo) };
        if (token is not null) r.Headers.Add("X-Agente-Token", token);
        return r;
    }

    [Fact]
    public async Task El_agente_necesita_su_token_y_las_fotos_encendidas_para_dejar_una()
    {
        using var f = new ModulosFactory();
        var cliente = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.SendAsync(DelAgente(Jpeg(50, 40), null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.SendAsync(DelAgente(Jpeg(50, 40), "otro"))).StatusCode);
        // con el token pero con las fotos apagadas (lo normal): nada
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.SendAsync(DelAgente(Jpeg(50, 40), ApiFactory.Token))).StatusCode);
        using (var s = f.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            await Capturas.Poner(db, Capturas.ClaveActivo, "si");
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(DelAgente(Jpeg(50, 40), ApiFactory.Token, "origen=camara&motivo=intruso"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.SendAsync(DelAgente([1, 2, 3], ApiFactory.Token))).StatusCode);                          // no es una imagen
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.SendAsync(DelAgente(Jpeg(50, 40), ApiFactory.Token, "origen=otra&motivo=manual"))).StatusCode);
        using var s2 = f.Services.CreateScope();
        var guardadas = await s2.ServiceProvider.GetRequiredService<AppDbContext>().CapturasPc.ToListAsync();
        Assert.Single(guardadas);
        Assert.Equal(("camara", "intruso"), (guardadas[0].Origen, guardadas[0].Motivo));
    }

    [Fact]
    public async Task Las_fotos_se_ven_solo_con_sesion_descifradas_y_se_borran()
    {
        using var f = new ModulosFactory();
        int id;
        using (var s = f.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            id = (await Capturas.Guardar(db, TimeProvider.System, "pantalla", "manual", Jpeg(64, 48)))!.Id;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync($"/api/pc/capturas/{id}/imagen")).StatusCode);
        var c = await f.Dentro();
        var r = await c.GetAsync($"/api/pc/capturas/{id}/imagen");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/jpeg", r.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-store", r.Headers.CacheControl!.ToString());
        var bytes = await r.Content.ReadAsByteArrayAsync();
        using var bmp = SKBitmap.Decode(bytes);
        Assert.Equal((64, 48), (bmp.Width, bmp.Height));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/pc/capturas/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/pc/capturas/{id}/imagen")).StatusCode);
    }
}
