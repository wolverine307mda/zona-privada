using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Compra;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Cocina;

// Planificador de comidas: recetas (a mano o leídas de una página web), un menú por semana (a mano o generado por reglas), la lista de la compra que sale de él
// y un aviso diario de lo que toca. Todo determinista y sin servicios externos de IA.
public class Receta
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public int Raciones { get; set; } = 4;
    public int Minutos { get; set; }
    public string Etiquetas { get; set; } = "";       // separadas por coma, en minúsculas: «rápida, vegetariana»
    public string Pasos { get; set; } = "";           // un paso por línea
    public string Origen { get; set; } = "";
}

public class IngredienteReceta
{
    public int Id { get; set; }
    public int RecetaId { get; set; }
    public string Nombre { get; set; } = "";
    public double? Cantidad { get; set; }
    public string Unidad { get; set; } = "";
    public string Texto { get; set; } = "";
}

public class MenuDia
{
    public int Id { get; set; }
    public DateOnly Fecha { get; set; }
    public string Momento { get; set; } = "comida";   // comida o cena
    public int? RecetaId { get; set; }
    public string Texto { get; set; } = "";           // si no hay receta: texto libre («fuera de casa», «sobras»)
    public bool Sobras { get; set; }
}

public record IngredienteEntrada(string? Nombre, double? Cantidad, string? Unidad);
public record RecetaEntrada(string? Nombre, int? Raciones, int? Minutos, string? Etiquetas, string? Pasos, string? Origen, List<IngredienteEntrada>? Ingredientes);
public record DesdeWeb(string? Url);
public record CeldaMenu(int? RecetaId, string? Texto, bool? Sobras);
public record GenerarEntrada(string? Desde, List<string>? Etiquetas, List<string>? Alergias, int? Sobras, bool? Reemplazar);
public record AjustesCocina(List<string>? Alergias, string? HoraAviso);
public record RecetaMenu(int Id, string Nombre, string[] Etiquetas, string[] Ingredientes);
public record CasillaGenerada(DateOnly Fecha, string Momento, int? RecetaId, bool Sobras);

public static class MenuSemanal
{
    public const int DiasSinRepetir = 14;

    // Pura: una semana (7 comidas y 7 cenas). Solo recetas con TODAS las etiquetas pedidas y sin nada de lo que no se puede comer (en el nombre, los ingredientes o las etiquetas);
    // las que gustan más (por etiquetas y nombre) van antes; no repite lo de los últimos 14 días ni dentro de la propia semana (si no hay recetas de sobra, reutiliza las más antiguas);
    // y reparte «días de sobras»: la comida de ese día es la cena de la víspera. La semilla hace el reparto repetible.
    public static List<CasillaGenerada> Generar(IReadOnlyList<RecetaMenu> recetas, IReadOnlyDictionary<int, DateOnly> ultimaVez, DateOnly inicio, IReadOnlyCollection<string> etiquetas, IReadOnlyCollection<string> alergias,
        IReadOnlyCollection<string> gustos, int diasSobras, int semilla)
    {
        string N(string s) => Ingrediente.Clave(s);
        var req = etiquetas.Select(N).Where(x => x.Length > 0).ToList();
        var alerg = alergias.Select(N).Where(x => x.Length > 0).ToList();
        var gust = gustos.Select(N).Where(x => x.Length > 0).ToList();
        var validas = recetas.Where(r =>
        {
            var tags = r.Etiquetas.Select(N).ToList();
            if (!req.All(t => tags.Contains(t))) return false;
            var todo = string.Join(' ', tags.Append(N(r.Nombre)).Concat(r.Ingredientes.Select(N)));
            return !alerg.Any(a => todo.Contains(a));
        }).ToList();
        var salida = new List<CasillaGenerada>();
        if (validas.Count == 0) return salida;
        var azar = new Random(semilla);
        var usadasAqui = new Dictionary<int, DateOnly>();
        int? previaCena = null;
        var sobras = SobrasDias(diasSobras);

        int? Elegir(DateOnly fecha)
        {
            // 1.º las que no se han hecho en los últimos 14 días (ni antes en esta semana); 2.º si no quedan, las del historial limpio que hace más que no salen en esta semana;
            // 3.º si todas son recientes, las que hace más que no se hacen
            DateOnly Antes(int id) => ultimaVez.TryGetValue(id, out var v) ? v : DateOnly.MinValue;
            DateOnly Aqui(int id) => usadasAqui.TryGetValue(id, out var u) ? u : DateOnly.MinValue;
            var limpias = validas.Where(r => fecha.DayNumber - Antes(r.Id).DayNumber >= DiasSinRepetir).ToList();
            var libres = limpias.Where(r => !usadasAqui.ContainsKey(r.Id)).ToList();
            var pool = libres.Count > 0 ? libres : limpias.Count > 0 ? limpias.OrderBy(r => Aqui(r.Id)).Take(Math.Max(1, limpias.Count / 2)).ToList() : validas.OrderBy(r => Antes(r.Id)).Take(Math.Max(1, validas.Count / 2)).ToList();
            int Puntos(RecetaMenu r) => gust.Count(g => r.Etiquetas.Select(N).Contains(g) || N(r.Nombre).Contains(g));
            var elegida = pool.OrderByDescending(Puntos).ThenBy(_ => azar.Next()).First();
            usadasAqui[elegida.Id] = fecha;
            return elegida.Id;
        }

        for (var d = 0; d < 7; d++)
        {
            var fecha = inicio.AddDays(d);
            if (sobras.Contains(d) && previaCena is { } p) salida.Add(new CasillaGenerada(fecha, "comida", p, true));
            else salida.Add(new CasillaGenerada(fecha, "comida", Elegir(fecha), false));
            var cena = Elegir(fecha);
            salida.Add(new CasillaGenerada(fecha, "cena", cena, false));
            previaCena = cena;
        }
        return salida;
    }

