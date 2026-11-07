using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Conexiones;

// Letras de lo que suena. Se piden a LRCLIB (comunitaria, gratis, sin clave) por título, artista, álbum y duración y se guardan en la base para no repetir:
// con letra sincronizada (formato LRC) se resalta la línea que suena; si solo hay letra sin tiempos se enseña estática; si no hay, se dice y se vuelve a mirar en una semana.
public class Letra
{
    public int Id { get; set; }
    public string CancionId { get; set; } = "";
    public string Sincronizada { get; set; } = "";
    public string Plana { get; set; } = "";
    public bool Encontrada { get; set; }
    public DateTimeOffset Fecha { get; set; }
}

public record LineaLetra(int Ms, string Texto);

public static partial class Lrc
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex Tiempo();

    // Pura: «[01:23.45] texto» → líneas con su milisegundo, ordenadas. Una línea con varios tiempos («[00:10.00][01:30.00] estribillo») se repite;
    // las etiquetas de metadatos ([ar:…], [ti:…]) y las líneas sin tiempo se ignoran; el texto vacío se conserva (es una pausa instrumental).
    public static List<LineaLetra> Parsear(string? lrc)
    {
        var l = new List<LineaLetra>();
        if (string.IsNullOrWhiteSpace(lrc)) return l;
        foreach (var linea in lrc.Split('\n'))
        {
            var ms = Tiempo().Matches(linea);
            if (ms.Count == 0) continue;
            var texto = Tiempo().Replace(linea, "").Trim();
            foreach (Match m in ms)
            {
                var frac = m.Groups[3].Success ? int.Parse(m.Groups[3].Value.PadRight(3, '0')[..3], CultureInfo.InvariantCulture) : 0;
                l.Add(new LineaLetra((int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)) * 1000 + frac, texto));
            }
        }
        return l.OrderBy(x => x.Ms).ToList();
    }

    // Pura: el índice de la línea que suena en ese momento (-1 si todavía no ha empezado la primera)
    public static int LineaActual(IReadOnlyList<LineaLetra> lineas, int progresoMs)
    {
        var lo = 0; var hi = lineas.Count - 1; var r = -1;
        while (lo <= hi) { var mid = (lo + hi) / 2; if (lineas[mid].Ms <= progresoMs) { r = mid; lo = mid + 1; } else hi = mid - 1; }
        return r;
    }
}

public sealed class ServicioLetras(IHttpClientFactory http, ILogger<ServicioLetras> log)
{
    public static readonly TimeSpan CadaCuantoReintentar = TimeSpan.FromDays(7);

    public async Task<Letra> Buscar(AppDbContext db, string id, string nombre, string artista, string? album, int duracionMs, DateTimeOffset ahora, CancellationToken ct)
    {
        var guardada = await db.Letras.FirstOrDefaultAsync(l => l.CancionId == id, ct);
        if (guardada is not null && (guardada.Encontrada || ahora - guardada.Fecha < CadaCuantoReintentar)) return guardada;

        var inv = CultureInfo.InvariantCulture;
        var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(nombre)}&artist_name={Uri.EscapeDataString(artista)}"
                  + (string.IsNullOrWhiteSpace(album) ? "" : $"&album_name={Uri.EscapeDataString(album)}")
                  + (duracionMs > 0 ? $"&duration={((int)Math.Round(duracionMs / 1000.0, MidpointRounding.AwayFromZero)).ToString(inv)}" : "");
        string sincro = "", plana = "";
        try
        {
            using var res = await http.CreateClient("lrclib").GetAsync(url, ct);
            if (res.IsSuccessStatusCode)
            {
                var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
                sincro = j.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
                plana = j.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            }
            else if (res.StatusCode != System.Net.HttpStatusCode.NotFound) return guardada ?? new Letra { CancionId = id };   // un fallo del servicio no se guarda como «no hay letra»
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            log.LogWarning(e, "No se pudo pedir la letra de «{Nombre}»", nombre);
            return guardada ?? new Letra { CancionId = id };
        }
        if (guardada is null) db.Letras.Add(guardada = new Letra { CancionId = id });
        guardada.Sincronizada = sincro.Length > 40000 ? "" : sincro; guardada.Plana = plana.Length > 40000 ? "" : plana;
        guardada.Encontrada = sincro.Length > 0 || plana.Length > 0; guardada.Fecha = ahora;
        await db.SaveChangesAsync(ct);
        return guardada;
    }
}

public static class LetrasEndpoints
{
    public static void MapLetras(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/musica/letra", async (string id, string nombre, string artista, string? album, int? duracion, ServicioLetras letras, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 40 || string.IsNullOrWhiteSpace(nombre) || nombre.Length > 200 || artista?.Length > 400)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["id"] = ["Falta la canción."] });
            var l = await letras.Buscar(db, id, nombre.Trim(), (artista ?? "").Split(',')[0].Trim(), album, Math.Clamp(duracion ?? 0, 0, 3_600_000), reloj.GetUtcNow(), ct);
            var lineas = Lrc.Parsear(l.Sincronizada);
            var estado = lineas.Count > 0 ? "sincronizada" : l.Plana.Length > 0 ? "plana" : "nada";
            return Results.Ok(new { estado, lineas, plana = estado == "plana" ? l.Plana : null, fuente = "LRCLIB" });
        }).RequireAuthorization();
    }
}
