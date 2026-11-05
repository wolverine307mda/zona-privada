using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class BuscarTests
{
    [Fact]
    public async Task Sin_sesion_no_se_puede_buscar()
    {
        using var f = new ModulosFactory();
        var r = await f.CreateClient().GetAsync("/api/buscar?q=taller");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Busca_en_agenda_y_movimientos_ignorando_mayusculas_y_con_tope_de_cinco()
    {
        using var f = new ModulosFactory();
        using (var s = f.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Eventos.Add(new Evento { Titulo = "Llevar el coche al Taller", Inicio = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), Lugar = "Getafe" });
            db.Eventos.Add(new Evento { Titulo = "Dentista", Inicio = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero) });
            for (var i = 0; i < 8; i++) db.Movimientos.Add(new Movimiento { Concepto = $"Taller Paco {i}", Importe = 10 + i, Fecha = new DateOnly(2026, 11, 1 + i), Categoria = "mecanico", Tipo = TipoMovimiento.Gasto });
            db.Movimientos.Add(new Movimiento { Concepto = "Mercadona", Importe = 30, Fecha = new DateOnly(2026, 11, 2), Categoria = "supermercado", Tipo = TipoMovimiento.Gasto });
            await db.SaveChangesAsync();
        }
        var c = await f.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/buscar?q=TALLER");
        Assert.Equal(1, j.GetProperty("agenda").GetArrayLength());
        Assert.Equal(5, j.GetProperty("movimientos").GetArrayLength());
        Assert.Equal("Taller Paco 7", j.GetProperty("movimientos")[0].GetProperty("concepto").GetString());   // el más reciente primero
        var geta = await c.GetFromJsonAsync<JsonElement>("/api/buscar?q=getafe");
        Assert.Equal(1, geta.GetProperty("agenda").GetArrayLength());                                        // también por lugar
    }

    [Fact]
    public async Task Una_busqueda_demasiado_corta_no_devuelve_nada()
    {
        using var f = new ModulosFactory();
        var c = await f.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/buscar?q=a");
        Assert.Equal(0, j.GetProperty("agenda").GetArrayLength());
        Assert.Equal(0, j.GetProperty("movimientos").GetArrayLength());
    }
}
