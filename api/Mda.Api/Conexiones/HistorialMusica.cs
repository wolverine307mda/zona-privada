using System.Globalization;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Conexiones;

// Historial propio de lo que escuchas. Spotify solo recuerda las últimas 50 canciones, así que cada 20 minutos se recogen las nuevas y se guardan aquí
// (unos pocos MB al año). De ahí salen el top del mes y del año, las horas del día, la racha y los artistas nuevos. Se puede borrar todo desde la pantalla.
public class Escucha
{
    public long Id { get; set; }
    public string CancionId { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Artistas { get; set; } = "";
    public string? Album { get; set; }
    public string? Imagen { get; set; }
    public int Duracion { get; set; }
    public DateTimeOffset Fecha { get; set; }
}

public record TopCancion(string CancionId, string Nombre, string Artistas, string? Imagen, int Veces);
public record TopArtista(string Nombre, int Veces);

// Las cuentas de las estadísticas, puras y sin base de datos para poder probarlas
public static class EstadisticasMusica
{
    public static List<TopCancion> TopCanciones(IEnumerable<Escucha> l, int cuantas = 10) =>
        l.GroupBy(e => e.CancionId).Select(g => new { g, ultima = g.MaxBy(x => x.Fecha)! })
            .Select(x => new TopCancion(x.g.Key, x.ultima.Nombre, x.ultima.Artistas, x.ultima.Imagen, x.g.Count()))
            .OrderByDescending(t => t.Veces).ThenBy(t => t.Nombre, StringComparer.CurrentCultureIgnoreCase).Take(cuantas).ToList();

    // un artista por cada nombre separado por coma (una colaboración cuenta para los dos)
    public static List<TopArtista> TopArtistas(IEnumerable<Escucha> l, int cuantos = 10) =>
        l.SelectMany(e => e.Artistas.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .GroupBy(a => a, StringComparer.CurrentCultureIgnoreCase).Select(g => new TopArtista(g.Key, g.Count()))
            .OrderByDescending(a => a.Veces).ThenBy(a => a.Nombre, StringComparer.CurrentCultureIgnoreCase).Take(cuantos).ToList();

    // Escuchas por hora local del día (24 casillas): el mapa de calor
    public static int[] Horas(IEnumerable<Escucha> l, TimeZoneInfo zona)
    {
        var h = new int[24];
        foreach (var e in l) h[TimeZoneInfo.ConvertTime(e.Fecha, zona).Hour]++;
        return h;
    }

    // Días seguidos con música hasta hoy (o hasta ayer, si hoy todavía no ha sonado nada)
    public static int Racha(IEnumerable<Escucha> l, TimeZoneInfo zona, DateOnly hoy)
    {
        var dias = l.Select(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Fecha, zona).DateTime)).ToHashSet();
        var d = dias.Contains(hoy) ? hoy : hoy.AddDays(-1);
        var n = 0;
        while (dias.Contains(d)) { n++; d = d.AddDays(-1); }
        return n;
    }

