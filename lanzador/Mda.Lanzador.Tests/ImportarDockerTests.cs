using Mda.Lanzador;

namespace Mda.Lanzador.Tests;

public class ImportarDockerTests
{
    [Fact]
    public void Solo_se_importa_si_la_clave_maestra_es_la_misma()
    {
        var d = new Dictionary<string, string> { ["MDA_CLAVE_MAESTRA"] = "abc" };
        Assert.Empty(ImportarDocker.Problemas(d, new Dictionary<string, string> { ["MDA_CLAVE_MAESTRA"] = "abc" }));
        Assert.Single(ImportarDocker.Problemas(d, new Dictionary<string, string> { ["MDA_CLAVE_MAESTRA"] = "otra" }));
        Assert.Single(ImportarDocker.Problemas(new Dictionary<string, string>(), new Dictionary<string, string> { ["MDA_CLAVE_MAESTRA"] = "abc" }));
    }

    [Fact]
    public void Las_ordenes_a_docker_solo_leen()
    {
        var todas = new[] { ImportarDocker.VolcarDocker, ImportarDocker.IdApiDocker, ImportarDocker.Contar(ImportarDocker.SqlUsuarios), ImportarDocker.Contar(ImportarDocker.SqlTablas), ImportarDocker.CopiarClaves("abc123", @"C:\tmp\claves") };
        foreach (var o in todas)
        {
            foreach (var peligro in new[] { "down", "stop", "restart", "rm ", "kill", "up ", "DROP", "DELETE", "UPDATE", "INSERT", "pg_restore" })
                Assert.DoesNotContain(peligro, o, StringComparison.OrdinalIgnoreCase);
        }
        Assert.StartsWith("cp abc123:/claves/.", ImportarDocker.CopiarClaves("abc123", @"C:\tmp"));   // de Docker hacia fuera, nunca al revés
    }

    [Fact]
    public void La_comprobacion_exige_los_mismos_usuarios_y_tablas()
    {
        Assert.True(ImportarDocker.Coincide(1, 47, 1, 47));
        Assert.False(ImportarDocker.Coincide(1, 47, 1, 46));
        Assert.False(ImportarDocker.Coincide(1, 47, 0, 47));
        Assert.False(ImportarDocker.Coincide(0, 47, 0, 47));   // una base sin usuarios no es una copia válida
        Assert.False(ImportarDocker.Coincide(1, 47, -1, -1));
    }
}
