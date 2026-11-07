using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mda.Api.Agenda;
using Mda.Api.Contexto;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Conexiones;

// Listas por estado de ánimo: «Concentración», «Cocina», «Entrenar», «Dormir», «Fiesta»… Cada momento lleva la lista de Spotify que tú eliges, y un toque
// la pone a sonar (mezclada y con el volumen que quieras). Requiere Spotify Premium y un aparato con Spotify abierto. También se puede sugerir según la hora.
public class MomentoMusical
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Icono { get; set; } = "🎵";
    public string PlaylistUri { get; set; } = "";
    public string PlaylistNombre { get; set; } = "";
    public string? Desde { get; set; }
    public string? Hasta { get; set; }
    public int? Volumen { get; set; }
    public bool Mezclar { get; set; } = true;
    public int Orden { get; set; }
}

public record MomentoEntrada(string? Nombre, string? Icono, string? PlaylistUri, string? PlaylistNombre, string? Desde, string? Hasta, int? Volumen, bool? Mezclar, bool QuitarVolumen = false);
public record MomentoAtajo(string? Nombre);

public static partial class Momentos
{
    public const int Maximo = 12;

    public static readonly (string Nombre, string Icono, string? Desde, string? Hasta, int? Volumen)[] DeSerie =
    [
        ("Concentración", "🎯", "09:00", "13:00", 25), ("Cocina", "🍳", "13:00", "15:00", 45),
        ("Entrenar", "💪", "17:00", "20:00", 70), ("Dormir", "🌙", "22:30", "02:00", 15), ("Fiesta", "🎉", null, null, 80),
    ];

    [GeneratedRegex(@"^spotify:playlist:[A-Za-z0-9]{22}$")]
    private static partial Regex UriLista();
    [GeneratedRegex(@"open\.spotify\.com/(?:intl-[a-z]{2}/)?playlist/([A-Za-z0-9]{22})")]
    private static partial Regex UrlLista();

    // Pura: acepta «spotify:playlist:ID» o la dirección de la lista que se copia desde Spotify; devuelve la URI normalizada o null
    public static string? NormalizarLista(string? entrada)
    {
        var s = (entrada ?? "").Trim();
        if (UriLista().IsMatch(s)) return s;
        return UrlLista().Match(s) is { Success: true } m ? $"spotify:playlist:{m.Groups[1].Value}" : null;
    }

    // Pura: el momento que toca a esta hora (el primero con franja que la contenga y lista elegida), o null
    public static MomentoMusical? Sugerido(IEnumerable<MomentoMusical> momentos, TimeOnly hora) =>
        momentos.OrderBy(m => m.Orden).FirstOrDefault(m => m.PlaylistUri.Length > 0 && (m.Desde is not null || m.Hasta is not null) && ServicioContexto.EnFranja(m.Desde, m.Hasta, hora));

