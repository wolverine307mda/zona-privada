using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mda.Api.Atajos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

// Una llamada a la API que no viene de la app: sin cookie de sesión y con un token (atajos del iPhone, agente del PC…).
// Se guarda qué pidió, con qué resultado, qué contestó la API y, si falló, por qué. Nunca se guarda el token.
public class LlamadaExterna
{
    public int Id { get; set; }
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.UtcNow;
    public string Metodo { get; set; } = "";
    public string Ruta { get; set; } = "";
    public string Consulta { get; set; } = "";
    // «Atajo · iPhone», «Agente del PC», «Token desconocido»…
    public string Origen { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Agente { get; set; } = "";
    public int Estado { get; set; }
    public int DuracionMs { get; set; }
    public string Peticion { get; set; } = "";
    public string Respuesta { get; set; } = "";
    public string Error { get; set; } = "";
}

public record LlamadaViva(long Seq, DateTimeOffset Fecha, string Metodo, string Ruta, string Origen, int Estado, int DuracionMs, string Error, bool Guardada);

// Las últimas llamadas externas, solo en memoria (no toca la base): sirve para verlas en directo.
// La pantalla apunta el último número al abrirse y solo pide lo posterior, así que al salir y volver empieza vacía.
public class LlamadasEnVivo
{
    private const int Tope = 300;
    private readonly Queue<LlamadaViva> _cola = new();
    private long _seq;
    private readonly object _cerrojo = new();

    public long Ultimo { get { lock (_cerrojo) return _seq; } }

    public void Apuntar(DateTimeOffset fecha, string metodo, string ruta, string origen, int estado, int ms, string error, bool guardada)
    {
        lock (_cerrojo)
        {
            _cola.Enqueue(new LlamadaViva(++_seq, fecha, metodo, ruta, origen, estado, ms, error, guardada));
            while (_cola.Count > Tope) _cola.Dequeue();
        }
    }

    public IReadOnlyList<LlamadaViva> Desde(long seq)
    {
        lock (_cerrojo) return _cola.Where(l => l.Seq > seq).OrderByDescending(l => l.Seq).ToList();
    }
}

public static partial class LlamadasExternas
{
    public const int MaxTexto = 2000;
    public const int Conservar = 500;
    public static readonly TimeSpan Retencion = TimeSpan.FromDays(60);

    // ¿cuenta como llamada externa? Una ruta de la API, sin la cookie de la app y con una credencial de otra cosa
    public static bool Aplica(HttpRequest req)
    {
        var ruta = req.Path.Value ?? "";
        if (!ruta.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return false;
        if (ruta.Equals("/api/salud", StringComparison.OrdinalIgnoreCase)) return false;
        if (req.Cookies.ContainsKey("mda.sesion")) return false;
        return req.Headers.ContainsKey("Authorization") || req.Headers.ContainsKey("X-Agente-Token");
    }

    // el agente del PC pregunta cada pocos segundos: sus llamadas buenas son ruido, solo interesan las que fallan
    public static bool EsRuido(string ruta, int estado) =>
        ruta.StartsWith("/api/sistema/agente", StringComparison.OrdinalIgnoreCase) && estado is >= 200 and < 400;

    // quita lo que parece un secreto de un cuerpo JSON o de un formulario antes de guardarlo
    public static string Limpiar(string texto) => Secreto().Replace(texto, "$1\"***\"");

    [GeneratedRegex("(\"(?:[^\"]*(?:pass|clave|token|secret|pin|apikey|api_key|authorization|codigo|code)[^\"]*)\"\\s*:\\s*)(\"(?:[^\"\\\\]|\\\\.)*\"|[^,}\\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Secreto();

    public static string Recortar(string s) => s.Length <= MaxTexto ? s : s[..MaxTexto] + "…";

    // el mensaje de un error: lo que diga el JSON de la API (detalle, error, title…) o, si no, el estado HTTP
    public static string MensajeDeError(int estado, string respuesta)
    {
        if (estado < 400) return "";
        try
        {
            using var doc = JsonDocument.Parse(respuesta);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var clave in new[] { "detalle", "error", "message", "mensaje", "detail", "title" })
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (p.NameEquals(clave) && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } t) return Recortar(t);
                if (doc.RootElement.TryGetProperty("errors", out var errores) && errores.ValueKind == JsonValueKind.Object)
                    return Recortar(string.Join(" · ", errores.EnumerateObject().SelectMany(e => e.Value.ValueKind == JsonValueKind.Array ? e.Value.EnumerateArray().Select(m => $"{e.Name}: {m.GetString()}") : [])));
            }
        }
        catch (JsonException) { }
        return estado switch
        {
            401 => "No autorizado: el token falta, es incorrecto o no tiene ese permiso.", 403 => "Prohibido.", 404 => "No existe esa ruta.",
            429 => "Demasiadas peticiones.", >= 500 => "Error interno del servidor.", _ => $"Error {estado}.",
        };
    }

