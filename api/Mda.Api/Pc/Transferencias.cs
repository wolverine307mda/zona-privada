using Mda.Api.Archivos;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// La bandeja en los dos sentidos. «Para el PC»: subes un archivo desde la app (hasta 100 MB, sin ejecutables) y el agente lo recoge y lo deja en
// «Zona privada\Bandeja\Recibidos». «Para el móvil»: lo que dejes en «Zona privada\Bandeja\Para el móvil» lo sube el agente y la app lo ofrece 24 horas.
// Mientras tanto el archivo vive cifrado en un almacén temporal (tope de 1 GB) y se borra al recogerlo, al bajarlo o al caducar. Nada se ejecuta.
public class Transferencia
{
    public int Id { get; set; }
    public string Direccion { get; set; } = "pc";          // pc (de la app al PC) o movil (del PC a la app)
    public string Nombre { get; set; } = "";
    public long Tamano { get; set; }
    public string Estado { get; set; } = "subido";          // subido · en-el-pc · para-descargar · descargado · caducado · error
    public DateTimeOffset Creada { get; set; }
    public DateTimeOffset Caduca { get; set; }
    public string Archivo { get; set; } = "";               // nombre del fichero cifrado en el almacén (vacío si ya se borró)
    public string Mensaje { get; set; } = "";
}

public record ResultadoRecogida(bool Ok, string? Mensaje);

public static class Transferencias
{
    public const long Maximo = 100L * 1024 * 1024, TopeAlmacen = 1024L * 1024 * 1024, ConContrasena = 20L * 1024 * 1024;
    public static readonly TimeSpan Validez = TimeSpan.FromHours(24);
    public const string Proposito = "bandeja";

    public static bool CabeAlmacen(long usado, long nuevo) => nuevo > 0 && nuevo <= Maximo && usado + nuevo <= TopeAlmacen;
    public static bool PideContrasena(long tamano) => tamano > ConContrasena;

