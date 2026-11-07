using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Pc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class TransferenciasLogicaTests
{
    [Theory]
    [InlineData("foto.jpg", "foto.jpg")]
    [InlineData("C:\\Users\\Mario\\foto.jpg", "foto.jpg")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("a<b>c:d.txt", "a_b_c_d.txt")]
    [InlineData("  .oculto.txt  ", "oculto.txt")]
    [InlineData("informe.", "informe")]
    [InlineData("CON.txt", "_CON.txt")]
    public void Los_nombres_quedan_en_un_nombre_de_archivo_sin_rutas(string entrada, string esperado) => Assert.Equal(esperado, Transferencias.NombreSeguro(entrada));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")] [InlineData("...")] [InlineData("a/")]
    public void Sin_nombre_utilizable_es_null(string? n) => Assert.Null(Transferencias.NombreSeguro(n));

    [Fact]
    public void Un_nombre_larguisimo_se_recorta_conservando_la_extension()
    {
        var n = Transferencias.NombreSeguro(new string('a', 400) + ".pdf")!;
        Assert.Equal(120, n.Length); Assert.EndsWith(".pdf", n);
    }

    [Theory]
    [InlineData("setup.exe", false, true)] [InlineData("SCRIPT.PS1", false, true)] [InlineData("acceso.lnk", false, true)] [InlineData("foto.jpg", false, false)] [InlineData("setup.exe", true, false)]
    public void Los_ejecutables_se_bloquean_salvo_que_se_permitan(string nombre, bool permitir, bool bloqueado) => Assert.Equal(bloqueado, Transferencias.Bloqueada(nombre, permitir));

    [Fact]
    public void Los_limites_de_tamano_y_de_almacen()
    {
        Assert.True(Transferencias.CabeAlmacen(0, 1));
        Assert.True(Transferencias.CabeAlmacen(0, Transferencias.Maximo));
        Assert.False(Transferencias.CabeAlmacen(0, Transferencias.Maximo + 1));
        Assert.False(Transferencias.CabeAlmacen(0, 0));
        Assert.False(Transferencias.CabeAlmacen(Transferencias.TopeAlmacen - 10, 11));
        Assert.True(Transferencias.CabeAlmacen(Transferencias.TopeAlmacen - 10, 10));
    }

    [Fact]
    public void Solo_los_de_mas_de_20_MB_piden_la_contraseña_otra_vez()
    {
        Assert.False(Transferencias.PideContrasena(20L * 1024 * 1024));
        Assert.True(Transferencias.PideContrasena(20L * 1024 * 1024 + 1));
    }
}

public class TransferenciasApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static MultipartFormDataContent Archivo(string nombre, byte[] datos) { var c = new MultipartFormDataContent(); c.Add(new ByteArrayContent(datos), "archivo", nombre); return c; }

    [Fact]
    public async Task No_se_envian_ejecutables_ni_cosas_sin_agente()
    {
        var c = await factory.Dentro();
        var mal = await c.PostAsync("/api/pc/bandeja/transferencias/enviar", Archivo("instalador.exe", [1, 2, 3]));
        Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);
        var sinAgente = await c.PostAsync("/api/pc/bandeja/transferencias/enviar", Archivo("nota.txt", Encoding.UTF8.GetBytes("hola")));
        Assert.Equal(HttpStatusCode.Conflict, sinAgente.StatusCode);       // el agente no está en marcha en las pruebas
    }

    [Fact]
    public async Task Lo_que_sube_el_agente_se_ofrece_se_baja_igual_y_se_borra_al_bajarlo()
    {
        var c = await factory.Dentro();
        int id;
        var contenido = Encoding.UTF8.GetBytes("contenido de prueba del PC");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            // lo mismo que hace la ruta del agente: cifrar y guardar
            var t = new Transferencia { Direccion = "movil", Nombre = "resumen.txt", Tamano = contenido.Length, Estado = "para-descargar", Creada = DateTimeOffset.UtcNow, Caduca = DateTimeOffset.UtcNow.AddHours(24), Archivo = Guid.NewGuid().ToString("N") + ".bin" };
            await File.WriteAllBytesAsync(Path.Combine(Transferencias.Almacen(config), t.Archivo), Mda.Api.Seguridad.Boveda.CifrarBytes(contenido, Transferencias.Proposito));
            db.Transferencias.Add(t); await db.SaveChangesAsync(); id = t.Id;
        }
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/pc/bandeja/transferencias");
        var e = lista.EnumerateArray().First(x => x.GetProperty("id").GetInt32() == id);
        Assert.True(e.GetProperty("descargable").GetBoolean());
        Assert.False(e.GetProperty("pideContrasena").GetBoolean());

        var bajado = await c.GetAsync($"/api/pc/bandeja/transferencias/{id}/archivo");
        Assert.Equal(HttpStatusCode.OK, bajado.StatusCode);
        Assert.Equal(contenido, await bajado.Content.ReadAsByteArrayAsync());
        Assert.Equal("resumen.txt", bajado.Content.Headers.ContentDisposition?.FileNameStar ?? bajado.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        // ya no está: se borra al bajarlo
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/pc/bandeja/transferencias/{id}/archivo")).StatusCode);
        var tras = (await c.GetFromJsonAsync<JsonElement>("/api/pc/bandeja/transferencias")).EnumerateArray().First(x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal("descargado", tras.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Lo_caducado_se_borra_del_almacen_y_se_marca()
    {
        var c = await factory.Dentro();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var fichero = Guid.NewGuid().ToString("N") + ".bin";
        var ruta = Path.Combine(Transferencias.Almacen(config), fichero);
        await File.WriteAllBytesAsync(ruta, [1, 2, 3]);
        var ahora = DateTimeOffset.UtcNow;
        db.Transferencias.Add(new Transferencia { Direccion = "movil", Nombre = "viejo.bin", Tamano = 3, Estado = "para-descargar", Creada = ahora.AddDays(-2), Caduca = ahora.AddDays(-1), Archivo = fichero });
        await db.SaveChangesAsync();

        await Transferencias.Limpiar(db, config, ahora, default);

        Assert.False(File.Exists(ruta));
        Assert.Equal("caducado", (await db.Transferencias.FirstAsync(t => t.Nombre == "viejo.bin")).Estado);
        Assert.Empty(await db.Transferencias.Where(t => t.Archivo == fichero).ToListAsync());
    }

    [Fact]
    public async Task El_agente_necesita_su_token_para_recoger_y_subir()
    {
        var anonimo = factory.CreateClient();
        Assert.True((await anonimo.GetAsync("/api/sistema/agente/bandeja/1")).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.True((await anonimo.PostAsync("/api/sistema/agente/bandeja/subir?nombre=x.txt", new ByteArrayContent([1]))).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/pc/bandeja/transferencias")).StatusCode);
    }
}
