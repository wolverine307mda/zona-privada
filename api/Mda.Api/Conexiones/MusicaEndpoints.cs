using System.Net;
using System.Text.Json;

namespace Mda.Api.Conexiones;

public record Cancion(string Id, string Nombre, string Artistas, string? Album, string? Imagen, string? Url, int Duracion);
public record Artista(string Id, string Nombre, string? Imagen, string? Url, string[] Generos, int Popularidad);
public record Reproduccion(bool Sonando, Cancion? Cancion, int Progreso, string? Dispositivo, string? TipoDispositivo, int? Volumen, bool Aleatorio, string Repetir);

// Música: lo que suena ahora, lo último escuchado y lo más escuchado, desde la API web de Spotify.
// La API simplifica las respuestas de Spotify a lo que pinta el front. Controlar la reproducción pide Spotify Premium.
public static class MusicaEndpoints
{
    private static readonly string[] Periodos = ["short_term", "medium_term", "long_term"];
    private static readonly Dictionary<string, (HttpMethod Metodo, string Ruta)> Acciones = new()
    {
        ["play"] = (HttpMethod.Put, "play"), ["pause"] = (HttpMethod.Put, "pause"),
        ["next"] = (HttpMethod.Post, "next"), ["previous"] = (HttpMethod.Post, "previous"),
    };

