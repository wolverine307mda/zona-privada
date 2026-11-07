using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Series;

// Series y películas: lo que quieres ver, lo que estás viendo y lo que has visto, con el siguiente episodio y su fecha de estreno.
// Las series se buscan en TVMaze (gratis y sin clave); las películas se apuntan a mano. Avisa el día antes y el día del estreno y hace un resumen semanal.
public class Titulo
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "serie";            // serie o pelicula
    public int? IdExterno { get; set; }                    // el de TVMaze
    public string Nombre { get; set; } = "";
    public string? Imagen { get; set; }
    public string Estado { get; set; } = "por-ver";        // por-ver · viendo · vista
    public int Temporada { get; set; }                     // el último episodio visto (0×0 = ninguno)
    public int Episodio { get; set; }
    public int? Nota { get; set; }                         // 1–10
    public string Plataforma { get; set; } = "";
    public string Generos { get; set; } = "";
    public int Minutos { get; set; }                       // duración media de un episodio o de la película
    public bool Terminada { get; set; }                    // la serie ya acabó (o se canceló): no hay más estrenos
    public DateOnly? Proximo { get; set; }                 // fecha (hora local) del siguiente estreno
    public int ProximaTemporada { get; set; }
    public int ProximoEpisodio { get; set; }
    public DateOnly? AvisadoEstreno { get; set; }          // el estreno del que ya se avisó (para no repetir)
    public int AvisadoNivel { get; set; }
    public DateOnly? Terminado { get; set; }               // cuándo se marcó como vista (para las estadísticas del año)
}

public record TituloEntrada(string? Tipo, string? Nombre, int? IdExterno, string? Estado, int? Temporada, int? Episodio, int? Nota, string? Plataforma, string? Minutos, string? Estreno, bool QuitarNota = false);
public record ProximoEp(DateOnly Fecha, int Temporada, int Episodio);
public record FichaSerie(int Id, string Nombre, string? Imagen, string Plataforma, string[] Generos, int Minutos, bool Terminada, ProximoEp? Proximo);

public static class SeriesLogica
{
    public static readonly string[] Estados = ["por-ver", "viendo", "vista"];

    // Pura: lo que dice TVMaze (/shows/{id}?embed=nextepisode o un resultado de búsqueda) → ficha. La fecha de estreno se pasa a TU zona horaria con `airstamp`.
    public static FichaSerie? Interpretar(JsonElement s, TimeZoneInfo zona)
    {
        if (s.ValueKind != JsonValueKind.Object || !s.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number) return null;
        var nombre = s.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        string? imagen = s.TryGetProperty("image", out var im) && im.ValueKind == JsonValueKind.Object && im.TryGetProperty("medium", out var m) ? m.GetString() : null;
        var plataforma = s.TryGetProperty("network", out var red) && red.ValueKind == JsonValueKind.Object && red.TryGetProperty("name", out var rn) ? rn.GetString()
            : s.TryGetProperty("webChannel", out var web) && web.ValueKind == JsonValueKind.Object && web.TryGetProperty("name", out var wn) ? wn.GetString() : null;
        var generos = s.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array ? g.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Take(4).ToArray() : [];
        var minutos = s.TryGetProperty("averageRuntime", out var ar) && ar.ValueKind == JsonValueKind.Number ? ar.GetInt32() : s.TryGetProperty("runtime", out var rt) && rt.ValueKind == JsonValueKind.Number ? rt.GetInt32() : 0;
        var estado = s.TryGetProperty("status", out var st) ? st.GetString() : null;
        ProximoEp? prox = null;
        if (s.TryGetProperty("_embedded", out var emb) && emb.ValueKind == JsonValueKind.Object && emb.TryGetProperty("nextepisode", out var ne) && ne.ValueKind == JsonValueKind.Object) prox = Episodio(ne, zona);
        return new FichaSerie(id.GetInt32(), nombre!.Trim(), imagen?.Replace("http://", "https://"), plataforma ?? "", generos, minutos, estado == "Ended", prox);
    }

