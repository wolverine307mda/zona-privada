using System.Text.Json;
using System.Text.RegularExpressions;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// «Qué ocupa» (solo lectura): pide al agente las carpetas más grandes de una raíz y guarda la última respuesta.
// El agente se queda en unidades locales y se corta a los 60 s; aquí se valida la ruta antes de encolar la orden.
public record EspacioEntrada(string? Raiz);
public record CarpetaEspacio(string Nombre, long Bytes);
public record ResultadoEspacio(string Raiz, DateTimeOffset Hora, List<CarpetaEspacio> Carpetas, long TotalBytes, bool Parcial, int Segundos);

public static partial class EspacioPc
{
    public const string Clave = "pc-espacio";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^[A-Za-z]:(\\[^\\/:*?""<>|\x00-\x1f]+)*\\?$")]
    private static partial Regex RutaLocal();

    // Una ruta de unidad local: «C:\», «D:\Juegos». Nada de red (\\equipo\…), relativas, «..» ni caracteres raros. Devuelve la ruta limpia o null.
    public static string? RaizValida(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return null;
        var r = ruta.Trim().Replace('/', '\\');
        if (r.Length is < 2 or > 240 || r.Contains("..") || !RutaLocal().IsMatch(r)) return null;
        r = char.ToUpperInvariant(r[0]) + r[1..];
        r = r.TrimEnd('\\');
        return r.Length == 2 ? r + "\\" : r;
    }

    public static async Task<ResultadoEspacio?> Ultimo(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor;
        try { return v is null ? null : JsonSerializer.Deserialize<ResultadoEspacio>(v, Json); } catch (JsonException) { return null; }
    }

    public static string Formato(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };

    public static void MapEspacio(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pc/espacio").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(await Ultimo(db, ct)));

        g.MapPost("/", async (EspacioEntrada e, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            if (RaizValida(e.Raiz) is not { } raiz)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["raiz"] = ["Pon una carpeta de una unidad del PC, como C:\\ o D:\\Juegos."] });
            await OrdenesPc.Encolar(db, reloj, "espacio", raiz, "qué ocupa", ct);
            return Results.Accepted(value: new { raiz });
        });

        // lo manda el agente al terminar de contar
        app.MapPost("/api/sistema/agente/espacio", async (HttpRequest req, ResultadoEspacio e, AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            if (RaizValida(e.Raiz) is null || e.Carpetas is null || e.Carpetas.Count > 50 || e.Carpetas.Any(c => c.Nombre is null || c.Nombre.Length > 260 || c.Bytes < 0))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["raiz"] = ["Resultado que no cuadra."] });
            var fila = await db.Ajustes.FirstOrDefaultAsync(a => a.Clave == Clave, ct);
            var valor = JsonSerializer.Serialize(e, Json);
            if (fila is null) db.Ajustes.Add(new Ajuste { Clave = Clave, Valor = valor }); else fila.Valor = valor;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting(Limites.Agente);
    }
}
