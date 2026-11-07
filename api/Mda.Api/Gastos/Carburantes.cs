using System.Globalization;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Gastos;

// Dónde y cuándo repostar. Los precios salen del servicio público de «Precios de carburantes» del Ministerio (sin clave: son datos abiertos de todas las
// gasolineras de España). Con tu punto de referencia (el lugar del tiempo u otro) se buscan las más baratas en tu radio y se compara con la media de la provincia,
// con lo que pagaste en tu último repostaje y con la última semana (una foto al día de lo más barato cerca de ti).
public record Estacion(string Rotulo, string Direccion, string Localidad, string Provincia, double Lat, double Lon, IReadOnlyDictionary<string, decimal> Precios);
public record EstacionCercana(string Rotulo, string Direccion, string Localidad, double Km, decimal Precio, double Lat, double Lon);
public record PuntoCombustible(string Nombre, double Lat, double Lon);
public record AjustesCombustible(string Combustible = "gasolina95", int Radio = 10, PuntoCombustible? Punto = null, bool Aviso = false);
public record Valoracion(string Nivel, string Titulo, IReadOnlyList<string> Razones);

public class PrecioCarburante
{
    public DateOnly Fecha { get; set; }
    public string Combustible { get; set; } = "";
    public decimal Media { get; set; }        // media (mediana) de la provincia ese día
    public decimal MinimoCerca { get; set; }  // lo más barato dentro de tu radio ese día
}