    // Copia lo primero que se escribe en la respuesta sin retrasarla ni cambiarla
    private sealed class Espejo(Stream destino) : Stream
    {
        private readonly MemoryStream _copia = new();
        public string Texto => Encoding.UTF8.GetString(_copia.GetBuffer(), 0, (int)_copia.Length);
        private void Guardar(ReadOnlySpan<byte> datos)
        {
            var sitio = MaxTexto * 2 - (int)_copia.Length; // bytes, con margen para caracteres de varios bytes
            if (sitio > 0) _copia.Write(datos[..Math.Min(sitio, datos.Length)]);
        }
        public override void Write(byte[] buffer, int offset, int count) { Guardar(buffer.AsSpan(offset, count)); destino.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Guardar(buffer); destino.Write(buffer); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { Guardar(buffer.Span); await destino.WriteAsync(buffer, ct); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override Task FlushAsync(CancellationToken ct) => destino.FlushAsync(ct);
        public override void Flush() => destino.Flush();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static string Cortar(string s) => s.Length <= MaxTexto ? s : s[..MaxTexto];

    public static IApplicationBuilder UseRegistroLlamadas(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (!Aplica(ctx.Request)) { await next(); return; }

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        // el cuerpo de la petición: se lee un trozo y se deja listo para que la API lo lea entero
        var peticion = "";
        if (ctx.Request.ContentLength is > 0 && ctx.Request.ContentType is { } tipo && (tipo.Contains("json", StringComparison.OrdinalIgnoreCase) || tipo.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || tipo.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Request.EnableBuffering();
            var buffer = new byte[Math.Min(MaxTexto * 2, (int)Math.Min(ctx.Request.ContentLength.Value, int.MaxValue))];
            var leidos = 0;
            while (leidos < buffer.Length) { var n = await ctx.Request.Body.ReadAsync(buffer.AsMemory(leidos)); if (n == 0) break; leidos += n; }
            ctx.Request.Body.Position = 0;
            peticion = Cortar(Limpiar(Encoding.UTF8.GetString(buffer, 0, leidos)));
        }

        var original = ctx.Response.Body;
        var espejo = new Espejo(original);
        ctx.Response.Body = espejo;
        string? excepcion = null;
        try { await next(); }
        catch (Exception e) { excepcion = e.Message; throw; }
        finally
        {
            ctx.Response.Body = original;
            reloj.Stop();
            var estado = excepcion is not null && ctx.Response.StatusCode < 400 ? 500 : ctx.Response.StatusCode;
            await Guardar(ctx, estado, (int)reloj.ElapsedMilliseconds, peticion, espejo.Texto, excepcion);
        }
    });

    private static async Task Guardar(HttpContext ctx, int estado, int ms, string peticion, string cuerpo, string? excepcion)
    {
        try
        {
            var texto = ctx.Response.ContentType is { } t && (t.Contains("json", StringComparison.OrdinalIgnoreCase) || t.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                ? cuerpo : cuerpo.Length > 0 ? "[contenido no es texto]" : "";
            var ruta = ctx.Request.Path.Value ?? "";
            var origen = ctx.Items["tokenAtajo"] is TokenAtajo ta ? $"Atajo · {ta.Nombre}"
                : ruta.StartsWith("/api/sistema/agente", StringComparison.OrdinalIgnoreCase) ? "Agente del PC"
                : ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer mda_", StringComparison.Ordinal) ? "Token de atajo no válido"
                : "Credencial desconocida";
            var l = new LlamadaExterna
            {
                Metodo = ctx.Request.Method, Ruta = ruta, Consulta = Cortar(Limpiar(ConsultaSinSecretos(ctx.Request.QueryString.Value ?? ""))),
                Origen = origen, Ip = IpCliente.De(ctx), Agente = ctx.Request.Headers.UserAgent.ToString(),
                Estado = estado, DuracionMs = ms, Peticion = peticion, Respuesta = Cortar(Limpiar(texto)),
                Error = excepcion is not null ? Recortar(excepcion) : MensajeDeError(estado, texto),
            };
            if (l.Agente.Length > 400) l.Agente = l.Agente[..400];
            // en directo entran todas; en la base solo las que no son ruido (las buenas del agente no se guardan)
            var guardar = !EsRuido(ruta, estado);
            ctx.RequestServices.GetRequiredService<LlamadasEnVivo>().Apuntar(l.Fecha, l.Metodo, l.Ruta, l.Origen, l.Estado, l.DuracionMs, l.Error, guardar);
            if (!guardar) return;
            using var scope = ctx.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LlamadasExternas.Add(l);
            await db.SaveChangesAsync();
            // se guardan las 500 últimas y nada con más de 60 días
            var limite = DateTimeOffset.UtcNow - Retencion;
            var sobran = await db.LlamadasExternas.Where(x => x.Fecha < limite).OrderBy(x => x.Id).Take(200).ToListAsync();
            var exceso = await db.LlamadasExternas.OrderByDescending(x => x.Id).Skip(Conservar).Take(200).ToListAsync();
            sobran.AddRange(exceso.Where(e => sobran.All(s => s.Id != e.Id)));
            if (sobran.Count > 0) { db.LlamadasExternas.RemoveRange(sobran); await db.SaveChangesAsync(); }
        }
        catch (Exception e) when (e is DbUpdateException or InvalidOperationException)
        {
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Llamadas").LogWarning(e, "No se pudo registrar la llamada externa");
        }
    }

    // en la consulta (?a=b) se tapa el valor de lo que parezca un secreto
    private static string ConsultaSinSecretos(string q) =>
        Regex.Replace(q, "([?&][^=&]*(?:pass|clave|token|secret|pin|key|code)[^=&]*=)[^&]*", "$1***", RegexOptions.IgnoreCase);

    public static void MapLlamadasExternas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/seguridad/llamadas").RequireAuthorization();

        // lista ligera (sin cuerpos). Filtros: estado (todas | fallos | ok), origen, desde (fecha) y buscar (texto en la ruta o el error)
        g.MapGet("/", async (string? estado, string? origen, DateTimeOffset? desde, string? buscar, AppDbContext db, GeoIp geo) =>
        {
            var q = Filtrar(db.LlamadasExternas.AsNoTracking(), estado, origen, desde, buscar);
            var lista = await q.OrderByDescending(l => l.Id).Take(100).ToListAsync();
            var origenes = await db.LlamadasExternas.AsNoTracking().Select(l => l.Origen).Distinct().OrderBy(o => o).ToListAsync();
            return Results.Ok(new
            {
                origenes,
                coinciden = await q.CountAsync(),
                total = await db.LlamadasExternas.CountAsync(),
                fallos = await db.LlamadasExternas.CountAsync(l => l.Estado >= 400),
                llamadas = lista.Select(l => new
                {
                    l.Id, l.Fecha, l.Metodo, l.Ruta, l.Consulta, l.Origen, l.Ip, l.Estado, l.DuracionMs, l.Error,
                    Dispositivo = Dispositivo.Nombre(l.Agente), Lugar = geo.Buscar(l.Ip) is { } ge ? (ge.Ciudad.Length > 0 ? $"{ge.Ciudad}, {ge.Pais}" : ge.Pais) : null,
                }),
            });
        });

        // una llamada entera: lo que se pidió y lo que se contestó
        g.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.LlamadasExternas.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id) is { } l
                ? Results.Ok(new { l.Id, l.Peticion, l.Respuesta, l.Error, l.Agente })
                : Results.NotFound());

        // en directo: sin `desde` solo dice por dónde va la cuenta; con `desde`, lo que ha llegado después
        g.MapGet("/vivo", (long? desde, LlamadasEnVivo vivo) =>
            Results.Ok(new { ultimo = vivo.Ultimo, llamadas = desde is { } d ? vivo.Desde(d) : [] }));

        // limpiar: sin filtros borra todo; con los mismos filtros de la lista borra solo lo que coincide;
        // `antesDe` borra lo anterior a esa fecha (p. ej. «todo menos lo de hoy»)
        g.MapDelete("/", async (string? estado, string? origen, DateTimeOffset? desde, string? buscar, DateTimeOffset? antesDe, AppDbContext db) =>
        {
            var q = Filtrar(db.LlamadasExternas.AsQueryable(), estado, origen, desde, buscar);
            if (antesDe is { } hasta) q = q.Where(l => l.Fecha < hasta);
            var lista = await q.ToListAsync();
            db.LlamadasExternas.RemoveRange(lista);
            await db.SaveChangesAsync();
            return Results.Ok(new { borradas = lista.Count });
        });
    }

    private static IQueryable<LlamadaExterna> Filtrar(IQueryable<LlamadaExterna> q, string? estado, string? origen, DateTimeOffset? desde, string? buscar)
    {
        if (estado == "fallos") q = q.Where(l => l.Estado >= 400);
        else if (estado == "ok") q = q.Where(l => l.Estado < 400);
        if (!string.IsNullOrWhiteSpace(origen)) q = q.Where(l => l.Origen == origen);
        if (desde is { } d) q = q.Where(l => l.Fecha >= d);
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var t = buscar.Trim();
            q = q.Where(l => l.Ruta.Contains(t) || l.Error.Contains(t) || l.Origen.Contains(t));
        }
        return q;
    }
}