    public static Dictionary<string, string[]> Validar(MomentoEntrada e, bool nuevo)
    {
        var mal = new Dictionary<string, string[]>();
        if (nuevo || e.Nombre is not null) { if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 30) mal["nombre"] = ["Ponle un nombre (máximo 30 letras)."]; }
        if (e.Icono is { Length: > 8 }) mal["icono"] = ["Un emoji, nada más."];
        if (!string.IsNullOrEmpty(e.PlaylistUri) && NormalizarLista(e.PlaylistUri) is null) mal["playlistUri"] = ["Pega la dirección de la lista de Spotify (open.spotify.com/playlist/…) o su URI."];
        if (!string.IsNullOrEmpty(e.Desde) && !ServicioContexto.HoraValida(e.Desde)) mal["desde"] = ["La hora va como 09:00."];
        if (!string.IsNullOrEmpty(e.Hasta) && !ServicioContexto.HoraValida(e.Hasta)) mal["hasta"] = ["La hora va como 13:00."];
        if (e.Volumen is < 0 or > 100) mal["volumen"] = ["De 0 a 100."];
        return mal;
    }

    public static async Task Sembrar(AppDbContext db, CancellationToken ct)
    {
        if (await db.Momentos.AnyAsync(ct)) return;
        var i = 0;
        foreach (var (n, ic, d, h, v) in DeSerie) db.Momentos.Add(new MomentoMusical { Nombre = n, Icono = ic, Desde = d, Hasta = h, Volumen = v, Orden = i++ });
        await db.SaveChangesAsync(ct);
    }

    // Pone el momento a sonar. Devuelve (ok, mensaje, estado http para los errores explicados).
    public static async Task<(bool Ok, string Mensaje, int Estado)> Reproducir(ServicioConexiones con, MomentoMusical m, CancellationToken ct)
    {
        if (m.PlaylistUri.Length == 0) return (false, $"«{m.Nombre}» aún no tiene lista: elige una en Música.", 409);
        var sp = ServicioConexiones.Spotify;
        using var play = await con.Llamar(sp, HttpMethod.Put, "https://api.spotify.com/v1/me/player/play", new { context_uri = m.PlaylistUri }, ct);
        if (play is null) return (false, "Spotify no está conectado.", 409);
        if (!play.IsSuccessStatusCode)
            return play.StatusCode switch
            {
                HttpStatusCode.NotFound => (false, "No hay ningún aparato con Spotify abierto: ábrelo en el móvil o en el PC y vuelve a probar.", 404),
                HttpStatusCode.Forbidden => (false, "Spotify no deja controlar la reproducción con esta cuenta (hace falta Premium).", 403),
                HttpStatusCode.TooManyRequests => (false, "Spotify pide esperar un poco.", 429),
                _ => (false, "Spotify no ha respondido bien.", 502),
            };
        // el mezclado y el volumen son un extra: si fallan, la música ya suena
        using var _ = await con.Llamar(sp, HttpMethod.Put, $"https://api.spotify.com/v1/me/player/shuffle?state={(m.Mezclar ? "true" : "false")}", ct: ct);
        if (m.Volumen is { } v) { using var __ = await con.Llamar(sp, HttpMethod.Put, $"https://api.spotify.com/v1/me/player/volume?volume_percent={v}", ct: ct); }
        return (true, $"Sonando «{m.Nombre}»{(m.PlaylistNombre.Length > 0 ? $": {m.PlaylistNombre}" : "")}.", 200);
    }

    public static void MapMomentos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/musica").RequireAuthorization();

        g.MapGet("/momentos", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            await Sembrar(db, ct);
            var lista = await db.Momentos.AsNoTracking().OrderBy(m => m.Orden).ThenBy(m => m.Id).ToListAsync(ct);
            var hora = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).DateTime);
            return Results.Ok(new { momentos = lista, sugerido = Sugerido(lista, hora)?.Id });
        });

        g.MapPost("/momentos", async (MomentoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (Validar(e, true) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.Momentos.CountAsync(ct) >= Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Como mucho {Maximo} momentos."] });
            var m = new MomentoMusical
            {
                Nombre = e.Nombre!.Trim(), Icono = string.IsNullOrWhiteSpace(e.Icono) ? "🎵" : e.Icono.Trim(), PlaylistUri = NormalizarLista(e.PlaylistUri) ?? "", PlaylistNombre = (e.PlaylistNombre ?? "").Trim(),
                Desde = string.IsNullOrEmpty(e.Desde) ? null : e.Desde, Hasta = string.IsNullOrEmpty(e.Hasta) ? null : e.Hasta, Volumen = e.Volumen, Mezclar = e.Mezclar ?? true,
                Orden = (await db.Momentos.MaxAsync(x => (int?)x.Orden, ct) ?? -1) + 1,
            };
            db.Momentos.Add(m);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/musica/momentos/{m.Id}", m);
        });

        g.MapPut("/momentos/{id:int}", async (int id, MomentoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (Validar(e, false) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var m = await db.Momentos.FindAsync([id], ct);
            if (m is null) return Results.NotFound();
            if (e.Nombre is not null) m.Nombre = e.Nombre.Trim();
            if (e.Icono is not null) m.Icono = string.IsNullOrWhiteSpace(e.Icono) ? "🎵" : e.Icono.Trim();
            if (e.PlaylistUri is not null) m.PlaylistUri = NormalizarLista(e.PlaylistUri) ?? "";
            if (e.PlaylistNombre is not null) m.PlaylistNombre = e.PlaylistNombre.Trim();
            if (e.Desde is not null) m.Desde = e.Desde == "" ? null : e.Desde;
            if (e.Hasta is not null) m.Hasta = e.Hasta == "" ? null : e.Hasta;
            if (e.Volumen is { } v) m.Volumen = v; else if (e.QuitarVolumen) m.Volumen = null;
            if (e.Mezclar is { } mz) m.Mezclar = mz;
            await db.SaveChangesAsync(ct);
            return Results.Ok(m);
        });

        g.MapDelete("/momentos/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var m = await db.Momentos.FindAsync([id], ct);
            if (m is null) return Results.NotFound();
            db.Momentos.Remove(m);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // tus listas de Spotify, para elegir la de cada momento (hace falta el permiso de leer listas: si la conexión es antigua, se dice)
        g.MapGet("/playlists", async (ServicioConexiones s, CancellationToken ct) =>
        {
            using var res = await s.Llamar(ServicioConexiones.Spotify, HttpMethod.Get, "https://api.spotify.com/v1/me/playlists?limit=50", ct: ct);
            if (res is null) return Results.Problem("Spotify no está conectado.", statusCode: 409);
            if (res.StatusCode == HttpStatusCode.Forbidden) return Results.Problem("Falta el permiso para leer tus listas: desconecta y vuelve a conectar Spotify desde Seguridad.", statusCode: 403);
            if (!res.IsSuccessStatusCode) return Results.Problem("Spotify no ha respondido bien.", statusCode: 502);
            var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            return Results.Ok(j.GetProperty("items").EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => new
            {
                id = x.GetProperty("id").GetString(), nombre = x.GetProperty("name").GetString(), uri = x.GetProperty("uri").GetString(),
                imagen = x.TryGetProperty("images", out var im) && im.ValueKind == JsonValueKind.Array && im.GetArrayLength() > 0 ? im[0].GetProperty("url").GetString() : null,
            }).ToList());
        });

        g.MapPost("/momento/{id:int}", async (int id, AppDbContext db, ServicioConexiones con, CancellationToken ct) =>
        {
            var m = await db.Momentos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) return Results.NotFound();
            var (ok, mensaje, estado) = await Reproducir(con, m, ct);
            return ok ? Results.Ok(new { mensaje }) : Results.Json(new { detalle = mensaje }, statusCode: estado);
        });
    }
}
