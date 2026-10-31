using Mda.Api.Avisos;

namespace Mda.Api.Tests;

public class PlantillaTests
{
    private const string Texto = "Se ha iniciado sesión en tu zona privada.\n\nEquipo: iPhone · Safari\nIP: 1.2.3.4\nHora: 31/10/2026 11:20:05\n\nSi no has sido tú, ciérrala desde Seguridad y cambia la contraseña.";

    [Fact]
    public void Los_avisos_salen_con_marca_boton_y_pie()
    {
        var html = Plantilla.Correo("🔐 Nuevo inicio de sesión", Plantilla.DeTexto(Texto), "https://zona.example/", "/seguridad", "Se ha iniciado sesión");
        var visto = System.Net.WebUtility.HtmlDecode(html);
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<h1", html);
        Assert.Contains(">Nuevo inicio de sesión</h1>", visto);   // el emoji va aparte, como icono
        Assert.Contains("🔐", visto);
        Assert.Contains("href=\"https://zona.example/seguridad\"", html);
        Assert.Contains("Zona privada", html);
        Assert.Contains("Seguridad › Avisos", visto);
        // las líneas «Clave: valor» se pintan como ficha y el texto se escapa
        Assert.Contains(">IP</td>", html);
        Assert.Contains(">iPhone · Safari</td>", visto);
        Assert.DoesNotContain("<script", Plantilla.DeTexto("<script>alert(1)</script>"));

        // para verlo en un navegador: MDA_CORREO_MUESTRA=ruta\muestra.html dotnet test --filter PlantillaTests
        if (Environment.GetEnvironmentVariable("MDA_CORREO_MUESTRA") is { Length: > 0 } f) File.WriteAllText(f, html);
    }

    [Fact]
    public void Sin_url_publica_no_hay_boton()
    {
        var html = Plantilla.Correo("Prueba", "<p>hola</p>", null, null);
        Assert.DoesNotContain("Abrir mi zona privada", html);
        Assert.Contains(">Prueba</h1>", html);
    }
}