public static class Carburantes
{
    public const string Clave = "coche-combustible", ClaveAvisado = "carburante-avisado";
    public const int RadioMin = 1, RadioMax = 50;
    // el combustible de cada tipo: su clave, el nombre del campo en el servicio del Ministerio y cómo se llama para ti
    public static readonly Dictionary<string, (string Campo, string Nombre)> Combustibles = new()
    {
        ["gasolina95"] = ("Precio Gasolina 95 E5", "Gasolina 95"),
        ["gasolina98"] = ("Precio Gasolina 98 E5", "Gasolina 98"),
        ["diesel"] = ("Precio Gasoleo A", "Diésel"),
        ["dieselplus"] = ("Precio Gasoleo Premium", "Diésel premium"),
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static decimal? Precio(string? s) => !string.IsNullOrWhiteSpace(s) && decimal.TryParse(s.Replace(',', '.'), NumberStyles.Number, Inv, out var p) && p is > 0.4m and < 5m ? p : null;
    private static double? Coord(string? s) => !string.IsNullOrWhiteSpace(s) && double.TryParse(s.Replace(',', '.'), NumberStyles.Float, Inv, out var d) ? d : null;
    private static string Texto(JsonElement e, string campo) => e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    // El JSON del Ministerio: números con coma decimal («1,459»), precios vacíos cuando no hay ese combustible y «Tipo Venta» R para las gasolineras
    // de socios (cooperativas), que no son públicas: se descartan. Lo que no se entiende se salta.
    public static List<Estacion> Parsear(string json)
    {
        var lista = new List<Estacion>();
        using var doc = JsonDocument.Parse(json.TrimStart('﻿'));
        if (!doc.RootElement.TryGetProperty("ListaEESSPrecio", out var todas) || todas.ValueKind != JsonValueKind.Array) return lista;
        foreach (var e in todas.EnumerateArray())
        {
            if (Texto(e, "Tipo Venta").Equals("R", StringComparison.OrdinalIgnoreCase)) continue;
            var lat = Coord(Texto(e, "Latitud")); var lon = Coord(Texto(e, "Longitud (WGS84)"));
            if (lat is not { } la || lon is not { } lo || la is < 26 or > 45 || lo is < -19 or > 5) continue; // fuera de España (con Canarias)
            var precios = new Dictionary<string, decimal>();
            foreach (var (clave, (campo, _)) in Combustibles) if (Precio(Texto(e, campo)) is { } p) precios[clave] = p;
            if (precios.Count == 0) continue;
            lista.Add(new Estacion(Texto(e, "Rótulo"), Texto(e, "Dirección"), Texto(e, "Localidad"), Texto(e, "Provincia"), la, lo, precios));
        }
        return lista;
    }

    // kilómetros en línea recta (fórmula del semiverso)
    public static double Distancia(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double g) => g * Math.PI / 180;
        var dLat = Rad(lat2 - lat1); var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 6371 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    // las más baratas dentro del radio, de menor a mayor precio (y, a igual precio, la más cercana)
    public static List<EstacionCercana> Cercanas(IEnumerable<Estacion> estaciones, double lat, double lon, double radioKm, string combustible, int cuantas = 5) =>
        estaciones.Where(e => e.Precios.ContainsKey(combustible))
            .Select(e => new EstacionCercana(e.Rotulo, e.Direccion, e.Localidad, Math.Round(Distancia(lat, lon, e.Lat, e.Lon), 1), e.Precios[combustible], e.Lat, e.Lon))
            .Where(e => e.Km <= radioKm).OrderBy(e => e.Precio).ThenBy(e => e.Km).Take(cuantas).ToList();

    // la provincia de la gasolinera más cercana al punto
    public static string? ProvinciaDe(IEnumerable<Estacion> estaciones, double lat, double lon) =>
        estaciones.OrderBy(e => Distancia(lat, lon, e.Lat, e.Lon)).FirstOrDefault()?.Provincia;

    // la mediana (resiste a un precio mal puesto) de un combustible en una provincia
    public static decimal? MediaProvincia(IEnumerable<Estacion> estaciones, string? provincia, string combustible)
    {
        var v = estaciones.Where(e => e.Provincia == provincia && e.Precios.ContainsKey(combustible)).Select(e => e.Precios[combustible]).Order().ToList();
        return v.Count == 0 ? null : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    private static string Cent(decimal d) => $"{Math.Abs(d) * 100:0.#}".Replace('.', ',') + " céntimos";
    private static string Euro(decimal d) => d.ToString("0.000", Inv).Replace('.', ',') + " €/L";

    // «¿Conviene repostar hoy?»: nunca un sí o un no a secas, siempre con las comparaciones que lo explican.
    // cerca = lo más barato dentro de tu radio; media7 = lo más barato cerca de ti en los últimos días (null si aún no hay datos)
    public static Valoracion Valorar(decimal? cerca, decimal? mediaProvincia, decimal? tuUltimo, decimal? media7)
    {
        if (cerca is not { } c) return new Valoracion("sin-datos", "No hay gasolineras con ese combustible en tu radio", []);
        var razones = new List<string>();
        var puntos = 0;
        if (mediaProvincia is { } mp)
        {
            var d = c - mp;
            razones.Add(d <= -0.005m ? $"{Cent(d)} más barata que la media de la provincia ({Euro(mp)})" : d >= 0.005m ? $"{Cent(d)} más cara que la media de la provincia ({Euro(mp)})" : $"En la media de la provincia ({Euro(mp)})");
            if (d <= -0.02m) puntos++; else if (d >= 0.02m) puntos--;
        }
        if (tuUltimo is { } tu)
        {
            var d = c - tu;
            razones.Add(d <= -0.005m ? $"{Cent(d)} más barata que en tu último repostaje ({Euro(tu)})" : d >= 0.005m ? $"{Cent(d)} más cara que en tu último repostaje ({Euro(tu)})" : $"Igual que en tu último repostaje ({Euro(tu)})");
            if (d <= -0.02m) puntos++; else if (d >= 0.02m) puntos--;
        }
        var sube = false; var baja = false;
        if (media7 is { } m7)
        {
            var d = c - m7;
            sube = d >= 0.01m; baja = d <= -0.01m;
            razones.Add(sube ? $"Ha subido {Cent(d)} respecto a la última semana" : baja ? $"Ha bajado {Cent(d)} respecto a la última semana" : "Se mantiene estable esta semana");
        }
        if (puntos >= 1 && !sube) return new Valoracion("bueno", "Buen momento para repostar", razones);
        if (sube) return new Valoracion("sube", "El precio está subiendo: mejor repostar pronto", razones);
        if (baja) return new Valoracion("baja", "El precio está bajando: puedes esperar unos días", razones);
        if (puntos <= -1) return new Valoracion("caro", "Está algo caro ahora mismo", razones);
        return new Valoracion("normal", "Precio normal", razones);
    }

    // lo que ahorras en un depósito repostando en la más barata: la diferencia con lo que pagaste (o con la media) por tus litros habituales
    public static decimal Ahorro(decimal litros, decimal? referencia, decimal cerca) => referencia is { } r && r > cerca ? Math.Round((r - cerca) * litros, 2, MidpointRounding.AwayFromZero) : 0;

    public static decimal LitrosTipicos(IEnumerable<Movimiento> repostajes)
    {
        var v = repostajes.Where(m => m.Litros is > 5).Select(m => m.Litros!.Value).Order().ToList();
        return v.Count == 0 ? 40 : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    public static AjustesCombustible Normalizar(AjustesCombustible a) => a with
    {
        Combustible = Combustibles.ContainsKey(a.Combustible) ? a.Combustible : "gasolina95",
        Radio = Math.Clamp(a.Radio, RadioMin, RadioMax),
        Punto = a.Punto is { } p && p.Lat is >= -90 and <= 90 && p.Lon is >= -180 and <= 180 ? p with { Nombre = p.Nombre.Length > 60 ? p.Nombre[..60] : p.Nombre } : null,
    };
}

// Quien da las gasolineras (se puede cambiar por una de mentira en las pruebas)
public interface ICarburantes
{
    // null si nunca se pudo consultar; `Antiguo` si son datos guardados porque ahora no responde
    Task<(List<Estacion> Lista, DateTimeOffset Hora, bool Antiguo)?> Obtener(CancellationToken ct);
}

public class ServicioCarburantes(IHttpClientFactory http, ILogger<ServicioCarburantes> log) : ICarburantes
{
    private const string Url = "https://sedeaplicaciones.minetur.gob.es/ServiciosRESTCarburantes/PreciosCarburantes/EstacionesTerrestres/";
    private static readonly TimeSpan Vida = TimeSpan.FromHours(3);
    private readonly SemaphoreSlim _cerrojo = new(1, 1);
    private (List<Estacion> Lista, DateTimeOffset Hora)? _ultimo;

    public async Task<(List<Estacion> Lista, DateTimeOffset Hora, bool Antiguo)?> Obtener(CancellationToken ct)
    {
        if (_ultimo is { } u && DateTimeOffset.UtcNow - u.Hora < Vida) return (u.Lista, u.Hora, false);
        await _cerrojo.WaitAsync(ct);
        try
        {
            if (_ultimo is { } v && DateTimeOffset.UtcNow - v.Hora < Vida) return (v.Lista, v.Hora, false);
            try
            {
                var json = await http.CreateClient("carburantes").GetStringAsync(Url, ct);
                var lista = Carburantes.Parsear(json);
                if (lista.Count < 100) throw new JsonException("El servicio devolvió muy pocas gasolineras");
                _ultimo = (lista, DateTimeOffset.UtcNow);
                return (lista, _ultimo.Value.Hora, false);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
                log.LogWarning(e, "No se pudieron consultar los precios de carburantes");
                return _ultimo is { } s ? (s.Lista, s.Hora, true) : null;
            }
        }
        finally { _cerrojo.Release(); }
    }
}

public static class CarburantesApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<AjustesCombustible> Leer(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Carburantes.Clave, ct))?.Valor;
        try { return v is null ? new AjustesCombustible() : Carburantes.Normalizar(JsonSerializer.Deserialize<AjustesCombustible>(v, Json) ?? new()); } catch (JsonException) { return new(); }
    }