    // los días (0–6) de la semana que se comen sobras: repartidos (2 → miércoles y sábado; 1 → miércoles; 3 → martes, jueves y domingo)
    public static HashSet<int> SobrasDias(int n) => n switch { <= 0 => [], 1 => [2], 2 => [2, 5], _ => [1, 3, 6] };
}

// Pura: lo de «hoy toca…»
public static class AvisoCocina
{
    public static string? Texto(IEnumerable<(string Momento, string Nombre, bool Sobras)> hoy)
    {
        var l = hoy.ToList();
        if (l.Count == 0) return null;
        var partes = l.OrderBy(x => x.Momento == "cena" ? 1 : 0).Select(x => $"{(x.Momento == "cena" ? "cena" : "comida")}: {x.Nombre}{(x.Sobras ? " (sobras)" : "")}");
        return "🍽️ Hoy toca — " + string.Join("; ", partes);
    }
}

public sealed class ServicioRecetas(IHttpClientFactory http, ILogger<ServicioRecetas> log)
{
    public async Task<(RecetaLeida? Receta, string? Error)> Leer(string url, CancellationToken ct)
    {
        if (!UrlsRecetas.Valida(url, out var uri)) return (null, "Pon una dirección web normal (https://…): nada de la red local.");
        var cliente = http.CreateClient("recetas");
        try
        {
            for (var salto = 0; salto < 4; salto++)
            {
                // se mira a dónde apunta el nombre de verdad antes de pedir: así un DNS que lleve a la red local no cuela
                var ips = IPAddress.TryParse(uri!.Host, out var ya) ? [ya] : await Dns.GetHostAddressesAsync(uri.Host, ct);
                if (ips.Length == 0 || ips.Any(UrlsRecetas.EsLocal)) return (null, "Esa dirección apunta a la red local o a este equipo: no se lee.");
                using var res = await cliente.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } destino)
                {
                    if (!UrlsRecetas.Valida(new Uri(uri, destino).AbsoluteUri, out uri)) return (null, "La página redirige a un sitio que no se puede leer.");
                    continue;
                }
                if (!res.IsSuccessStatusCode) return (null, $"La página respondió {(int)res.StatusCode}.");
                var tipo = res.Content.Headers.ContentType?.MediaType ?? "";
                if (!tipo.Contains("html", StringComparison.OrdinalIgnoreCase) && !tipo.Contains("xml", StringComparison.OrdinalIgnoreCase)) return (null, "Eso no es una página web.");
                await using var s = await res.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[UrlsRecetas.MaxBytes]; var leidos = 0;
                while (leidos < buffer.Length) { var n = await s.ReadAsync(buffer.AsMemory(leidos), ct); if (n == 0) break; leidos += n; }
                var r = SchemaRecipe.Parsear(Encoding.UTF8.GetString(buffer, 0, leidos));
                return r is null ? (null, "No encuentro una receta en esa página (no trae el marcado que usan las webs de cocina). Puedes escribirla a mano.") : (r, null);
            }
            return (null, "Demasiadas redirecciones.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Net.Sockets.SocketException)
        {
            log.LogWarning(e, "No se pudo leer la receta de {Url}", url);
            return (null, "No se pudo abrir esa página.");
        }
    }
}

