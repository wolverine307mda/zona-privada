using System.Net;
using System.Net.Sockets;
using System.Text;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Mda.Api.Pc;

// Una foto tomada bajo demanda: la pantalla del PC, su cámara web o una cámara IP de casa. Se guarda CIFRADA (AES-256-GCM, subclave propia) y se borra sola
// pasados unos días. Es para tus propios equipos y tu casa: viene apagada, no graba vídeo ni audio, cada foto queda apuntada con su motivo y te llega un aviso.
public class CapturaPc
{
    public int Id { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public string Origen { get; set; } = "";   // pantalla, camara, camara-ip
    public string Motivo { get; set; } = "";   // manual, panico, intruso
    public int Ancho { get; set; }
    public int Alto { get; set; }
    public int Tamano { get; set; }            // bytes de la imagen sin cifrar
    public byte[] Datos { get; set; } = [];    // la imagen, cifrada
}

public record AjustesCapturas(bool Activo, bool EnPanico, bool Intruso, bool CamaraIp, int Dias);
public record EntradaAjustesCapturas(bool? Activo, bool? EnPanico, bool? Intruso, string? CamaraIpUrl, int? Dias);
public record PedirCaptura(string? Origen);

public static class Capturas
{
    public const string ClaveActivo = "capturas-activo", ClavePanico = "capturas-panico", ClaveIntruso = "capturas-intruso", ClaveDias = "capturas-dias", ClaveIp = "capturas-ip";
    public static readonly string[] Origenes = ["pantalla", "camara", "camara-ip"];
    public static readonly string[] Motivos = ["manual", "panico", "intruso"];
    public const string Proposito = "capturas";
    public const int DiasPorDefecto = 30, MaxBytesEntrada = 4 * 1024 * 1024, LadoMaximo = 1280, FallosParaFoto = 3;
    public static readonly TimeSpan VentanaFallos = TimeSpan.FromMinutes(10);

    public static bool ParametroValido(string? p, out string origen, out string motivo)
    {
        var partes = (p ?? "").Split(':');
        (origen, motivo) = (partes[0], partes.Length > 1 ? partes[1] : "manual");
        return partes.Length <= 2 && origen is "pantalla" or "camara" && Motivos.Contains(motivo);
    }

    public static async Task<AjustesCapturas> Leer(AppDbContext db, CancellationToken ct = default)
    {
        var a = await db.Ajustes.AsNoTracking().Where(x => x.Clave.StartsWith("capturas-")).ToDictionaryAsync(x => x.Clave, x => x.Valor, ct);
        bool Si(string k) => a.GetValueOrDefault(k) == "si";
        var dias = int.TryParse(a.GetValueOrDefault(ClaveDias), out var d) && d is >= 1 and <= 365 ? d : DiasPorDefecto;
        return new AjustesCapturas(Si(ClaveActivo), Si(ClavePanico), Si(ClaveIntruso), !string.IsNullOrEmpty(a.GetValueOrDefault(ClaveIp)), dias);
    }

    public static async Task Poner(AppDbContext db, string clave, string valor, CancellationToken ct = default)
    {
        var f = await db.Ajustes.FindAsync([clave], ct);
        if (f is null) db.Ajustes.Add(new Ajuste { Clave = clave, Valor = valor }); else f.Valor = valor;
    }

    // Una cámara IP de casa: solo direcciones IP locales (127.x, 10.x, 172.16-31.x, 192.168.x, 169.254.x o IPv6 local), nunca un nombre ni una IP pública:
    // así la API no se puede usar para pedir cosas a Internet ni a otros servicios de la red por su nombre.
    public static bool UrlCamaraIpValida(string? url, out string motivo)
    {
        motivo = "";
        if (string.IsNullOrWhiteSpace(url) || url.Length > 300 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) { motivo = "Una dirección http o https."; return false; }
        if (!IPAddress.TryParse(u.Host.Trim('[', ']'), out var ip) || !EsLocal(ip)) { motivo = "Solo direcciones IP de tu red local (192.168.x.x, 10.x.x.x…): sin nombres ni IP públicas."; return false; }
        return true;
    }