    // lo más barato cerca y la media de la provincia de hoy para un combustible
    private static (decimal? Cerca, decimal? Media, List<EstacionCercana> Lista) Mirar(List<Estacion> todas, AjustesCombustible a, double lat, double lon)
    {
        var lista = Carburantes.Cercanas(todas, lat, lon, a.Radio, a.Combustible, 6);
        return (lista.Count > 0 ? lista[0].Precio : null, Carburantes.MediaProvincia(todas, Carburantes.ProvinciaDe(todas, lat, lon), a.Combustible), lista);
    }

    // el punto de referencia: el que pusiste o, si no, el lugar del tiempo
    private static async Task<(PuntoCombustible? Punto, string Origen)> Punto(AjustesCombustible a, ServicioTiempo tiempo, CancellationToken ct)
    {
        if (a.Punto is { } p) return (p, "propio");
        return await tiempo.LugarGuardado(ct) is { } l ? (new PuntoCombustible(l.Nombre, l.Lat, l.Lon), "tiempo") : (null, "");
    }

    // el media de lo más barato cerca en los 7 días anteriores (hacen falta al menos 3 fotos)
    public static async Task<decimal?> Media7(AppDbContext db, string combustible, DateOnly hoy, CancellationToken ct)
    {
        var v = await db.PreciosCarburante.AsNoTracking().Where(p => p.Combustible == combustible && p.Fecha < hoy && p.Fecha >= hoy.AddDays(-7)).Select(p => p.MinimoCerca).ToListAsync(ct);
        return v.Count >= 3 ? v.Average() : null;
    }

