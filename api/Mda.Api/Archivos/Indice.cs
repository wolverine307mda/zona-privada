using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Archivos;

// El texto de un archivo de tus carpetas, para buscar por lo que dice y no solo por cómo se llama. Todo se lee en este equipo: nada sale a Internet.
public class TextoArchivo
{
    public int Id { get; set; }
    public string Raiz { get; set; } = "";
    public string Ruta { get; set; } = "";
    public long Tamano { get; set; }
    public long ModificadoTicks { get; set; }
    public string Origen { get; set; } = "";   // texto, office, pdf, ocr
    public string Estado { get; set; } = "ok"; // ok, sin-texto, omitido, error
    public string Texto { get; set; } = "";
}

public record ResultadoTexto(string Raiz, string Ruta, string Nombre, string Origen, string Fragmento);
public record ResultadoPasada(int Procesados, int Pendientes, int Quitados, int Total);

// Los lectores de cada tipo de archivo. Sin dependencias de pago: texto plano, Office (OpenXML y OpenDocument, que son zips de XML), PDF con texto (PdfPig)
// y, para imágenes y PDF escaneados, Tesseract si está instalado.
public static class Lectores
{
    public const int MaxCaracteres = 400_000, MaxBytes = 25 * 1024 * 1024, MaxPaginasPdf = 200;
    public static readonly string[] Plano = [".txt", ".md", ".csv", ".tsv", ".json", ".log", ".xml", ".html", ".htm", ".yml", ".yaml", ".ini", ".cfg", ".tex"];
    public static readonly string[] Office = [".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp"];
    public static readonly string[] Imagen = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif", ".webp"];

    public static string Tipo(string nombre)
    {
        var e = Path.GetExtension(nombre).ToLowerInvariant();
        return Plano.Contains(e) ? "texto" : Office.Contains(e) ? "office" : e == ".pdf" ? "pdf" : Imagen.Contains(e) ? "imagen" : "";
    }

    // Lee el texto de un archivo. null = no hay nada que leer (o hace falta OCR y no está disponible). Origen: texto, office, pdf u ocr.
    public static async Task<(string Origen, string Texto)?> Leer(string ruta, Ocr ocr, CancellationToken ct)
    {
        switch (Tipo(ruta))
        {
            case "texto": return Limpiar("texto", LeerPlano(ruta));
            case "office": return Limpiar("office", LeerOffice(ruta));
            case "pdf":
                var t = LeerPdf(ruta);
                // un PDF escaneado no tiene texto (o casi): si hay OCR se lee la imagen de cada página
                if (t.Trim().Length < 30 && ocr.Disponible) return Limpiar("ocr", await ocr.Pdf(ruta, ct));
                return Limpiar("pdf", t);
            case "imagen": return ocr.Disponible ? Limpiar("ocr", await ocr.Imagen(ruta, ct)) : null;
            default: return null;
        }
    }