    // «airstamp» manda (lleva la hora y la zona); si no está, vale «airdate» tal cual
    public static ProximoEp? Episodio(JsonElement e, TimeZoneInfo zona)
    {
        DateOnly? fecha = null;
        if (e.TryGetProperty("airstamp", out var a) && a.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(a.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t))
            fecha = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, zona).DateTime);
        else if (e.TryGetProperty("airdate", out var d) && DateOnly.TryParseExact(d.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)) fecha = f;
        if (fecha is null) return null;
        return new ProximoEp(fecha.Value, e.TryGetProperty("season", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0, e.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0);
    }

    public static string Codigo(int temporada, int episodio) => $"{temporada}×{episodio:00}";

    // Pura: 0 = nada que avisar, 1 = mañana, 2 = hoy
    public static int NivelAviso(DateOnly? proximo, DateOnly hoy) => proximo is null ? 0 : proximo.Value == hoy ? 2 : proximo.Value == hoy.AddDays(1) ? 1 : 0;

    public static string TextoAviso(Titulo t, int nivel) => t.Tipo == "pelicula"
        ? (nivel == 2 ? $"Hoy se estrena «{t.Nombre}»." : $"Mañana se estrena «{t.Nombre}».")
        : (nivel == 2 ? $"Hoy sale el {Codigo(t.ProximaTemporada, t.ProximoEpisodio)} de «{t.Nombre}»." : $"Mañana sale el {Codigo(t.ProximaTemporada, t.ProximoEpisodio)} de «{t.Nombre}».");

    // Pura: «Esta semana sale…» con lo que se estrena en los próximos 7 días, por orden
    public static string? Semanal(IEnumerable<Titulo> titulos, DateOnly hoy)
    {
        var l = titulos.Where(t => t.Estado != "vista" && t.Proximo is { } p && p >= hoy && p <= hoy.AddDays(6)).OrderBy(t => t.Proximo).ToList();
        if (l.Count == 0) return null;
        var es = CultureInfo.GetCultureInfo("es-ES");
        return "📺 Esta semana sale:\n" + string.Join("\n", l.Select(t => $"• {t.Proximo!.Value.ToString("dddd d", es)}: {t.Nombre}{(t.Tipo == "serie" ? " " + Codigo(t.ProximaTemporada, t.ProximoEpisodio) : "")}"));
    }

    // Pura: tras ver un episodio sube uno; si se conocía la fecha del que acaba de pasar, se espera al que toque
    public static void Siguiente(Titulo t, DateOnly hoy)
    {
        t.Episodio += 1;
        if (t.Temporada == 0) t.Temporada = 1;
        if (t.Estado == "por-ver") t.Estado = "viendo";
        if (t.Proximo is { } p && p <= hoy && t.ProximaTemporada == t.Temporada && t.ProximoEpisodio <= t.Episodio) { t.Proximo = null; t.AvisadoEstreno = null; t.AvisadoNivel = 0; }   // ya lo has visto: hasta que TVMaze diga el siguiente
    }

    // Pura: estadísticas del año (horas estimadas, por género, notas)
    public static object Estadisticas(IReadOnlyList<Titulo> todos, int anio)
    {
        var vistas = todos.Where(t => t.Estado == "vista" && t.Terminado is { } d && d.Year == anio).ToList();
        // series: episodios vistos × duración; películas: su duración
        double Min(Titulo t) => t.Tipo == "pelicula" ? t.Minutos : Math.Max(0, t.Episodio) * Math.Max(20, t.Minutos);
        var minutos = todos.Where(t => t.Estado != "por-ver").Sum(Min);
        var generos = todos.Where(t => t.Estado != "por-ver").SelectMany(t => t.Generos.Split(',', StringSplitOptions.RemoveEmptyEntries)).GroupBy(x => x).OrderByDescending(g => g.Count()).Take(5).Select(g => new { genero = g.Key, veces = g.Count() });
        var notas = todos.Where(t => t.Nota is not null).Select(t => t.Nota!.Value).ToList();
        return new { anio, vistasEsteAnio = vistas.Count, horas = Math.Round(minutos / 60.0, 1), generos, notaMedia = notas.Count > 0 ? Math.Round(notas.Average(), 1) : (double?)null, viendo = todos.Count(t => t.Estado == "viendo"), porVer = todos.Count(t => t.Estado == "por-ver") };
    }
}

public sealed class ServicioTVMaze(IHttpClientFactory http, ILogger<ServicioTVMaze> log)
{
    public async Task<List<FichaSerie>> Buscar(string q, TimeZoneInfo zona, CancellationToken ct)
    {
        try
        {
            var j = await http.CreateClient("tvmaze").GetFromJsonAsync<JsonElement>($"https://api.tvmaze.com/search/shows?q={Uri.EscapeDataString(q.Trim())}", ct);
            return j.ValueKind != JsonValueKind.Array ? [] : j.EnumerateArray().Take(8).Select(x => x.TryGetProperty("show", out var s) ? SeriesLogica.Interpretar(s, zona) : null).Where(x => x is not null).Select(x => x!).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException) { log.LogWarning(e, "No se pudo buscar «{Q}» en TVMaze", q); return []; }
    }

    public async Task<FichaSerie?> Ficha(int id, TimeZoneInfo zona, CancellationToken ct)
    {
        try { return SeriesLogica.Interpretar(await http.CreateClient("tvmaze").GetFromJsonAsync<JsonElement>($"https://api.tvmaze.com/shows/{id}?embed=nextepisode", ct), zona); }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException) { log.LogWarning(e, "No se pudo leer la serie {Id} de TVMaze", id); return null; }
    }
}

