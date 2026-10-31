using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mda.Api.Tests;

public class SenuelosFactory : ModulosFactory
{
    public string Base { get; } = Directory.CreateTempSubdirectory("mda-senuelos-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        Directory.CreateDirectory(Path.Combine(Base, "documentos", "Facturas"));
        File.WriteAllText(Path.Combine(Base, "documentos", "normal.txt"), "un archivo cualquiera");
        builder.UseSetting("Archivos:Base", Base);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(Base, true); } catch { /* temporal */ }
    }
}

public class SenuelosTests(SenuelosFactory factory) : IClassFixture<SenuelosFactory>
{
    private async Task<(HttpClient Cliente, int Id, string Nombre)> Preparar()
    {
        var c = await factory.Dentro();
        var s = await (await c.PostAsJsonAsync("/api/senuelos", new { plantilla = "contrasenas", carpeta = "" })).Content.ReadFromJsonAsync<JsonElement>();
        factory.Avisos.Enviados.Clear();
        return (c, s.GetProperty("id").GetInt32(), s.GetProperty("ruta").GetString()!);
    }

    private static string Url(string ruta) => $"/api/archivos/archivo?raiz=documentos&ruta={Uri.EscapeDataString(ruta)}";

    [Fact]
    public async Task Crear_un_senuelo_deja_un_archivo_con_datos_inventados()
    {
        var (c, id, nombre) = await Preparar();
        var texto = await File.ReadAllTextAsync(Path.Combine(factory.Base, "documentos", nombre));
        Assert.Contains("NO-ES-REAL", texto);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/senuelos/{id}")).StatusCode);
        Assert.False(File.Exists(Path.Combine(factory.Base, "documentos", nombre)));
    }

    [Fact]
    public async Task Ver_la_lista_o_abrir_un_archivo_normal_no_da_la_alarma()
    {
        var (c, _, _) = await Preparar();
        await c.GetAsync("/api/archivos/lista?raiz=documentos");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Url("normal.txt"))).StatusCode);
        Assert.DoesNotContain(factory.Avisos.Enviados, e => e.Contains("Señuelo"));
    }

    [Fact]
    public async Task Abrir_el_senuelo_avisa_sirve_el_falso_y_cierra_la_sesion()
    {
        var c = await factory.Dentro();
        var s = await (await c.PostAsJsonAsync("/api/senuelos", new { plantilla = "banco", carpeta = "Facturas" })).Content.ReadFromJsonAsync<JsonElement>();
        factory.Avisos.Enviados.Clear();
        var res = await c.GetAsync(Url(s.GetProperty("ruta").GetString()!));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode); // quien lo abre ve el archivo falso
        Assert.Contains("NO-ES-REAL", await res.Content.ReadAsStringAsync());
        Assert.Contains(factory.Avisos.Enviados, e => e.StartsWith("push:🚨 Señuelo abierto"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/sesion")).StatusCode); // y se queda sin sesión
    }

    [Fact]
    public async Task Un_senuelo_desactivado_no_avisa()
    {
        var (c, id, nombre) = await Preparar();
        await c.PutAsync($"/api/senuelos/{id}?activo=false", null);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Url(nombre))).StatusCode);
        Assert.DoesNotContain(factory.Avisos.Enviados, e => e.Contains("Señuelo"));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sesion")).StatusCode);
    }

    [Fact]
    public async Task Probar_avisa_sin_cerrar_la_sesion()
    {
        var (c, id, _) = await Preparar();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/senuelos/{id}/probar", null)).StatusCode);
        Assert.Contains(factory.Avisos.Enviados, e => e.StartsWith("push:🪤 Prueba del señuelo"));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sesion")).StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_no_se_gestionan_senuelos()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/senuelos")).StatusCode);
    }
}