    private static (string, string)? Limpiar(string origen, string texto)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(texto ?? "", @"\s+", " ").Trim();
        if (t.Length > MaxCaracteres) t = t[..MaxCaracteres];
        return t.Length == 0 ? null : (origen, t);
    }

    public static string LeerPlano(string ruta)
    {
        using var s = File.OpenRead(ruta);
        var buf = new char[MaxCaracteres];
        using var r = new StreamReader(s, Encoding.UTF8, true);
        var n = r.ReadBlock(buf, 0, buf.Length);
        var t = new string(buf, 0, n);
        return t.Contains('\0') ? "" : t;   // un binario con extensión de texto: nada
    }

    public static string LeerOffice(string ruta)
    {
        try
        {
            using var zip = ZipFile.OpenRead(ruta);
            var ext = Path.GetExtension(ruta).ToLowerInvariant();
            IEnumerable<ZipArchiveEntry> partes = ext switch
            {
                ".docx" => zip.Entries.Where(e => e.FullName == "word/document.xml"),
                ".xlsx" => zip.Entries.Where(e => e.FullName == "xl/sharedStrings.xml"),
                ".pptx" => zip.Entries.Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal)),
                _ => zip.Entries.Where(e => e.FullName == "content.xml"),
            };
            var sb = new StringBuilder();
            foreach (var p in partes)
            {
                if (p.Length > MaxBytes) continue;   // una parte «de 2 GB» al descomprimir (zip bomb) no se lee
                using var s = p.Open();
                sb.Append(TextoDeXml(s, ext is ".odt" or ".ods" or ".odp")).Append(' ');
                if (sb.Length > MaxCaracteres) break;
            }
            return sb.ToString();
        }
        catch (Exception e) when (e is InvalidDataException or IOException or XmlException) { return ""; }
    }

    // El texto de un XML de Office: en OpenXML los elementos «t» (w:t, a:t, t); en OpenDocument, todo el texto. Un salto de párrafo separa palabras.
    public static string TextoDeXml(Stream xml, bool todoElTexto)
    {
        var sb = new StringBuilder();
        var ajustes = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 1024 };
        using var r = XmlReader.Create(xml, ajustes);
        var dentroDeT = false;
        while (r.Read() && sb.Length < MaxCaracteres)
        {
            switch (r.NodeType)
            {
                case XmlNodeType.Element when r.LocalName == "t" && !todoElTexto: dentroDeT = !r.IsEmptyElement; break;
                case XmlNodeType.EndElement when r.LocalName == "t": dentroDeT = false; break;
                case XmlNodeType.EndElement when r.LocalName is "p" or "tab" or "br" or "row" or "si": sb.Append(' '); break;
                case XmlNodeType.Text or XmlNodeType.SignificantWhitespace when dentroDeT || todoElTexto: sb.Append(r.Value); break;
            }
        }
        return sb.ToString();
    }

    public static string LeerPdf(string ruta)
    {
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(ruta);
            var sb = new StringBuilder();
            foreach (var p in pdf.GetPages().Take(MaxPaginasPdf))
            {
                sb.Append(p.Text).Append(' ');
                if (sb.Length > MaxCaracteres) break;
            }
            return sb.ToString();
        }
        catch (Exception e) when (e is not OperationCanceledException) { return ""; }   // PDF roto o con contraseña: sin texto
    }

    // El trozo del texto donde aparece la búsqueda, con unas palabras de contexto a cada lado
    public static string Fragmento(string texto, string consulta, int ancho = 160)
    {
        var palabras = consulta.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length >= 2).ToList();
        var pos = -1;
        foreach (var p in palabras)
        {
            var i = texto.IndexOf(p, StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && (pos < 0 || i < pos)) pos = i;
        }
        if (pos < 0) return texto.Length <= ancho ? texto : texto[..ancho] + "…";
        var ini = Math.Max(0, pos - ancho / 3);
        var fin = Math.Min(texto.Length, ini + ancho);
        // sin cortar palabras por la mitad
        if (ini > 0) { var e = texto.IndexOf(' ', ini); if (e >= 0 && e < pos) ini = e + 1; }
        return (ini > 0 ? "…" : "") + texto[ini..fin].Trim() + (fin < texto.Length ? "…" : "");
    }
}

// Tesseract y poppler (pdftoppm) como programas externos, si están instalados. Sin ellos se sigue leyendo lo demás.
// Se ejecutan con prioridad baja, de uno en uno y con tiempo máximo.
public sealed class Ocr(IConfiguration config, ILogger<Ocr> log)
{
    private bool? _disponible;
    private readonly SemaphoreSlim _uno = new(1);
    public const int PaginasMax = 20, TiempoSegundos = 60;
    private string Idiomas => config["Indexado:Idiomas"] is { Length: > 0 } i ? i : "spa+eng";

    public bool Disponible => (config["Indexado:Ocr"] ?? "auto") != "off" && (_disponible ??= Comprobar());

