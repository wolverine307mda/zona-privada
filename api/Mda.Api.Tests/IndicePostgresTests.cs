using Mda.Api.Archivos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

// La búsqueda de texto completo de verdad (PostgreSQL, diccionario español); la base en memoria solo sabe buscar palabras sueltas. Hace falta Docker; sin él se salta.
public class IndicePostgresTests(PostgresFactory f) : IClassFixture<PostgresFactory>
{
    [Fact]
    public async Task La_busqueda_de_texto_completo_entiende_plurales_acentos_y_conjugaciones_y_ordena_por_relevancia()
    {
        if (!f.Disponible) return;
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TextosArchivo.AddRange(
            new TextoArchivo { Raiz = "documentos", Ruta = "facturas/luz.pdf", Estado = "ok", Origen = "pdf", Texto = "Factura de la luz de octubre. Importe de la factura: 54,20 euros." },
            new TextoArchivo { Raiz = "documentos", Ruta = "casa/contrato.docx", Estado = "ok", Origen = "office", Texto = "Contrato de alquiler. La fianza son dos mensualidades." },
            new TextoArchivo { Raiz = "documentos", Ruta = "viejo/vacio.txt", Estado = "sin-texto", Origen = "", Texto = "" },
            new TextoArchivo { Raiz = "descargas", Ruta = "recetas.txt", Estado = "ok", Origen = "texto", Texto = "Receta de tortilla de patatas. Se baten los huevos." });
        await db.SaveChangesAsync();

        // plural ↔ singular y la raíz de la palabra
        var facturas = await Indice.Buscar(db, "facturas", null, 20, default);
        Assert.Equal(["facturas/luz.pdf"], facturas.Select(r => r.Ruta).ToArray());
        Assert.Contains("actura", facturas[0].Fragmento);
        // acentos y mayúsculas da igual
        Assert.Single(await Indice.Buscar(db, "ALQUILER fianza", null, 20, default));
        // conjugaciones: «bate» encuentra «baten»
        Assert.Single(await Indice.Buscar(db, "batir huevo", null, 20, default));
        // restringido a una raíz
        Assert.Empty(await Indice.Buscar(db, "factura", "descargas", 20, default));
        // lo que no está
        Assert.Empty(await Indice.Buscar(db, "hipoteca", null, 20, default));
        // una consulta con símbolos raros no rompe la sintaxis de tsquery
        await Indice.Buscar(db, "a & ( | ! :* '", null, 20, default);
    }

    [Fact]
    public async Task El_indice_gin_existe_tras_aplicar_las_migraciones()
    {
        if (!f.Disponible) return;
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var n = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_textos_archivo_texto'").SingleAsync();
        Assert.Equal(1, n);
    }
}