    // una foto al día (la primera consulta del día fija la de ese día; las siguientes la actualizan)
    public static async Task Registrar(AppDbContext db, DateOnly hoy, string combustible, decimal media, decimal minimo, CancellationToken ct)
    {
        var f = await db.PreciosCarburante.FindAsync([hoy, combustible], ct);
        if (f is null) db.PreciosCarburante.Add(f = new PrecioCarburante { Fecha = hoy, Combustible = combustible });
        f.Media = media; f.MinimoCerca = minimo;
        await db.SaveChangesAsync(ct);
        // se guardan 120 días
        var viejos = await db.PreciosCarburante.Where(p => p.Fecha < hoy.AddDays(-120)).ToListAsync(ct);
        if (viejos.Count > 0) { db.PreciosCarburante.RemoveRange(viejos); await db.SaveChangesAsync(ct); }
    }

    // Una vez al día (la revisión del vigilante): guarda la foto de hoy y, si lo pediste, avisa de una bajada fuerte respecto a la última semana
    public static async Task Revisar(AppDbContext db, ICarburantes servicio, ServicioTiempo tiempo, IAvisos avisos, DateOnly hoy, ILogger log, CancellationToken ct)
    {
        var a = await Leer(db, ct);
        if (await db.PreciosCarburante.AnyAsync(p => p.Fecha == hoy && p.Combustible == a.Combustible, ct) && !a.Aviso) return;
        if ((await Punto(a, tiempo, ct)).Punto is not { } punto || await servicio.Obtener(ct) is not { Antiguo: false } datos) return;
        var (cerca, media, _) = Mirar(datos.Lista, a, punto.Lat, punto.Lon);
        if (cerca is not { } c || media is not { } m) return;
        var media7 = await Media7(db, a.Combustible, hoy, ct);
        await Registrar(db, hoy, a.Combustible, m, c, ct);
        if (!a.Aviso || media7 is not { } m7 || c > m7 - 0.04m) return;      // solo una bajada clara: 4 céntimos o más
        var dia = hoy.ToString("yyyy-MM-dd");
        if ((await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(x => x.Clave == Carburantes.ClaveAvisado, ct))?.Valor == dia) return;
        var ajuste = await db.Ajustes.FirstOrDefaultAsync(x => x.Clave == Carburantes.ClaveAvisado, ct);
        if (ajuste is null) db.Ajustes.Add(new Ajuste { Clave = Carburantes.ClaveAvisado, Valor = dia }); else ajuste.Valor = dia;
        await db.SaveChangesAsync(ct);
        var mas = Carburantes.Cercanas(datos.Lista, punto.Lat, punto.Lon, a.Radio, a.Combustible, 1)[0];
        var nombre = Carburantes.Combustibles[a.Combustible].Nombre;
        var titulo = $"⛽ {nombre} más barato cerca de ti";
        var texto = $"{mas.Precio:0.000} €/L en {mas.Rotulo} ({mas.Localidad}, a {mas.Km:0.#} km): {(m7 - c) * 100:0.#} céntimos menos que la semana pasada.".Replace('.', ',');
        _ = Task.Run(async () =>
        {
            try { await avisos.PorPush(titulo, texto, "/gastos"); await avisos.PorTelegram($"{titulo}\n{texto}"); }
            catch (Exception e) { log.LogWarning(e, "No se pudo avisar del precio del combustible"); }
        });
    }