    public static bool EsLocal(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.AddressFamily == AddressFamily.InterNetworkV6 && (ip.GetAddressBytes()[0] & 0xFE) == 0xFC)) return true;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    // Deja la imagen en JPEG de como mucho 1280 px de lado, sin metadatos; null si lo recibido no es una imagen.
    public static (byte[] Jpeg, int Ancho, int Alto)? Normalizar(byte[] entrada)
    {
        if (entrada.Length is 0 or > MaxBytesEntrada) return null;
        try
        {
            using var origen = SKBitmap.Decode(entrada);
            if (origen is null || origen.Width < 1 || origen.Height < 1) return null;
            var escala = Math.Min(1.0, (double)LadoMaximo / Math.Max(origen.Width, origen.Height));
            var (w, h) = (Math.Max(1, (int)Math.Round(origen.Width * escala)), Math.Max(1, (int)Math.Round(origen.Height * escala)));
            using var final = escala < 1 ? origen.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) : origen.Copy();
            using var imagen = SKImage.FromBitmap(final);
            using var datos = imagen.Encode(SKEncodedImageFormat.Jpeg, 75);
            return (datos.ToArray(), final.Width, final.Height);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return null; }
    }

    public static async Task<CapturaPc?> Guardar(AppDbContext db, TimeProvider reloj, string origen, string motivo, byte[] recibido, CancellationToken ct = default)
    {
        if (!Origenes.Contains(origen) || !Motivos.Contains(motivo) || Normalizar(recibido) is not var (jpeg, ancho, alto)) return null;
        var ahora = reloj.GetUtcNow();
        var c = new CapturaPc { Fecha = ahora, Origen = origen, Motivo = motivo, Ancho = ancho, Alto = alto, Tamano = jpeg.Length, Datos = Boveda.CifrarBytes(jpeg, Proposito) };
        db.CapturasPc.Add(c);
        // las viejas fuera (según los días elegidos)
        var limite = ahora.AddDays(-(await Leer(db, ct)).Dias);
        db.CapturasPc.RemoveRange(await db.CapturasPc.Where(x => x.Fecha < limite).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        return c;
    }

    // Trae una instantánea de la cámara IP (la dirección guardada va cifrada); null si no responde o no es una imagen
    public static async Task<(byte[]? Imagen, string? Error)> TraerCamaraIp(AppDbContext db, IHttpClientFactory http, CancellationToken ct)
    {
        var guardada = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(x => x.Clave == ClaveIp, ct))?.Valor;
        if (string.IsNullOrEmpty(guardada)) return (null, "No hay ninguna cámara IP configurada.");
        var url = Boveda.Descifrar(guardada, Proposito);
        if (!UrlCamaraIpValida(url, out _)) return (null, "La dirección de la cámara ya no es válida.");
        var u = new Uri(url);
        try
        {
            using var cliente = http.CreateClient("camara-ip");
            cliente.Timeout = TimeSpan.FromSeconds(8);
            using var peticion = new HttpRequestMessage(HttpMethod.Get, new UriBuilder(u) { UserName = "", Password = "" }.Uri);
            if (u.UserInfo.Length > 0) peticion.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(u.UserInfo))));
            using var r = await cliente.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!r.IsSuccessStatusCode) return (null, $"La cámara responde {(int)r.StatusCode}.");
            if (r.Content.Headers.ContentLength > MaxBytesEntrada) return (null, "La imagen de la cámara es demasiado grande.");
            await using var s = await r.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buf = new byte[81920]; int n;
            while ((n = await s.ReadAsync(buf, ct)) > 0) { ms.Write(buf, 0, n); if (ms.Length > MaxBytesEntrada) return (null, "La imagen de la cámara es demasiado grande."); }
            return (ms.ToArray(), null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return (null, "La cámara no responde."); }
    }

    // ---- disparadores automáticos (opt-in en los ajustes) ----

    // al activar el pánico: una foto de la pantalla y otra de la cámara del PC
    public static async Task EnPanico(AppDbContext db, TimeProvider reloj, CancellationToken ct = default)
    {
        var a = await Leer(db, ct);
        if (!a.Activo || !a.EnPanico) return;
        await OrdenesPc.Encolar(db, reloj, "captura", "pantalla:panico", "pánico", ct);
        await OrdenesPc.Encolar(db, reloj, "captura", "camara:panico", "pánico", ct);
    }

    // tras un intento de entrar fallado: si ya van varios seguidos, una foto de la cámara (una vez cada 10 minutos como mucho)
    public static async Task TrasFallo(AppDbContext db, TimeProvider reloj, CancellationToken ct = default)
    {
        var a = await Leer(db, ct);
        if (!a.Activo || !a.Intruso) return;
        var ahora = reloj.GetUtcNow();
        var desde = ahora - VentanaFallos;
        if (await db.Accesos.CountAsync(x => !x.Exito && x.Fecha > desde, ct) < FallosParaFoto) return;
        if (await db.CapturasPc.AnyAsync(x => x.Motivo == "intruso" && x.Fecha > desde, ct) || await db.OrdenesPc.AnyAsync(x => x.Tipo == "captura" && x.Parametro.EndsWith(":intruso") && x.Creada > desde, ct)) return;
        await OrdenesPc.Encolar(db, reloj, "captura", "camara:intruso", "intentos fallidos de entrar", ct);
    }

    public static string TextoOrigen(string origen) => origen switch { "pantalla" => "la pantalla del PC", "camara" => "la cámara del PC", "camara-ip" => "la cámara IP", _ => origen };
}

