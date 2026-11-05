using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Mda.Api.Archivos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class IndiceTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mda-indice-" + Guid.NewGuid().ToString("N"));
    public IndiceTests() => Directory.CreateDirectory(_raiz);
    public void Dispose() { try { Directory.Delete(_raiz, true); } catch (IOException) { /* se limpia solo */ } }

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    // sin OCR en las pruebas: se comprueba todo lo demás (con él apagado por ajuste no hace falta ningún programa externo)
    private static Ocr SinOcr() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Indexado:Ocr"] = "off" }).Build(), NullLogger<Ocr>.Instance);

    private string Docx(string nombre, params string[] parrafos)
    {
        var ruta = Path.Combine(_raiz, nombre);
        using var zip = ZipFile.Open(ruta, ZipArchiveMode.Create);
        var xml = "<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
            + string.Concat(parrafos.Select(p => $"<w:p><w:r><w:t>{p}</w:t></w:r></w:p>")) + "</w:body></w:document>";
        using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
        w.Write(xml);
        return ruta;
    }

    private string Pdf(string nombre, string texto)
    {
        var ruta = Path.Combine(_raiz, nombre);
        var b = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var fuente = b.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        b.AddPage(595, 842).AddText(texto, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), fuente);
        File.WriteAllBytes(ruta, b.Build());
        return ruta;
    }

    [Fact]
    public void Clasifica_los_tipos_de_archivo()
    {
        Assert.Equal("texto", Lectores.Tipo("notas.TXT"));
        Assert.Equal("office", Lectores.Tipo("contrato.docx"));
        Assert.Equal("pdf", Lectores.Tipo("factura.pdf"));
        Assert.Equal("imagen", Lectores.Tipo("escaneo.jpg"));
        Assert.Equal("", Lectores.Tipo("programa.exe"));
        Assert.Equal("", Lectores.Tipo("cancion.mp3"));
    }

    [Fact]
    public async Task Lee_texto_plano_y_descarta_un_binario_con_extension_de_texto()
    {
        File.WriteAllText(Path.Combine(_raiz, "a.txt"), "Seguro del coche   renovación\n en marzo");
        File.WriteAllBytes(Path.Combine(_raiz, "raro.txt"), [0x50, 0x00, 0x01, 0x02]);
        var r = await Lectores.Leer(Path.Combine(_raiz, "a.txt"), SinOcr(), default);
        Assert.Equal(("texto", "Seguro del coche renovación en marzo"), r);                    // los espacios y saltos se juntan
        Assert.Null(await Lectores.Leer(Path.Combine(_raiz, "raro.txt"), SinOcr(), default));
    }

    [Fact]
    public async Task Lee_un_docx_y_un_xlsx()
    {
        var d = Docx("contrato.docx", "Contrato de alquiler", "Fianza: dos mensualidades");
        var r = await Lectores.Leer(d, SinOcr(), default);
        Assert.Equal("office", r!.Value.Origen);
        Assert.Contains("Contrato de alquiler", r.Value.Texto);
        Assert.Contains("Fianza: dos mensualidades", r.Value.Texto);

        var x = Path.Combine(_raiz, "gastos.xlsx");
        using (var zip = ZipFile.Open(x, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("xl/sharedStrings.xml").Open(), new UTF8Encoding(false)))
            w.Write("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>Mercadona</t></si><si><t>Gasolinera</t></si></sst>");
        var rx = await Lectores.Leer(x, SinOcr(), default);
        Assert.Contains("Mercadona", rx!.Value.Texto);
        Assert.Contains("Gasolinera", rx.Value.Texto);
    }

    [Fact]
    public async Task Un_zip_roto_o_que_no_es_office_no_tumba_nada()
    {
        File.WriteAllBytes(Path.Combine(_raiz, "roto.docx"), [1, 2, 3, 4]);
        Assert.Null(await Lectores.Leer(Path.Combine(_raiz, "roto.docx"), SinOcr(), default));
        using (var zip = ZipFile.Open(Path.Combine(_raiz, "vacio.docx"), ZipArchiveMode.Create)) { }
        Assert.Null(await Lectores.Leer(Path.Combine(_raiz, "vacio.docx"), SinOcr(), default));
    }

    [Fact]
    public async Task Lee_un_pdf_con_texto_y_un_pdf_roto_da_nada()
    {
        var r = await Lectores.Leer(Pdf("factura.pdf", "Factura numero 2026 de la luz"), SinOcr(), default);
        Assert.Equal("pdf", r!.Value.Origen);
        Assert.Contains("Factura numero 2026", r.Value.Texto);
        File.WriteAllText(Path.Combine(_raiz, "mal.pdf"), "esto no es un pdf");
        Assert.Null(await Lectores.Leer(Path.Combine(_raiz, "mal.pdf"), SinOcr(), default));
    }

    [Fact]
    public async Task Una_imagen_sin_ocr_no_da_texto_y_no_es_un_error()
    {
        File.WriteAllBytes(Path.Combine(_raiz, "foto.png"), [0x89, 0x50, 0x4E, 0x47]);
        Assert.Null(await Lectores.Leer(Path.Combine(_raiz, "foto.png"), SinOcr(), default));
    }

    [Fact]
    public void El_xml_con_una_entidad_externa_no_se_resuelve()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><w:p xmlns:w=\"x\"><w:t>&e;</w:t></w:p>";
        Assert.ThrowsAny<System.Xml.XmlException>(() => Lectores.TextoDeXml(new MemoryStream(Encoding.UTF8.GetBytes(xml)), false));   // los DTD están prohibidos
    }

    [Fact]
    public void El_fragmento_centra_la_busqueda_y_no_corta_palabras()
    {
        var texto = string.Concat(Enumerable.Repeat("palabra ", 40)) + "la fianza del alquiler es de dos mensualidades " + string.Concat(Enumerable.Repeat("relleno ", 40));
        var f = Lectores.Fragmento(texto, "FIANZA");
        Assert.Contains("fianza", f);
        Assert.StartsWith("…", f);
        Assert.EndsWith("…", f);
        Assert.True(f.Length < 200);
        Assert.Equal("corto", Lectores.Fragmento("corto", "inexistente"));
        Assert.Equal("a b", Lectores.Fragmento("a b", "b"));
    }

    [Fact]
    public async Task La_pasada_lee_lo_nuevo_no_repite_y_quita_lo_borrado()
    {
        File.WriteAllText(Path.Combine(_raiz, "a.txt"), "uno dos tres");
        Docx("b.docx", "hola mundo");
        File.WriteAllBytes(Path.Combine(_raiz, "musica.mp3"), [1, 2, 3]);                 // no es de los que se leen
        Directory.CreateDirectory(Path.Combine(_raiz, Carpetas.Papelera));
        File.WriteAllText(Path.Combine(_raiz, Carpetas.Papelera, "borrado.txt"), "secreto");   // la papelera no se indexa
        using var db = Db();

        var r1 = await Indice.Pasada(db, "documentos", _raiz, SinOcr());
        Assert.Equal(2, r1.Procesados);
        Assert.Equal(2, r1.Total);
        Assert.Equal(2, await db.TextosArchivo.CountAsync(t => t.Estado == "ok"));
        Assert.DoesNotContain(await db.TextosArchivo.ToListAsync(), t => t.Ruta.Contains("papelera") || t.Texto == "secreto");

        Assert.Equal(0, (await Indice.Pasada(db, "documentos", _raiz, SinOcr())).Procesados);   // nada cambió

        File.WriteAllText(Path.Combine(_raiz, "a.txt"), "uno dos tres cuatro");
        Assert.Equal(1, (await Indice.Pasada(db, "documentos", _raiz, SinOcr())).Procesados);   // lo cambiado se vuelve a leer
        File.Delete(Path.Combine(_raiz, "b.docx"));
        var r4 = await Indice.Pasada(db, "documentos", _raiz, SinOcr());
        Assert.Equal(1, r4.Quitados);
        Assert.Equal(1, await db.TextosArchivo.CountAsync());
    }

    [Fact]
    public async Task El_tope_por_pasada_deja_pendientes_y_el_resto_se_lee_despues()
    {
        for (var i = 0; i < 5; i++) File.WriteAllText(Path.Combine(_raiz, $"f{i}.txt"), $"contenido {i}");
        using var db = Db();
        var r1 = await Indice.Pasada(db, "documentos", _raiz, SinOcr(), maximo: 2);
        Assert.Equal((2, 3), (r1.Procesados, r1.Pendientes));
        var r2 = await Indice.Pasada(db, "documentos", _raiz, SinOcr(), maximo: 10);
        Assert.Equal((3, 0), (r2.Procesados, r2.Pendientes));
    }

    [Fact]
    public async Task Un_archivo_sin_texto_se_apunta_para_no_leerlo_otra_vez()
    {
        File.WriteAllText(Path.Combine(_raiz, "vacio.txt"), "   ");
        using var db = Db();
        await Indice.Pasada(db, "documentos", _raiz, SinOcr());
        Assert.Equal("sin-texto", (await db.TextosArchivo.SingleAsync()).Estado);
        Assert.Equal(0, (await Indice.Pasada(db, "documentos", _raiz, SinOcr())).Procesados);
    }

    [Fact]
    public async Task Busca_por_palabras_en_cualquier_orden_y_da_el_fragmento_sin_mirar_otras_raices()
    {
        File.WriteAllText(Path.Combine(_raiz, "seguro.txt"), "Póliza del seguro del coche, renovación en marzo");
        File.WriteAllText(Path.Combine(_raiz, "otro.txt"), "Lista de la compra: pan, leche");
        using var db = Db();
        await Indice.Pasada(db, "documentos", _raiz, SinOcr());
        var r = await Indice.Buscar(db, "renovación seguro", null, 20, default);
        Assert.Single(r);
        Assert.Equal("seguro.txt", r[0].Nombre);
        Assert.Contains("renovación", r[0].Fragmento);
        Assert.Empty(await Indice.Buscar(db, "renovación", "descargas", 20, default));     // otra raíz
        Assert.Empty(await Indice.Buscar(db, "x", null, 20, default));                      // demasiado corta
        Assert.Empty(await Indice.Buscar(db, "inexistente", null, 20, default));
    }

    [Fact]
    public async Task Sin_sesion_o_sin_pin_no_se_busca()
    {
        using var f = new ModulosFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/archivos/buscar-texto?q=seguro")).StatusCode);
        var c = await f.Dentro();
        var j = await c.GetFromJsonAsync<JsonElement>("/api/archivos/indice");
        Assert.Equal(0, j.GetProperty("indexados").GetInt32());
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/archivos/buscar-texto?q=seguro")).GetArrayLength());
    }
}