    public static void MapCarburantes(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos/combustible").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, ICarburantes servicio, ServicioTiempo tiempo, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var a = await Leer(db, ct);
            var combustibles = Carburantes.Combustibles.Select(c => new { id = c.Key, nombre = c.Value.Nombre }).ToList();
            var (punto, origen) = await Punto(a, tiempo, ct);
            if (punto is null) return Results.Ok(new { ajustes = a, combustibles, sinPunto = true });
            if (await servicio.Obtener(ct) is not { } datos) return Results.Ok(new { ajustes = a, combustibles, punto, origen, sinDatos = true });

            var hoy = Cartera.Hoy(reloj, zona);
            var (cerca, media, lista) = Mirar(datos.Lista, a, punto.Lat, punto.Lon);
            var repostajes = await db.Movimientos.AsNoTracking().Where(m => m.Litros != null && m.PrecioLitro != null && m.Fecha >= hoy.AddYears(-1)).OrderByDescending(m => m.Fecha).ToListAsync(ct);
            var ultimo = repostajes.FirstOrDefault();
            var litros = Carburantes.LitrosTipicos(repostajes);
            var media7 = await Media7(db, a.Combustible, hoy, ct);
            var valoracion = Carburantes.Valorar(cerca, media, ultimo?.PrecioLitro, media7);
            if (cerca is { } c0 && media is { } m0 && !datos.Antiguo) await Registrar(db, hoy, a.Combustible, m0, c0, ct);
            return Results.Ok(new
            {
                ajustes = a, combustibles, punto, origen, nombreCombustible = Carburantes.Combustibles[a.Combustible].Nombre,
                estaciones = lista, mediaProvincia = media, provincia = Carburantes.ProvinciaDe(datos.Lista, punto.Lat, punto.Lon), valoracion,
                tuUltimo = ultimo is null ? null : new { precio = ultimo.PrecioLitro, fecha = ultimo.Fecha }, litrosTipicos = litros,
                ahorroDeposito = cerca is { } c ? Carburantes.Ahorro(litros, ultimo?.PrecioLitro ?? media, c) : 0,
                datosDe = datos.Hora, antiguo = datos.Antiguo,
            });
        });

        g.MapPut("/", async (AjustesCombustible e, AppDbContext db, CancellationToken ct) =>
        {
            var a = Carburantes.Normalizar(e);
            var fila = await db.Ajustes.FirstOrDefaultAsync(x => x.Clave == Carburantes.Clave, ct);
            if (fila is null) db.Ajustes.Add(new Ajuste { Clave = Carburantes.Clave, Valor = JsonSerializer.Serialize(a, Json) }); else fila.Valor = JsonSerializer.Serialize(a, Json);
            await db.SaveChangesAsync(ct);
            return Results.Ok(a);
        });
    }
}
