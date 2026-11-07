using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Conexiones;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Tests;

public class LrcTests
{
    [Fact]
    public void Lee_tiempos_con_centesimas_milesimas_y_sin_fraccion()
    {
        var l = Lrc.Parsear("[00:12.34] Hola\n[01:05.5] Adiós\n[02:00] Fin\n[00:30.123] Tres");
        Assert.Equal([(12340, "Hola"), (30123, "Tres"), (65500, "Adiós"), (120000, "Fin")], l.Select(x => (x.Ms, x.Texto)));
    }

    [Fact]
    public void Una_linea_con_varios_tiempos_se_repite_y_las_etiquetas_y_lineas_sin_tiempo_se_ignoran()
    {
        var l = Lrc.Parsear("[ar:Artista]\n[ti:Título]\n[by:alguien]\nsin tiempo\n[00:10.00][01:30.00] Estribillo\n\n[00:20.00]");
        Assert.Equal([(10000, "Estribillo"), (20000, ""), (90000, "Estribillo")], l.Select(x => (x.Ms, x.Texto)));      // el vacío se conserva: es una pausa
    }

    [Fact]
    public void Aguanta_finales_de_linea_de_windows_y_entradas_raras()
    {
        Assert.Equal("Hola", Lrc.Parsear("[00:01.00] Hola\r\n")[0].Texto);
        Assert.Empty(Lrc.Parsear(null)); Assert.Empty(Lrc.Parsear("")); Assert.Empty(Lrc.Parsear("   "));
        Assert.Empty(Lrc.Parsear("[99:99.99 roto"));
    }

    [Fact]
    public void La_linea_actual_es_la_ultima_que_ya_ha_empezado()
    {
        var l = Lrc.Parsear("[00:10.00] A\n[00:20.00] B\n[00:30.00] C");
        Assert.Equal(-1, Lrc.LineaActual(l, 5000));
        Assert.Equal(0, Lrc.LineaActual(l, 10000));
        Assert.Equal(0, Lrc.LineaActual(l, 19999));
        Assert.Equal(1, Lrc.LineaActual(l, 20000));
        Assert.Equal(2, Lrc.LineaActual(l, 999999));
        Assert.Equal(-1, Lrc.LineaActual([], 1000));
    }
}

public class LetrasApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Una_letra_guardada_se_sirve_sin_pedirla_otra_vez_sincronizada_o_plana()
    {
        var c = await factory.Dentro();
        using (var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(factory.Services))
        {
            var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AppDbContext>(scope.ServiceProvider);
            db.Letras.AddRange(
                new Letra { CancionId = "sinc", Sincronizada = "[00:05.00] Primera\n[00:10.00] Segunda", Plana = "Primera\nSegunda", Encontrada = true, Fecha = DateTimeOffset.UtcNow },
                new Letra { CancionId = "plana", Plana = "Solo texto\nsin tiempos", Encontrada = true, Fecha = DateTimeOffset.UtcNow },
                new Letra { CancionId = "nada", Encontrada = false, Fecha = DateTimeOffset.UtcNow });       // «no hay», mirado hace un momento: no se vuelve a pedir
            await db.SaveChangesAsync();
        }
        var a = await c.GetFromJsonAsync<JsonElement>("/api/musica/letra?id=sinc&nombre=Cancion&artista=A,B&duracion=200000");
        Assert.Equal("sincronizada", a.GetProperty("estado").GetString());
        Assert.Equal(2, a.GetProperty("lineas").GetArrayLength());
        Assert.Equal("LRCLIB", a.GetProperty("fuente").GetString());

        var b = await c.GetFromJsonAsync<JsonElement>("/api/musica/letra?id=plana&nombre=Cancion&artista=A");
        Assert.Equal("plana", b.GetProperty("estado").GetString());
        Assert.Contains("sin tiempos", b.GetProperty("plana").GetString());

        var n = await c.GetFromJsonAsync<JsonElement>("/api/musica/letra?id=nada&nombre=Cancion&artista=A");
        Assert.Equal("nada", n.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Pide_los_datos_y_exige_entrar()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/musica/letra?id=&nombre=x&artista=y")).StatusCode);
        using var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/musica/letra?id=a&nombre=x&artista=y")).StatusCode);
    }
}