    public static void MapMusica(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/musica").RequireAuthorization();
        var sp = ServicioConexiones.Spotify;

        g.MapGet("/ahora", async (ServicioConexiones s, CancellationToken ct) =>
        {
            using var res = await s.Llamar(sp, HttpMethod.Get, "https://api.spotify.com/v1/me/player?additional_types=track", ct: ct);
            if (res is null) return Results.Problem("Spotify no está conectado.", statusCode: 409);
            if (res.StatusCode == HttpStatusCode.NoContent) return Results.Ok(new Reproduccion(false, null, 0, null, null, null, false, "off"));
            if (!res.IsSuccessStatusCode) return Fallo(res);
            var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            var item = j.TryGetProperty("item", out var it) && it.ValueKind == JsonValueKind.Object ? CancionDe(it) : null;
            var disp = j.TryGetProperty("device", out var d) ? d : default;
            return Results.Ok(new Reproduccion(
                j.GetProperty("is_playing").GetBoolean(), item, j.TryGetProperty("progress_ms", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0,
                disp.ValueKind == JsonValueKind.Object ? disp.GetProperty("name").GetString() : null,
                disp.ValueKind == JsonValueKind.Object ? disp.GetProperty("type").GetString() : null,
                disp.ValueKind == JsonValueKind.Object && disp.GetProperty("volume_percent").ValueKind == JsonValueKind.Number ? disp.GetProperty("volume_percent").GetInt32() : null,
                j.TryGetProperty("shuffle_state", out var sh) && sh.ValueKind == JsonValueKind.True,
                j.TryGetProperty("repeat_state", out var rp) ? rp.GetString() ?? "off" : "off"));
        });

        g.MapGet("/recientes", async (ServicioConexiones s, CancellationToken ct) =>
        {
            using var res = await s.Llamar(sp, HttpMethod.Get, "https://api.spotify.com/v1/me/player/recently-played?limit=30", ct: ct);
            if (res is null) return Results.Problem("Spotify no está conectado.", statusCode: 409);
            if (!res.IsSuccessStatusCode) return Fallo(res);
            var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            return Results.Ok(j.GetProperty("items").EnumerateArray()
                .Select(x => new { cancion = CancionDe(x.GetProperty("track")), cuando = x.GetProperty("played_at").GetString() }).ToList());
        });

        // lo que va a sonar después (solo canciones, las cinco primeras)
        g.MapGet("/cola", async (ServicioConexiones s, CancellationToken ct) =>
        {
            using var res = await s.Llamar(sp, HttpMethod.Get, "https://api.spotify.com/v1/me/player/queue", ct: ct);
            if (res is null) return Results.Problem("Spotify no está conectado.", statusCode: 409);
            if (res.StatusCode == HttpStatusCode.NoContent) return Results.Ok(Array.Empty<Cancion>());
            if (!res.IsSuccessStatusCode) return Fallo(res);
            var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            return Results.Ok(j.TryGetProperty("queue", out var q) && q.ValueKind == JsonValueKind.Array
                ? q.EnumerateArray().Where(x => x.TryGetProperty("type", out var t) && t.GetString() == "track").Take(5).Select(CancionDe).ToList()
                : []);
        });

        g.MapGet("/top",async (string? tipo, string? periodo, ServicioConexiones s, CancellationToken ct) =>
        {
            var t = tipo == "artistas" ? "artists" : "tracks";
            var per = Periodos.Contains(periodo) ? periodo : "short_term";
            using var res = await s.Llamar(sp, HttpMethod.Get, $"https://api.spotify.com/v1/me/top/{t}?limit=20&time_range={per}", ct: ct);
            if (res is null) return Results.Problem("Spotify no está conectado.", statusCode: 409);
            if (!res.IsSuccessStatusCode) return Fallo(res);
            var items = (await res.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items").EnumerateArray();
            return t == "artists" ? Results.Ok(items.Select(ArtistaDe).ToList()) : Results.Ok(items.Select(CancionDe).ToList());
        });

        // play, pause, next, previous (Spotify Premium)
        g.MapPost("/control/{accion}", async (string accion, ServicioConexiones s, CancellationToken ct) =>
        {
            if (!Acciones.TryGetValue(accion, out var a)) return Results.NotFound();
            using var res = await s.Llamar(sp, a.Metodo, $"https://api.spotify.com/v1/me/player/{a.Ruta}", ct: ct);
            if (res is null) return Results.Problem("Spotify no está conectado.", statusCode: 409);
            return res.IsSuccessStatusCode ? Results.NoContent() : Fallo(res);
        });
    }

    private static IResult Fallo(HttpResponseMessage res) => res.StatusCode switch
    {
        HttpStatusCode.Forbidden => Results.Problem("Spotify no deja hacer eso con esta cuenta (controlar la reproducción pide Premium).", statusCode: 403),
        HttpStatusCode.NotFound => Results.Problem("No hay ningún dispositivo con Spotify abierto.", statusCode: 404),
        HttpStatusCode.TooManyRequests => Results.Problem("Spotify pide esperar un poco.", statusCode: 429),
        _ => Results.Problem("Spotify no ha respondido bien.", statusCode: 502),
    };

    private static string? Imagen(JsonElement imagenes, int preferido = 1)
    {
        var l = imagenes.EnumerateArray().ToList();
        return l.Count == 0 ? null : l[Math.Min(preferido, l.Count - 1)].GetProperty("url").GetString();
    }

    private static Cancion CancionDe(JsonElement t)
    {
        var album = t.TryGetProperty("album", out var a) ? a : default;
        return new Cancion(
            t.GetProperty("id").GetString() ?? "", t.GetProperty("name").GetString() ?? "",
            string.Join(", ", t.GetProperty("artists").EnumerateArray().Select(x => x.GetProperty("name").GetString())),
            album.ValueKind == JsonValueKind.Object ? album.GetProperty("name").GetString() : null,
            album.ValueKind == JsonValueKind.Object ? Imagen(album.GetProperty("images"), 0) : null,
            t.TryGetProperty("external_urls", out var u) && u.TryGetProperty("spotify", out var su) ? su.GetString() : null,
            t.TryGetProperty("duration_ms", out var dm) ? dm.GetInt32() : 0);
    }

    private static Artista ArtistaDe(JsonElement a) => new(
        a.GetProperty("id").GetString() ?? "", a.GetProperty("name").GetString() ?? "",
        a.TryGetProperty("images", out var im) ? Imagen(im) : null,
        a.TryGetProperty("external_urls", out var u) && u.TryGetProperty("spotify", out var su) ? su.GetString() : null,
        a.TryGetProperty("genres", out var gs) ? gs.EnumerateArray().Select(x => x.GetString() ?? "").Take(3).ToArray() : [],
        a.TryGetProperty("popularity", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0);
}