public static class CapturasEndpoints
{
    public static void MapCapturas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pc/capturas").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(new
        {
            ajustes = await Capturas.Leer(db, ct),
            capturas = await db.CapturasPc.AsNoTracking().OrderByDescending(c => c.Fecha).Take(60).Select(c => new { c.Id, c.Fecha, c.Origen, c.Motivo, c.Ancho, c.Alto, c.Tamano }).ToListAsync(ct),
        }));

        // la imagen descifrada, solo para ti (nunca en cachés)
        g.MapGet("/{id:int}/imagen", async (int id, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var c = await db.CapturasPc.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            byte[] jpeg;
            try { jpeg = Boveda.DescifrarBytes(c.Datos, Capturas.Proposito); }
            catch (System.Security.Cryptography.CryptographicException) { return Results.Problem("La foto no se pudo descifrar (¿cambió la clave maestra?).", statusCode: 500); }
            ctx.Response.Headers.CacheControl = "private, no-store";
            return Results.File(jpeg, "image/jpeg");
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var c = await db.CapturasPc.FindAsync([id], ct);
            if (c is null) return Results.NotFound();
            db.CapturasPc.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("/", async (AppDbContext db, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            db.CapturasPc.RemoveRange(db.CapturasPc);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Los ajustes: encender y apagar todo, los disparadores y la cámara IP. Cambiarlos pide confirmar que eres tú.
        g.MapPut("/ajustes", async (EntradaAjustesCapturas e, AppDbContext db, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            var mal = new Dictionary<string, string[]>();
            if (e.Dias is { } d && d is < 1 or > 365) mal["dias"] = ["Los días van de 1 a 365."];
            if (e.CamaraIpUrl is { Length: > 0 } u && !Capturas.UrlCamaraIpValida(u, out var motivo)) mal["camaraIpUrl"] = [motivo];
            if (mal.Count > 0) return Results.ValidationProblem(mal);
            if (e.Activo is { } a) await Capturas.Poner(db, Capturas.ClaveActivo, a ? "si" : "no", ct);
            if (e.EnPanico is { } p) await Capturas.Poner(db, Capturas.ClavePanico, p ? "si" : "no", ct);
            if (e.Intruso is { } i) await Capturas.Poner(db, Capturas.ClaveIntruso, i ? "si" : "no", ct);
            if (e.Dias is { } dd) await Capturas.Poner(db, Capturas.ClaveDias, dd.ToString(), ct);
            if (e.CamaraIpUrl is { } url) await Capturas.Poner(db, Capturas.ClaveIp, url.Length == 0 ? "" : Boveda.Cifrar(url.Trim(), Capturas.Proposito), ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await Capturas.Leer(db, ct));
        });

        // «Foto ahora». Pantalla y cámara del PC las hace el agente (se devuelve su orden para seguirla); la cámara IP, la propia API al momento.
        g.MapPost("/capturar", async (PedirCaptura e, AppDbContext db, EstadoAgente agente, IHttpClientFactory http, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            var origen = (e.Origen ?? "").ToLowerInvariant();
            if (!Capturas.Origenes.Contains(origen)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["origen"] = ["Elige pantalla, cámara o cámara IP."] });
            if (!(await Capturas.Leer(db, ct)).Activo) return Results.Conflict(new { detalle = "Las fotos bajo demanda están apagadas: actívalas en sus ajustes." });
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            var desde = Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()) + " · " + IpCliente.De(ctx);

            if (origen == "camara-ip")
            {
                var (imagen, error) = await Capturas.TraerCamaraIp(db, http, ct);
                if (imagen is null) return Results.Problem(error, statusCode: StatusCodes.Status502BadGateway);
                if (await Capturas.Guardar(db, reloj, origen, "manual", imagen, ct) is not { } c) return Results.Problem("Lo que devolvió la cámara no es una imagen.", statusCode: StatusCodes.Status502BadGateway);
                _ = avisos.PorTelegram($"📷 Foto de {Capturas.TextoOrigen(origen)} desde {desde}.");
                return Results.Ok(new { id = c.Id, orden = (int?)null });
            }
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha (agente\\arrancar.cmd)." });
            var o = await OrdenesPc.Encolar(db, reloj, "captura", $"{origen}:manual", desde, ct);
            _ = avisos.PorTelegram($"📷 Orden al PC: foto de {Capturas.TextoOrigen(origen)}.\nDesde {desde}. Si no has sido tú, cierra sesiones en Seguridad.");
            return Results.Ok(new { id = (int?)null, orden = o.Id });
        }).RequireRateLimiting(Limites.Sensible);

        // ---- lo que usa el agente: deja la imagen que acaba de hacer (token compartido, sin pasar por el proxy) ----
        app.MapPost("/api/sistema/agente/captura", async (string origen, string? motivo, HttpRequest req, AppDbContext db, TimeProvider reloj, IAvisos avisos, IConfiguration config, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            if (req.ContentLength is null or 0 or > Capturas.MaxBytesEntrada) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            if (!(await Capturas.Leer(db, ct)).Activo) return Results.Conflict(new { detalle = "Las fotos bajo demanda están apagadas." });
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms, ct);
            var m = Capturas.Motivos.Contains(motivo ?? "") ? motivo! : "manual";
            if (await Capturas.Guardar(db, reloj, origen, m, ms.ToArray(), ct) is not { } c) return Results.BadRequest();
            var texto = $"Se ha tomado una foto de {Capturas.TextoOrigen(origen)} ({m switch { "panico" => "por el pánico", "intruso" => "por intentos de entrar fallidos", _ => "a petición tuya" }}).";
            await avisos.Avisar("pc", "📷 Foto del PC", texto, textoTelegram: $"📷 {texto}", ct: ct);
            return Results.Ok(new { c.Id });
        }).AllowAnonymous().RequireRateLimiting(Limites.Agente);
    }
}