public static class CocinaServicio
{
    public const int MaxRecetas = 400;
    public const string ClaveAjustes = "cocina-ajustes", ClaveAvisado = "cocina-avisado";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static object Dto(Receta r, IEnumerable<IngredienteReceta> ing) => new
    {
        r.Id, r.Nombre, r.Raciones, r.Minutos, etiquetas = r.Etiquetas.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        pasos = r.Pasos.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), r.Origen,
        ingredientes = ing.OrderBy(i => i.Id).Select(i => new { i.Nombre, i.Cantidad, i.Unidad, i.Texto }),
    };

    public static Dictionary<string, string[]> Validar(RecetaEntrada e, bool nueva)
    {
        var mal = new Dictionary<string, string[]>();
        if (nueva || e.Nombre is not null) { if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 120) mal["nombre"] = ["Ponle un nombre (máximo 120 letras)."]; }
        if (e.Raciones is < 1 or > 50) mal["raciones"] = ["De 1 a 50 raciones."];
        if (e.Minutos is < 0 or > 1440) mal["minutos"] = ["De 0 a 1440 minutos."];
        if (e.Ingredientes is { Count: > 80 }) mal["ingredientes"] = ["Hasta 80 ingredientes."];
        if (e.Pasos is { Length: > 8000 }) mal["pasos"] = ["Los pasos llegan a 8000 letras."];
        return mal;
    }

    public static string Etiquetas(string? e) => string.Join(',', (e ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(x => x.ToLowerInvariant()).Where(x => x.Length <= 30).Distinct().Take(12));

    public static async Task Aplicar(AppDbContext db, Receta r, RecetaEntrada e, CancellationToken ct)
    {
        if (e.Nombre is not null) r.Nombre = e.Nombre.Trim();
        if (e.Raciones is { } ra) r.Raciones = ra;
        if (e.Minutos is { } mi) r.Minutos = mi;
        if (e.Etiquetas is not null) r.Etiquetas = Etiquetas(e.Etiquetas);
        if (e.Pasos is not null) r.Pasos = e.Pasos.Trim();
        if (e.Origen is not null) r.Origen = e.Origen.Trim()[..Math.Min(e.Origen.Trim().Length, 300)];
        if (e.Ingredientes is not null && r.Id > 0)
        {
            db.IngredientesReceta.RemoveRange(await db.IngredientesReceta.Where(i => i.RecetaId == r.Id).ToListAsync(ct));
            foreach (var i in e.Ingredientes.Where(i => !string.IsNullOrWhiteSpace(i.Nombre)))
                db.IngredientesReceta.Add(new IngredienteReceta { RecetaId = r.Id, Nombre = i.Nombre!.Trim(), Cantidad = i.Cantidad, Unidad = i.Unidad?.Trim() ?? "", Texto = $"{(i.Cantidad is { } c ? Ingrediente.Escribir(c, i.Unidad ?? "") + " " : "")}{i.Nombre!.Trim()}" });
        }
    }

    public static async Task<AjustesCocina> LeerAjustes(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveAjustes, ct))?.Valor;
        try { var a = v is null ? null : JsonSerializer.Deserialize<AjustesCocina>(v, Json); return new AjustesCocina(a?.Alergias ?? [], a?.HoraAviso ?? ""); } catch (JsonException) { return new AjustesCocina([], ""); }
    }

    public static async Task GuardarAjustes(AppDbContext db, AjustesCocina a, CancellationToken ct)
    {
        var f = await db.Ajustes.FirstOrDefaultAsync(x => x.Clave == ClaveAjustes, ct);
        var v = JsonSerializer.Serialize(a, Json);
        if (f is null) db.Ajustes.Add(new Ajuste { Clave = ClaveAjustes, Valor = v }); else f.Valor = v;
        await db.SaveChangesAsync(ct);
    }

    // Una vez al día, a partir de la hora elegida: «Hoy toca…». Sin hora, no avisa.
    public static async Task AvisarHoy(AppDbContext db, IAvisos avisos, DateTime ahoraLocal, CancellationToken ct)
    {
        var a = await LeerAjustes(db, ct);
        if (!TimeOnly.TryParseExact(a.HoraAviso, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora) || TimeOnly.FromDateTime(ahoraLocal) < hora) return;
        var hoy = DateOnly.FromDateTime(ahoraLocal);
        var marca = await db.Ajustes.FirstOrDefaultAsync(x => x.Clave == ClaveAvisado, ct);
        if (marca?.Valor == hoy.ToString("O")) return;
        var menu = await db.MenuDias.AsNoTracking().Where(m => m.Fecha == hoy).ToListAsync(ct);
        var recetas = await db.Recetas.AsNoTracking().Where(r => menu.Select(m => m.RecetaId).Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Nombre, ct);
        var texto = AvisoCocina.Texto(menu.Select(m => (m.Momento, m.RecetaId is { } id && recetas.TryGetValue(id, out var n) ? n : m.Texto, m.Sobras)).Where(x => x.Item2.Length > 0));
        if (marca is null) db.Ajustes.Add(new Ajuste { Clave = ClaveAvisado, Valor = hoy.ToString("O") }); else marca.Valor = hoy.ToString("O");
        await db.SaveChangesAsync(ct);
        if (texto is null) return;
        await avisos.Avisar("cocina", "🍽️ Hoy toca", texto, textoTelegram: texto, ct: ct);
    }
}