    private bool Comprobar()
    {
        try
        {
            var r = Ejecutar("tesseract", ["--version"], TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            return r.Codigo == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { log.LogInformation("Sin Tesseract: las imágenes y los PDF escaneados no se leen (el resto sí)"); return false; }
    }

    public async Task<string> Imagen(string ruta, CancellationToken ct)
    {
        await _uno.WaitAsync(ct);
        try { return await Reconocer(ruta, ct); } finally { _uno.Release(); }
    }

    public async Task<string> Pdf(string ruta, CancellationToken ct)
    {
        await _uno.WaitAsync(ct);
        var dir = Path.Combine(Path.GetTempPath(), "mda-ocr-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var r = await Ejecutar("pdftoppm", ["-r", "200", "-png", "-l", PaginasMax.ToString(), ruta, Path.Combine(dir, "p")], TimeSpan.FromSeconds(TiempoSegundos * 2), ct);
            if (r.Codigo != 0) return "";
            var sb = new StringBuilder();
            foreach (var png in Directory.GetFiles(dir, "*.png").Order(StringComparer.Ordinal)) sb.Append(await Reconocer(png, ct)).Append(' ');
            return sb.ToString();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { return ""; }   // sin pdftoppm
        finally { _uno.Release(); try { Directory.Delete(dir, true); } catch (IOException) { /* se limpia solo */ } }
    }

    private async Task<string> Reconocer(string imagen, CancellationToken ct)
    {
        var r = await Ejecutar("tesseract", [imagen, "stdout", "-l", Idiomas, "--psm", "3"], TimeSpan.FromSeconds(TiempoSegundos), ct);
        // si falta el idioma español, se prueba solo con inglés
        if (r.Codigo != 0 && r.Error.Contains("language", StringComparison.OrdinalIgnoreCase) && Idiomas != "eng") r = await Ejecutar("tesseract", [imagen, "stdout", "-l", "eng", "--psm", "3"], TimeSpan.FromSeconds(TiempoSegundos), ct);
        return r.Codigo == 0 ? r.Salida : "";
    }

    public record Resultado(int Codigo, string Salida, string Error);

    public static async Task<Resultado> Ejecutar(string programa, IEnumerable<string> argumentos, TimeSpan limite, CancellationToken ct)
    {
        var info = new ProcessStartInfo(programa) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in argumentos) info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new InvalidOperationException("No se pudo arrancar " + programa);
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { /* ya terminó o no se deja */ }
        var salida = p.StandardOutput.ReadToEndAsync(ct);
        var error = p.StandardError.ReadToEndAsync(ct);
        using var tiempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tiempo.CancelAfter(limite);
        try { await p.WaitForExitAsync(tiempo.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { /* ya había terminado */ } if (ct.IsCancellationRequested) throw; return new Resultado(-1, "", "tiempo agotado"); }
        return new Resultado(p.ExitCode, await salida, await error);
    }
}

public static class Indice
{
    public const int PorPasada = 25;

    // Las raíces que se indexan (por defecto Documentos, Escritorio y Descargas; las fotos, la música y los vídeos no llevan texto que buscar)
    public static string[] Raices(IConfiguration config) =>
        (config["Indexado:Raices"] is { Length: > 0 } r ? r : "documentos,escritorio,descargas").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static IEnumerable<(string Ruta, FileInfo Archivo)> Archivos(string raiz)
    {
        var pendientes = new Stack<DirectoryInfo>();
        pendientes.Push(new DirectoryInfo(raiz));
        while (pendientes.Count > 0)
        {
            var d = pendientes.Pop();
            IEnumerable<FileSystemInfo> hijos;
            try { hijos = d.EnumerateFileSystemInfos().ToList(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var h in hijos)
            {
                if (Carpetas.Oculto(h) || h.Name == Carpetas.Papelera) continue;
                if (h is DirectoryInfo sub) pendientes.Push(sub);
                else if (h is FileInfo f && Lectores.Tipo(f.Name).Length > 0) yield return (Path.GetRelativePath(raiz, f.FullName).Replace('\\', '/'), f);
            }
        }
    }

    // Una pasada sobre una raíz: lo nuevo o cambiado se lee (hasta `maximo` archivos), lo que ya no está se quita
    public static async Task<ResultadoPasada> Pasada(AppDbContext db, string idRaiz, string rutaRaiz, Ocr ocr, int maximo = PorPasada, CancellationToken ct = default)
    {
        var enDisco = Archivos(rutaRaiz).ToList();
        var guardados = (await db.TextosArchivo.Where(t => t.Raiz == idRaiz).ToListAsync(ct)).ToDictionary(t => t.Ruta, StringComparer.Ordinal);
        int procesados = 0, pendientes = 0;
        foreach (var (ruta, f) in enDisco)
        {
            if (guardados.TryGetValue(ruta, out var g) && g.Tamano == f.Length && g.ModificadoTicks == f.LastWriteTimeUtc.Ticks) continue;
            if (procesados >= maximo) { pendientes++; continue; }
            if (g is null) db.TextosArchivo.Add(g = new TextoArchivo { Raiz = idRaiz, Ruta = ruta });
            (g.Tamano, g.ModificadoTicks) = (f.Length, f.LastWriteTimeUtc.Ticks);
            if (f.Length > Lectores.MaxBytes) { (g.Origen, g.Estado, g.Texto) = ("", "omitido", ""); }
            else
            {
                try
                {
                    var r = await Lectores.Leer(f.FullName, ocr, ct);
                    (g.Origen, g.Estado, g.Texto) = r is { } x ? (x.Origen, "ok", x.Texto) : ("", "sin-texto", "");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { (g.Origen, g.Estado, g.Texto) = ("", "error", ""); }
            }
            procesados++;
            await db.SaveChangesAsync(ct);
        }
        var presentes = enDisco.Select(x => x.Ruta).ToHashSet(StringComparer.Ordinal);
        var quitar = guardados.Values.Where(x => !presentes.Contains(x.Ruta)).ToList();
        db.TextosArchivo.RemoveRange(quitar);
        await db.SaveChangesAsync(ct);
        return new ResultadoPasada(procesados, pendientes, quitar.Count, enDisco.Count);
    }

    // Busca por lo que dicen los archivos. En PostgreSQL, búsqueda de texto completo en español (acentos, plurales, conjugaciones); en la base en memoria de las pruebas, por palabras.
    public static async Task<List<ResultadoTexto>> Buscar(AppDbContext db, string consulta, string? raiz, int limite, CancellationToken ct)
    {
        var q = consulta.Trim();
        if (q.Length < 2 || q.Length > 100) return [];
        var baseQ = db.TextosArchivo.AsNoTracking().Where(t => t.Estado == "ok" && (raiz == null || t.Raiz == raiz));
        List<TextoArchivo> filas;
        if (db.Database.IsNpgsql())
        {
            // la consulta tiene que ir dentro de la expresión (si no, EF intenta evaluarla en el cliente)
            filas = await baseQ.Where(t => EF.Functions.ToTsVector("spanish", t.Texto).Matches(EF.Functions.WebSearchToTsQuery("spanish", q)))
                .OrderByDescending(t => EF.Functions.ToTsVector("spanish", t.Texto).Rank(EF.Functions.WebSearchToTsQuery("spanish", q))).Take(limite).ToListAsync(ct);
        }
        else
        {
            var palabras = q.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            filas = (await baseQ.ToListAsync(ct)).Where(t => palabras.All(p => t.Texto.Contains(p, StringComparison.OrdinalIgnoreCase))).Take(limite).ToList();
        }
        return filas.Select(t => new ResultadoTexto(t.Raiz, t.Ruta, Path.GetFileName(t.Ruta), t.Origen, Lectores.Fragmento(t.Texto, q))).ToList();
    }
}

public sealed class IndiceDocumentos(IServiceScopeFactory ambitos, Carpetas carpetas, Ocr ocr, IConfiguration config, ILogger<IndiceDocumentos> log) : BackgroundService
{
    private readonly SemaphoreSlim _despertar = new(0);
    public ResultadoPasada? Ultimo { get; private set; }
    public bool Trabajando { get; private set; }
    public bool OcrDisponible => ocr.Disponible;

    // «acaba de subirse algo»: se repasa sin esperar a la próxima ronda
    public void Despertar() { if (_despertar.CurrentCount == 0) _despertar.Release(); }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if ((config["Indexado:Activo"] ?? "si") == "no") return;
        try { await Task.Delay(TimeSpan.FromMinutes(3), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Trabajando = true;
                int pendientes, procesados = 0, quitados = 0, total = 0;
                using var scope = ambitos.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                do
                {
                    pendientes = 0;
                    foreach (var id in Indice.Raices(config))
                    {
                        var ruta = carpetas.RutaRaiz(id);
                        if (!Directory.Exists(ruta)) continue;
                        var r = await Indice.Pasada(db, id, ruta, ocr, Indice.PorPasada, ct);
                        (procesados, quitados, total, pendientes) = (procesados + r.Procesados, quitados + r.Quitados, total + r.Total, pendientes + r.Pendientes);
                    }
                    Ultimo = new ResultadoPasada(procesados, pendientes, quitados, total);
                    if (pendientes > 0) await Task.Delay(TimeSpan.FromSeconds(3), ct);   // con mucho por leer se sigue, sin acaparar el PC
                } while (pendientes > 0);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "No se pudo indexar el texto de los documentos"); }
            finally { Trabajando = false; }
            try { await _despertar.WaitAsync(TimeSpan.FromMinutes(30), ct); } catch (OperationCanceledException) { return; }
        }
    }
}

public static class BuscarTextoEndpoints
{
    public static void MapBuscarTexto(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/archivos").RequireAuthorization();

        // Buscar por lo que dice el archivo (con el PIN, como el resto de Documentos)
        g.MapGet("/buscar-texto", async (string q, string? raiz, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await Indice.Buscar(db, q, raiz, 20, ct))).AddEndpointFilter<PinFiltro>();

        // cuánto lleva leído el índice
        g.MapGet("/indice", async (AppDbContext db, IndiceDocumentos indice, CancellationToken ct) => Results.Ok(new
        {
            indexados = await db.TextosArchivo.CountAsync(t => t.Estado == "ok", ct),
            sinTexto = await db.TextosArchivo.CountAsync(t => t.Estado == "sin-texto", ct),
            pendientes = indice.Ultimo?.Pendientes ?? 0, trabajando = indice.Trabajando, ocr = indice.OcrDisponible,
        })).AddEndpointFilter<PinFiltro>();
    }
}
