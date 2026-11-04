using Mda.Api.Archivos;
using Mda.Api.Avisos;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mda.Api.Tests;

// Lo que necesita la versión de escritorio de la API sin romper el comportamiento de Docker
public class EscritorioApiTests
{
    private static IConfiguration Conf(Dictionary<string, string?> d) => new ConfigurationBuilder().AddInMemoryCollection(d).Build();

    [Fact]
    public void Sin_rutas_propias_las_carpetas_siguen_siendo_base_mas_id()
    {
        var c = new Carpetas(Conf(new() { ["Archivos:Base"] = Path.Combine(Path.GetTempPath(), "mda-base") }));
        Assert.Equal(Path.Combine(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mda-base")), "documentos"), c.RutaRaiz("documentos"));
    }

    [Fact]
    public void Una_ruta_propia_por_carpeta_manda_sobre_la_base_y_solo_si_es_absoluta()
    {
        var docs = Path.Combine(Path.GetTempPath(), "mda-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(docs);
        try
        {
            var c = new Carpetas(Conf(new() { ["Archivos:Base"] = "/carpetas", ["Archivos:Rutas:documentos"] = docs, ["Archivos:Rutas:musica"] = "relativa/no/vale" }));
            Assert.Equal(Path.GetFullPath(docs), c.RutaRaiz("documentos"));
            Assert.Equal(Path.Combine(c.Base, "musica"), c.RutaRaiz("musica"));
            Assert.Contains(c.Disponibles(), r => r.Id == "documentos");
            Assert.DoesNotContain(c.Disponibles(), r => r.Id == "musica");
            // sigue sin poder salirse de la carpeta
            Assert.NotNull(c.Resolver("documentos", "sub/dir"));
            Assert.Null(c.Resolver("documentos", "../fuera"));
        }
        finally { Directory.Delete(docs, true); }
    }

    [Fact]
    public void La_ayuda_de_copias_depende_del_origen_y_por_defecto_es_la_de_docker()
    {
        Assert.Contains("docker compose logs copias", Vigilante.AyudaCopias(Conf(new())));
        Assert.DoesNotContain("docker", Vigilante.AyudaCopias(Conf(new() { ["Copias:Origen"] = "nativo" })), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("revisa", Vigilante.AyudaCopias(Conf(new() { ["Copias:Origen"] = "nativo" }), minuscula: true));
    }
}
