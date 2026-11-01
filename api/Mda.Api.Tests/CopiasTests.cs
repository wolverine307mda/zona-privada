using Mda.Api.Avisos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Mda.Api.Data;

namespace Mda.Api.Tests;

public class CopiasFactory : ModulosFactory
{
    public string Carpeta { get; } = Directory.CreateTempSubdirectory("copias-").FullName;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Copias:Ruta", Carpeta);
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); try { Directory.Delete(Carpeta, true); } catch (IOException) { } }
}

public class CopiasTests(CopiasFactory factory) : IClassFixture<CopiasFactory>
{
    [Fact]
    public void El_resultado_del_simulacro_se_lee_o_se_descarta()
    {
        var s = Vigilante.LeerSimulacro("ok|1|27|zonaprivada-20261101-0312.dump|2026-11-01T04:05:06Z")!;
        Assert.True(s.Ok);
        Assert.Equal(1, s.Usuarios);
        Assert.Equal(27, s.Tablas);
        Assert.Equal("zonaprivada-20261101-0312.dump", s.Copia);
        Assert.False(Vigilante.LeerSimulacro("fallo|0|0|x.dump|2026-11-01T04:05:06Z")!.Ok);
        Assert.Null(Vigilante.LeerSimulacro(""));
        Assert.Null(Vigilante.LeerSimulacro("quizá|1|2|x|2026-11-01T04:05:06Z"));
        Assert.Null(Vigilante.LeerSimulacro("ok|uno|2|x|ayer"));
        Assert.Null(Vigilante.LeerSimulacro(null));
    }

    [Fact]
    public async Task El_simulacro_se_avisa_una_sola_vez_y_el_fallo_tambien_por_push()
    {
        var v = factory.Services.GetRequiredService<Vigilante>();
        var fichero = Path.Combine(factory.Carpeta, ".ultimo-simulacro");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await v.ResultadoSimulacro(db, default); // sin fichero: nada
        Assert.DoesNotContain(factory.Avisos.Enviados, m => m.Contains("Copia verificada"));

        await File.WriteAllTextAsync(fichero, "ok|1|27|zonaprivada-20261101-0312.dump|2026-11-01T04:05:06Z\n");
        await v.ResultadoSimulacro(db, default);
        await v.ResultadoSimulacro(db, default); // la misma semana: no se repite
        Assert.Single(factory.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("Copia verificada") && m.Contains("1 usuario"));

        await File.WriteAllTextAsync(fichero, "fallo|0|0|zonaprivada-20261108-0312.dump|2026-11-08T04:05:06Z\n");
        await v.ResultadoSimulacro(db, default);
        Assert.Contains(factory.Avisos.Enviados, m => m.StartsWith("push:") && m.Contains("dudosa"));
        Assert.Contains(factory.Avisos.Enviados, m => m.StartsWith("telegram:") && m.Contains("NO se pudo restaurar"));
    }

    [Fact]
    public async Task Cada_copia_nueva_avisa_pero_la_primera_vez_solo_se_anota_y_se_puede_apagar()
    {
        var v = factory.Services.GetRequiredService<Vigilante>();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int Avisos() => factory.Avisos.Enviados.Count(m => m.Contains("Copia de seguridad hecha"));

        var a = Path.Combine(factory.Carpeta, "zonaprivada-20261101-0300.dump");
        await File.WriteAllBytesAsync(a, new byte[2048]);
        await v.CopiaNueva(db, default);
        Assert.Equal(0, Avisos());                       // la primera solo se apunta

        var b = Path.Combine(factory.Carpeta, "zonaprivada-20261102-0300.dump");
        await File.WriteAllBytesAsync(b, new byte[4096]);
        File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddMinutes(5));
        await v.CopiaNueva(db, default);
        await v.CopiaNueva(db, default);                 // la misma copia no se repite
        Assert.Equal(1, Avisos());

        await Mda.Api.Pc.AvisosPcAjustes.Guardar(db, new Mda.Api.Pc.AvisosPc(Copias: false));
        var c = Path.Combine(factory.Carpeta, "zonaprivada-20261103-0300.dump");
        await File.WriteAllBytesAsync(c, new byte[1024]);
        File.SetLastWriteTimeUtc(c, DateTime.UtcNow.AddMinutes(10));
        await v.CopiaNueva(db, default);
        Assert.Equal(1, Avisos());                       // con el aviso apagado no sale
    }
}