public static class SeriesServicio
{
    public const int Maximo = 300;
    public const string ClaveRefresco = "series-refresco", ClaveSemanal = "series-semanal";

    public static object Dto(Titulo t, DateOnly hoy) => new
    {
        t.Id, t.Tipo, t.Nombre, t.Imagen, t.Estado, t.Temporada, t.Episodio, t.Nota, t.Plataforma, generos = t.Generos.Split(',', StringSplitOptions.RemoveEmptyEntries), t.Minutos, t.Terminada,
        proximo = t.Proximo?.ToString("yyyy-MM-dd"), dias = t.Proximo is { } p ? p.DayNumber - hoy.DayNumber : (int?)null,
        proximoCodigo = t.Proximo is null ? null : SeriesLogica.Codigo(t.ProximaTemporada, t.ProximoEpisodio),
        visto = t.Temporada > 0 || t.Episodio > 0 ? SeriesLogica.Codigo(t.Temporada, t.Episodio) : null,
    };

    public static void AplicarFicha(Titulo t, FichaSerie f)
    {
        t.IdExterno = f.Id; t.Imagen ??= f.Imagen; if (t.Plataforma.Length == 0) t.Plataforma = f.Plataforma; if (t.Generos.Length == 0) t.Generos = string.Join(',', f.Generos);
        if (f.Minutos > 0) t.Minutos = f.Minutos;
        t.Terminada = f.Terminada;
        if (f.Proximo is { } p) { if (t.Proximo != p.Fecha || t.ProximoEpisodio != p.Episodio) { t.AvisadoEstreno = null; t.AvisadoNivel = 0; } t.Proximo = p.Fecha; t.ProximaTemporada = p.Temporada; t.ProximoEpisodio = p.Episodio; }
        else if (t.Tipo == "serie") { t.Proximo = null; }
    }

    // Una vez al día: actualiza las fechas de las series que sigues y avisa el día antes y el día del estreno; los domingos por la tarde, el resumen semanal
    public static async Task Revisar(AppDbContext db, ServicioTVMaze tv, IAvisos avisos, Zona zona, DateOnly hoy, DateTime ahoraLocal, ILogger log, CancellationToken ct)
    {
        var refresco = await db.Ajustes.FirstOrDefaultAsync(a => a.Clave == ClaveRefresco, ct);
        if (refresco?.Valor != hoy.ToString("O"))
        {
            foreach (var t in await db.Titulos.Where(t => t.Tipo == "serie" && t.IdExterno != null && t.Estado != "vista" && !t.Terminada).ToListAsync(ct))
                if (await tv.Ficha(t.IdExterno!.Value, zona.Info, ct) is { } f) AplicarFicha(t, f);
            if (refresco is null) db.Ajustes.Add(new Mda.Api.Seguridad.Ajuste { Clave = ClaveRefresco, Valor = hoy.ToString("O") }); else refresco.Valor = hoy.ToString("O");
            await db.SaveChangesAsync(ct);
        }
        var textos = new List<string>();
        foreach (var t in await db.Titulos.Where(t => t.Estado != "vista" && t.Proximo != null).ToListAsync(ct))
        {
            var nivel = SeriesLogica.NivelAviso(t.Proximo, hoy);
            if (nivel == 0 || (t.AvisadoEstreno == t.Proximo && t.AvisadoNivel >= nivel)) continue;
            t.AvisadoEstreno = t.Proximo; t.AvisadoNivel = nivel;
            textos.Add(SeriesLogica.TextoAviso(t, nivel));
        }
        // el resumen de la semana, los domingos a partir de las 18:00, una vez
        if (ahoraLocal.DayOfWeek == DayOfWeek.Sunday && ahoraLocal.Hour >= 18)
        {
            var marca = await db.Ajustes.FirstOrDefaultAsync(a => a.Clave == ClaveSemanal, ct);
            if (marca?.Valor != hoy.ToString("O"))
            {
                if (marca is null) db.Ajustes.Add(new Mda.Api.Seguridad.Ajuste { Clave = ClaveSemanal, Valor = hoy.ToString("O") }); else marca.Valor = hoy.ToString("O");
                if (SeriesLogica.Semanal(await db.Titulos.AsNoTracking().ToListAsync(ct), hoy.AddDays(1)) is { } resumen) textos.Add(resumen);
            }
        }
        await db.SaveChangesAsync(ct);
        foreach (var t in textos)
        {
            try { await avisos.PorPush("📺 Series y películas", t, "/series", ct); await avisos.PorTelegram(t, ct); }
            catch (Exception e) { log.LogWarning(e, "No se pudo avisar de una serie"); }
        }
    }
}

