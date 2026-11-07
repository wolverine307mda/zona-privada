using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class SaludAppLogicaTests
{
    private static TamanoTabla F(int dia, string tabla, long mb) => new() { Dia = new DateOnly(2026, 11, dia), Tabla = tabla, Bytes = mb * 1048576 };

    [Fact]
    public void El_crecimiento_es_la_diferencia_entre_la_foto_mas_vieja_y_la_mas_nueva_por_dia()
    {
        var c = SaludApp.Crecimiento([F(1, "lecturas", 100), F(5, "lecturas", 140), F(3, "lecturas", 120), F(1, "gastos", 10), F(5, "gastos", 10)]);
        Assert.Equal("lecturas", c[0].Tabla);
        Assert.Equal(10L * 1048576, c[0].BytesPorDia);        // 40 MB en 4 días
        Assert.Equal(4, c[0].DiasMedidos);
        Assert.Equal(0, c[1].BytesPorDia);
    }

    [Fact]
    public void Con_una_sola_foto_no_hay_tendencia_y_si_se_encoge_sale_negativo()
    {
        Assert.Equal(0, SaludApp.Crecimiento([F(1, "a", 50)])[0].BytesPorDia);
        Assert.Equal(0, SaludApp.Crecimiento([F(1, "a", 50)])[0].DiasMedidos);
        Assert.True(SaludApp.Crecimiento([F(1, "a", 100), F(3, "a", 60)])[0].BytesPorDia < 0);
    }

    [Fact]
    public void Recomienda_las_tablas_grandes_el_crecimiento_rapido_y_lo_de_la_copia_y_los_errores()
    {
        var tablas = new List<FilaTabla> { new("casa_lecturas", 400L * 1048576, 9_000_000), new("movimientos", 5L * 1048576, 800), new("fotos", 300L * 1048576, 2000) };
        var crece = new List<CrecimientoTabla> { new("casa_lecturas", 400L * 1048576, 25L * 1048576, 6) };
        var r = SaludApp.Recomendaciones(tablas, crece, 100, 7);
        Assert.Contains(r, x => x.Id == "tabla:casa_lecturas" && x.Texto.Contains("400 MB") && x.Texto.Contains("30 días"));
        Assert.Contains(r, x => x.Id == "tabla:fotos");
        Assert.DoesNotContain(r, x => x.Id == "tabla:movimientos");
        Assert.Contains(r, x => x.Id == "crece:casa_lecturas" && x.Texto.Contains("25 MB al día"));
        Assert.Contains(r, x => x.Id == "copia" && x.Texto.Contains("4 días"));
        Assert.Contains(r, x => x.Id == "errores");
    }

    [Fact]
    public void Sin_problemas_no_recomienda_nada_y_sin_copia_lo_dice()
    {
        Assert.Empty(SaludApp.Recomendaciones([new("movimientos", 1048576, 10)], [], 5, 0));
        Assert.Contains(SaludApp.Recomendaciones([], [], null, 0), x => x.Id == "copia");
    }

    [Fact]
    public void Un_crecimiento_con_menos_de_tres_dias_de_datos_no_se_recomienda()
    {
        var r = SaludApp.Recomendaciones([], [new("a", 1, 100L * 1048576, 2)], 5, 0);
        Assert.DoesNotContain(r, x => x.Id.StartsWith("crece"));
    }
}

public class SaludAppApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task El_panel_responde_aunque_no_haya_postgres()
    {
        var c = await factory.Dentro();
        var r = await c.GetFromJsonAsync<JsonElement>("/api/sistema/app");
        Assert.Equal("memoria", r.GetProperty("motor").GetString());
        Assert.True(r.GetProperty("memoriaMb").GetDouble() > 0);
        Assert.Equal(0, r.GetProperty("tablas").GetArrayLength());
    }

    [Fact]
    public async Task El_mantenimiento_enseña_la_vista_previa_y_exige_confirmar()
    {
        var c = await factory.Dentro();
        var acciones = await c.GetFromJsonAsync<JsonElement>("/api/sistema/app/mantenimiento");
        Assert.Contains(acciones.EnumerateArray(), a => a.GetProperty("id").GetString() == "llamadas");
        Assert.DoesNotContain(acciones.EnumerateArray(), a => a.GetProperty("id").GetString() == "vacuum" && a.GetProperty("disponible").GetBoolean());   // sin PostgreSQL no se ofrece
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/sistema/app/mantenimiento/llamadas", null)).StatusCode);
    }

    [Fact]
    public async Task Sin_entrar_no_se_ve_nada()
    {
        var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/sistema/app")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsync("/api/sistema/app/mantenimiento/llamadas?confirmar=true", null)).StatusCode);
    }

    [Fact]
    public async Task Las_acciones_borran_lo_viejo_y_solo_lo_viejo()
    {
        var db = BaseDeDatos.Nueva();
        var ahora = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        db.LlamadasExternas.AddRange(new Mda.Api.Seguridad.LlamadaExterna { Fecha = ahora.AddDays(-100) }, new Mda.Api.Seguridad.LlamadaExterna { Fecha = ahora.AddDays(-5) });
        await db.SaveChangesAsync();
        var antes = (await SaludApp.Acciones(db, ahora, default)).First(a => a.Id == "llamadas");
        Assert.Equal(1, antes.Afecta); Assert.True(antes.Disponible);

        var (ok, mensaje) = await SaludApp.Ejecutar("llamadas", db, ahora, default);
        Assert.True(ok); Assert.Contains("1", mensaje);
        Assert.Single(await db.LlamadasExternas.ToListAsync());
        Assert.False((await SaludApp.Acciones(db, ahora, default)).First(a => a.Id == "llamadas").Disponible);
        Assert.False((await SaludApp.Ejecutar("no-existe", db, ahora, default)).Ok);
    }
}