    private static readonly HashSet<string> Ejecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".jse", ".wsf", ".wsh", ".msi", ".msp",
        ".lnk", ".hta", ".jar", ".reg", ".dll", ".cpl", ".scf", ".url", ".pif", ".msc", ".application", ".gadget",
    };
    public static bool Bloqueada(string nombre, bool permitirEjecutables) => !permitirEjecutables && Ejecutables.Contains(Path.GetExtension(nombre));

    private static readonly HashSet<string> Reservados = new(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    // Solo el nombre, sin rutas ni caracteres raros; null si no queda nada
    public static string? NombreSeguro(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        var n = nombre.Replace('\\', '/'); n = n[(n.LastIndexOf('/') + 1)..];
        var malos = Path.GetInvalidFileNameChars();
        n = new string(n.Select(c => malos.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ').TrimStart('.');
        if (n.Length == 0) return null;
        if (Reservados.Contains(Path.GetFileNameWithoutExtension(n))) n = "_" + n;
        if (n.Length > 120) { var ext = Path.GetExtension(n); n = n[..Math.Max(1, 120 - ext.Length)] + (ext.Length < 20 ? ext : ""); }
        return n;
    }

    public static string Almacen(IConfiguration config)
    {
        var ruta = config["Bandeja:Almacen"] is { Length: > 0 } r && Path.IsPathRooted(r) ? r : Path.Combine(Path.GetTempPath(), "zp-bandeja");
        Directory.CreateDirectory(ruta);
        return ruta;
    }

    public static async Task<long> Usado(AppDbContext db, CancellationToken ct) => await db.Transferencias.Where(t => t.Archivo != "").SumAsync(t => t.Tamano, ct);

    private static async Task Guardar(AppDbContext db, IConfiguration config, Transferencia t, byte[] datos, CancellationToken ct)
    {
        t.Archivo = Guid.NewGuid().ToString("N") + ".bin";
        await File.WriteAllBytesAsync(Path.Combine(Almacen(config), t.Archivo), Boveda.CifrarBytes(datos, Proposito), ct);
        db.Transferencias.Add(t);
        await db.SaveChangesAsync(ct);
    }

    private static void BorrarFichero(IConfiguration config, Transferencia t)
    {
        if (t.Archivo.Length > 0) { try { File.Delete(Path.Combine(Almacen(config), t.Archivo)); } catch (IOException) { /* ya no está */ } }
        t.Archivo = "";
    }

    // Quita del almacén lo caducado y lo ya entregado; lo muy viejo se olvida de la lista
    public static async Task Limpiar(AppDbContext db, IConfiguration config, DateTimeOffset ahora, CancellationToken ct)
    {
        foreach (var t in await db.Transferencias.Where(x => x.Archivo != "" && x.Caduca <= ahora).ToListAsync(ct)) { BorrarFichero(config, t); if (t.Estado is "subido" or "para-descargar") t.Estado = "caducado"; }
        db.Transferencias.RemoveRange(await db.Transferencias.Where(x => x.Archivo == "" && x.Creada < ahora.AddDays(-7)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    public static void MapTransferencias(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pc/bandeja/transferencias").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, IConfiguration config, TimeProvider reloj, CancellationToken ct) =>
        {
            await Limpiar(db, config, reloj.GetUtcNow(), ct);
            return Results.Ok(await db.Transferencias.AsNoTracking().OrderByDescending(t => t.Id).Take(50)
                .Select(t => new { t.Id, t.Direccion, t.Nombre, t.Tamano, t.Estado, t.Creada, t.Caduca, t.Mensaje, descargable = t.Direccion == "movil" && t.Archivo != "", pideContrasena = t.Tamano > ConContrasena }).ToListAsync(ct));
        });

        // la app sube un archivo para el PC
        g.MapPost("/enviar", async (HttpRequest req, AppDbContext db, IConfiguration config, Carpetas carpetas, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } lim) lim.MaxRequestBodySize = Maximo + 1024 * 1024;
            if (!req.HasFormContentType) return Results.BadRequest(new { detalle = "Manda el archivo como formulario." });
            var form = await req.ReadFormAsync(ct);
            if (form.Files.FirstOrDefault() is not { } f) return Results.BadRequest(new { detalle = "No llegó ningún archivo." });
            if (NombreSeguro(f.FileName) is not { } nombre) return Results.BadRequest(new { detalle = "Ese nombre de archivo no vale." });
            if (Bloqueada(nombre, carpetas.PermitirEjecutables)) return Results.BadRequest(new { detalle = "No se envían programas ni scripts al PC (.exe, .bat, .ps1…)." });
            if (!CabeAlmacen(await Usado(db, ct), f.Length)) return Results.BadRequest(new { detalle = f.Length > Maximo ? "Pasa de 100 MB." : "El almacén temporal está lleno: espera a que el PC recoja lo anterior." });
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha." });
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();

            byte[] datos;
            await using (var s = f.OpenReadStream()) { using var m = new MemoryStream(); await s.CopyToAsync(m, ct); datos = m.ToArray(); }
            var ahora = reloj.GetUtcNow();
            var t = new Transferencia { Direccion = "pc", Nombre = nombre, Tamano = datos.Length, Creada = ahora, Caduca = ahora.Add(Validez) };
            await Guardar(db, config, t, datos, ct);
            await OrdenesPc.Encolar(db, reloj, "recibir", t.Id.ToString(), "bandeja · " + Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()), ct);
            _ = avisos.PorTelegram($"💻 Archivo para el PC: «{nombre}» ({datos.Length / 1024.0 / 1024:0.#} MB)\nDesde {Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString())} · {IpCliente.De(ctx)}.");
            return Results.Ok(new { t.Id });
        }).DisableAntiforgery().RequireRateLimiting(Limites.Sensible);

        // bajar lo que el PC ha dejado «para el móvil»; los grandes piden la contraseña otra vez y se borra al bajar
        g.MapGet("/{id:int}/archivo", async (int id, AppDbContext db, IConfiguration config, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            var t = await db.Transferencias.FirstOrDefaultAsync(x => x.Id == id && x.Direccion == "movil", ct);
            if (t is null || t.Archivo.Length == 0 || t.Caduca <= reloj.GetUtcNow()) return Results.NotFound();
            if (PideContrasena(t.Tamano) && !await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            byte[] datos;
            try { datos = Boveda.DescifrarBytes(await File.ReadAllBytesAsync(Path.Combine(Almacen(config), t.Archivo), ct), Proposito); }
            catch (Exception e) when (e is IOException or System.Security.Cryptography.CryptographicException) { return Results.NotFound(); }
            BorrarFichero(config, t); t.Estado = "descargado";
            await db.SaveChangesAsync(ct);
            return Results.File(datos, "application/octet-stream", t.Nombre);
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var t = await db.Transferencias.FindAsync([id], ct);
            if (t is null) return Results.NotFound();
            BorrarFichero(config, t); db.Transferencias.Remove(t);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- lo que usa el agente ----
        var a = app.MapGroup("/api/sistema/agente/bandeja").AllowAnonymous().RequireRateLimiting(Limites.Agente);

        a.MapGet("/{id:int}", async (int id, HttpRequest req, AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            var t = await db.Transferencias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Direccion == "pc" && x.Archivo != "", ct);
            if (t is null) return Results.NotFound();
            try
            {
                var datos = Boveda.DescifrarBytes(await File.ReadAllBytesAsync(Path.Combine(Almacen(config), t.Archivo), ct), Proposito);
                return Results.File(datos, "application/octet-stream", t.Nombre);
            }
            catch (Exception e) when (e is IOException or System.Security.Cryptography.CryptographicException) { return Results.NotFound(); }
        });

        a.MapPost("/{id:int}/hecho", async (int id, ResultadoRecogida r, HttpRequest req, AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            var t = await db.Transferencias.FirstOrDefaultAsync(x => x.Id == id && x.Direccion == "pc", ct);
            if (t is null) return Results.NotFound();
            BorrarFichero(config, t);
            t.Estado = r.Ok ? "en-el-pc" : "error";
            t.Mensaje = (r.Mensaje ?? "").Length > 200 ? r.Mensaje![..200] : r.Mensaje ?? "";
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // el agente sube lo que has dejado en «Para el móvil»
        a.MapPost("/subir", async (string nombre, HttpRequest req, AppDbContext db, IConfiguration config, Carpetas carpetas, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } lim) lim.MaxRequestBodySize = Maximo + 1024;
            if (NombreSeguro(nombre) is not { } limpio) return Results.BadRequest();
            using var m = new MemoryStream();
            await req.Body.CopyToAsync(m, ct);
            if (!CabeAlmacen(await Usado(db, ct), m.Length)) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var ahora = reloj.GetUtcNow();
            var t = new Transferencia { Direccion = "movil", Nombre = limpio, Tamano = m.Length, Estado = "para-descargar", Creada = ahora, Caduca = ahora.Add(Validez) };
            await Guardar(db, config, t, m.ToArray(), ct);
            _ = avisos.PorPush("📥 Archivo del PC para tu móvil", $"«{limpio}» ({m.Length / 1024.0 / 1024:0.#} MB), 24 horas para bajarlo.", "/pc", ct);
            return Results.Ok(new { t.Id });
        }).DisableAntiforgery();
    }
}