public static class SeriesEndpoints
{
    public static void MapSeries(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/series").RequireAuthorization();
        static DateOnly Hoy(TimeProvider r, Zona z) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.GetUtcNow(), z.Info).Date);

        g.MapGet("/", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona);
            var l = await db.Titulos.AsNoTracking().ToListAsync(ct);
            return Results.Ok(l.OrderBy(t => t.Proximo is null).ThenBy(t => t.Proximo).ThenBy(t => t.Nombre).Select(t => SeriesServicio.Dto(t, hoy)));
        });

        g.MapGet("/buscar", async (string q, ServicioTVMaze tv, Zona zona, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(q) || q.Length > 80 ? Results.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["Escribe qué buscar."] }) : Results.Ok(await tv.Buscar(q, zona.Info, ct)));

        g.MapGet("/estadisticas", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
            Results.Ok(SeriesLogica.Estadisticas(await db.Titulos.AsNoTracking().ToListAsync(ct), Hoy(reloj, zona).Year)));

        g.MapPost("/", async (TituloEntrada e, AppDbContext db, ServicioTVMaze tv, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var tipo = e.Tipo ?? "serie";
            if (tipo is not ("serie" or "pelicula") || (e.Estado is not null && !SeriesLogica.Estados.Contains(e.Estado)))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["tipo"] = ["Serie o película; por ver, viendo o vista."] });
            if (await db.Titulos.CountAsync(ct) >= SeriesServicio.Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Como mucho {SeriesServicio.Maximo} títulos."] });
            var t = new Titulo { Tipo = tipo, Estado = e.Estado ?? "por-ver", Nombre = (e.Nombre ?? "").Trim(), Plataforma = (e.Plataforma ?? "").Trim() };
            if (tipo == "serie" && e.IdExterno is { } id)
            {
                if (await db.Titulos.AnyAsync(x => x.IdExterno == id, ct)) return Results.Conflict(new { detalle = "Esa serie ya está en tu lista." });
                var f = await tv.Ficha(id, zona.Info, ct);
                if (f is null) return Results.Problem("TVMaze no ha respondido: prueba en un rato o apúntala a mano.", statusCode: 502);
                t.Nombre = f.Nombre; SeriesServicio.AplicarFicha(t, f);
            }
            if (t.Nombre.Length is 0 or > 120) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Ponle un nombre (máximo 120 letras)."] });
            if (int.TryParse(e.Minutos, out var min) && min is > 0 and <= 600) t.Minutos = min;
            if (tipo == "pelicula" && DateOnly.TryParseExact(e.Estreno, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var estreno)) t.Proximo = estreno;
            if (t.Estado == "vista") t.Terminado = Hoy(reloj, zona);
            db.Titulos.Add(t);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/series/{t.Id}", SeriesServicio.Dto(t, Hoy(reloj, zona)));
        });

        g.MapPut("/{id:int}", async (int id, TituloEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var t = await db.Titulos.FindAsync([id], ct);
            if (t is null) return Results.NotFound();
            if (e.Estado is not null && !SeriesLogica.Estados.Contains(e.Estado)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["estado"] = ["Por ver, viendo o vista."] });
            if (e.Nota is < 1 or > 10) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nota"] = ["De 1 a 10."] });
            if (e.Temporada is < 0 or > 99 || e.Episodio is < 0 or > 9999) return Results.ValidationProblem(new Dictionary<string, string[]> { ["episodio"] = ["Temporada y episodio no válidos."] });
            if (e.Nombre is { Length: > 0 }) t.Nombre = e.Nombre.Trim()[..Math.Min(e.Nombre.Trim().Length, 120)];
            if (e.Estado is not null) { t.Estado = e.Estado; t.Terminado = e.Estado == "vista" ? Hoy(reloj, zona) : null; }
            if (e.Temporada is { } te) t.Temporada = te;
            if (e.Episodio is { } ep) t.Episodio = ep;
            if (e.Nota is { } n) t.Nota = n; else if (e.QuitarNota) t.Nota = null;
            if (e.Plataforma is not null) t.Plataforma = e.Plataforma.Trim();
            if (e.Estreno is not null) t.Proximo = DateOnly.TryParseExact(e.Estreno, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(SeriesServicio.Dto(t, Hoy(reloj, zona)));
        });

        // «vi un episodio»: sube uno (y pasa a «viendo»)
        g.MapPost("/{id:int}/siguiente", async (int id, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var t = await db.Titulos.FindAsync([id], ct);
            if (t is null) return Results.NotFound();
            SeriesLogica.Siguiente(t, Hoy(reloj, zona));
            await db.SaveChangesAsync(ct);
            return Results.Ok(SeriesServicio.Dto(t, Hoy(reloj, zona)));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var t = await db.Titulos.FindAsync([id], ct);
            if (t is null) return Results.NotFound();
            db.Titulos.Remove(t);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