    // Artistas que escuchas en el periodo y nunca antes de él: lo descubierto este mes
    public static List<string> ArtistasNuevos(IReadOnlyCollection<Escucha> todas, DateTimeOffset desde)
    {
        string[] Nombres(Escucha e) => e.Artistas.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var antes = todas.Where(e => e.Fecha < desde).SelectMany(Nombres).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        return todas.Where(e => e.Fecha >= desde).SelectMany(Nombres).Where(a => !antes.Contains(a)).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static (long Minutos, int Canciones) Total(IEnumerable<Escucha> l) { var x = l.ToList(); return ((long)(x.Sum(e => (long)e.Duracion) / 60000), x.Count); }

    // Pura: el principio del periodo («semana» = 7 días, «mes» = el mes en curso, «anio» = el año en curso, el resto = todo)
    public static DateTimeOffset? Desde(string? periodo, DateTimeOffset ahora, TimeZoneInfo zona)
    {
        var l = TimeZoneInfo.ConvertTime(ahora, zona);
        return periodo switch
        {
            "semana" => ahora.AddDays(-7),
            "mes" => new DateTimeOffset(new DateTime(l.Year, l.Month, 1), zona.GetUtcOffset(new DateTime(l.Year, l.Month, 1))),
            "anio" => new DateTimeOffset(new DateTime(l.Year, 1, 1), zona.GetUtcOffset(new DateTime(l.Year, 1, 1))),
            _ => null,
        };
    }
}

// Cada 20 minutos recoge lo nuevo de «recently-played» (Spotify solo da las últimas 50). Si no hay Spotify conectado, no hace nada.
public sealed class RecolectorMusica(IServiceScopeFactory scopes, ServicioConexiones con, TimeProvider reloj, ILogger<RecolectorMusica> log) : BackgroundService
{
    public static readonly TimeSpan Cada = TimeSpan.FromMinutes(20);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), reloj, ct);
            using var t = new PeriodicTimer(Cada, reloj);
            do
            {
                try { using var scope = scopes.CreateScope(); await Recoger(scope.ServiceProvider.GetRequiredService<AppDbContext>(), con, ct); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "No se pudo recoger el historial de música"); }
            } while (await t.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { /* se apaga la API */ }
    }

    // Interpreta la respuesta de recently-played y devuelve las escuchas (puro, para las pruebas)
    public static List<Escucha> Interpretar(JsonElement j)
    {
        var l = new List<Escucha>();
        if (!j.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return l;
        foreach (var x in items.EnumerateArray())
        {
            if (!x.TryGetProperty("track", out var t) || t.ValueKind != JsonValueKind.Object || !x.TryGetProperty("played_at", out var pa)) continue;
            if (!DateTimeOffset.TryParse(pa.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fecha)) continue;
            var id = t.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (string.IsNullOrEmpty(id)) continue;                  // un archivo local o un episodio sin id
            var album = t.TryGetProperty("album", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
            l.Add(new Escucha
            {
                CancionId = id, Nombre = (t.GetProperty("name").GetString() ?? "")[..Math.Min((t.GetProperty("name").GetString() ?? "").Length, 200)],
                Artistas = string.Join(", ", t.GetProperty("artists").EnumerateArray().Select(z => z.GetProperty("name").GetString())),
                Album = album.ValueKind == JsonValueKind.Object ? album.GetProperty("name").GetString() : null,
                Imagen = album.ValueKind == JsonValueKind.Object && album.TryGetProperty("images", out var im) && im.GetArrayLength() > 0 ? im[Math.Min(1, im.GetArrayLength() - 1)].GetProperty("url").GetString() : null,
                Duracion = t.TryGetProperty("duration_ms", out var dm) && dm.ValueKind == JsonValueKind.Number ? dm.GetInt32() : 0,
                Fecha = fecha.ToUniversalTime(),
            });
        }
        return l;
    }

    // Guarda las que no estén (misma canción y misma fecha = la misma escucha). Devuelve cuántas nuevas.
    public static async Task<int> Guardar(AppDbContext db, IReadOnlyList<Escucha> nuevas, CancellationToken ct)
    {
        if (nuevas.Count == 0) return 0;
        var minima = nuevas.Min(e => e.Fecha);
        var existentes = (await db.Escuchas.AsNoTracking().Where(e => e.Fecha >= minima).Select(e => new { e.CancionId, e.Fecha }).ToListAsync(ct)).Select(e => (e.CancionId, e.Fecha)).ToHashSet();
        var aGuardar = nuevas.Where(e => !existentes.Contains((e.CancionId, e.Fecha))).DistinctBy(e => (e.CancionId, e.Fecha)).ToList();
        db.Escuchas.AddRange(aGuardar);
        await db.SaveChangesAsync(ct);
        return aGuardar.Count;
    }

    public static async Task<int> Recoger(AppDbContext db, ServicioConexiones con, CancellationToken ct)
    {
        if (await con.Leer(ServicioConexiones.Spotify, ct) is null) return 0;
        var ultima = await db.Escuchas.AsNoTracking().OrderByDescending(e => e.Fecha).Select(e => (DateTimeOffset?)e.Fecha).FirstOrDefaultAsync(ct);
        var url = "https://api.spotify.com/v1/me/player/recently-played?limit=50" + (ultima is { } u ? $"&after={u.ToUnixTimeMilliseconds()}" : "");
        using var res = await con.Llamar(ServicioConexiones.Spotify, HttpMethod.Get, url, ct: ct);
        if (res is null || !res.IsSuccessStatusCode) return 0;
        return await Guardar(db, Interpretar(await res.Content.ReadFromJsonAsync<JsonElement>(ct)), ct);
    }
}

public static class HistorialMusicaEndpoints
{
    public static void MapHistorialMusica(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/musica/historial").RequireAuthorization();

        g.MapGet("/", async (string? periodo, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var ahora = reloj.GetUtcNow();
            var todas = await db.Escuchas.AsNoTracking().ToListAsync(ct);
            var desde = EstadisticasMusica.Desde(periodo, ahora, zona.Info);
            var delPeriodo = desde is { } d ? todas.Where(e => e.Fecha >= d).ToList() : todas;
            var (minutos, canciones) = EstadisticasMusica.Total(delPeriodo);
            var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, zona.Info).DateTime);
            var semana = todas.Where(e => e.Fecha >= ahora.AddDays(-7)).ToList();
            return Results.Ok(new
            {
                periodo = periodo ?? "todo", hayDatos = todas.Count > 0, desdeFecha = todas.Count > 0 ? todas.Min(e => e.Fecha) : (DateTimeOffset?)null,
                minutos, canciones,
                topCanciones = EstadisticasMusica.TopCanciones(delPeriodo), topArtistas = EstadisticasMusica.TopArtistas(delPeriodo),
                horas = EstadisticasMusica.Horas(delPeriodo, zona.Info), racha = EstadisticasMusica.Racha(todas, zona.Info, hoy),
                artistasNuevos = desde is { } d2 ? EstadisticasMusica.ArtistasNuevos(todas, d2).Take(12).ToList() : [],
                cancionDeLaSemana = EstadisticasMusica.TopCanciones(semana, 1).FirstOrDefault(),
            });
        });

        // borrar todo el historial propio (Spotify seguirá teniendo lo suyo)
        g.MapDelete("/", async (AppDbContext db, CancellationToken ct) =>
        {
            db.Escuchas.RemoveRange(await db.Escuchas.ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
