using System.IO.Compression;
using System.Threading.Channels;
using MaxMind.Db;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

public class SondaRegistrada
{
    public int Id { get; set; }
    public DateTimeOffset Hora { get; set; }
    public string Ip { get; set; } = "";
    public string Ruta { get; set; } = "";
    public string Pais { get; set; } = "";
    public string Ciudad { get; set; } = "";
    public double? Lat { get; set; }
    public double? Lon { get; set; }
}

public record Geo(string Pais, string Ciudad, double Lat, double Lon);
public record PuntoSonda(double Lat, double Lon, string Pais, string Ciudad, int Sondas, int Ips, DateTimeOffset Ultima, IReadOnlyList<string> Rutas);

// Dónde están los que te sondean: la base DB-IP Lite (gratis, sin cuenta, se baja sola cada mes) pone coordenadas a cada IP, y cada sonda queda guardada 90 días
// para pintarla en el globo de Seguridad. Sin la base, las sondas se guardan igual, solo que sin punto en el mapa.
public sealed class GeoIp(IConfiguration config, ILogger<GeoIp> log) : IDisposable
{
    private Reader? _lector;
    public string Ruta => config["GeoIp:Ruta"] is { Length: > 0 } r ? r : Path.Combine(config["Galeria:Derivados"] ?? Path.GetTempPath(), "dbip-city-lite.mmdb");
    public bool Disponible => _lector is not null;

    public void Abrir()
    {
        try
        {
            if (!File.Exists(Ruta)) return;
            var nuevo = new Reader(Ruta);
            var viejo = Interlocked.Exchange(ref _lector, nuevo);
            viejo?.Dispose();
        }
        catch (Exception e) when (e is InvalidDatabaseException or IOException or UnauthorizedAccessException) { log.LogWarning("No se pudo abrir la base de geolocalización ({Motivo})", e.GetType().Name); }
    }

    public Geo? Buscar(string ip)
    {
        if (_lector is null || !System.Net.IPAddress.TryParse(ip, out var a)) return null;
        try { return Interpretar(_lector.Find<Dictionary<string, object>>(a)); }
        catch (Exception e) when (e is InvalidDatabaseException or ObjectDisposedException) { return null; }
    }

    // el formato MaxMind: { country: { iso_code, names: { en } }, city: { names: { en } }, location: { latitude, longitude } }
    public static Geo? Interpretar(IReadOnlyDictionary<string, object>? d)
    {
        if (d is null || !d.TryGetValue("location", out var l) || l is not IReadOnlyDictionary<string, object> loc) return null;
        if (!loc.TryGetValue("latitude", out var la) || !loc.TryGetValue("longitude", out var lo)) return null;
        double lat = Convert.ToDouble(la, System.Globalization.CultureInfo.InvariantCulture), lon = Convert.ToDouble(lo, System.Globalization.CultureInfo.InvariantCulture);
        if (lat is < -90 or > 90 || lon is < -180 or > 180) return null;
        string Texto(string clave) => d.TryGetValue(clave, out var x) && x is IReadOnlyDictionary<string, object> m
            ? (m.TryGetValue("iso_code", out var iso) && clave == "country" ? iso?.ToString() : m.TryGetValue("names", out var n) && n is IReadOnlyDictionary<string, object> nn && nn.TryGetValue("en", out var en) ? en?.ToString() : null) ?? "" : "";
        return new Geo(Texto("country"), Texto("city"), lat, lon);
    }

    public void Dispose() => _lector?.Dispose();
}

// Baja la base de DB-IP Lite si falta o tiene más de 35 días (la publican el día 1 de cada mes) y la deja lista sin reiniciar.
public sealed class ActualizadorGeoIp(GeoIp geo, IHttpClientFactory http, TimeProvider reloj, ILogger<ActualizadorGeoIp> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        geo.Abrir();
        try { await Task.Delay(TimeSpan.FromMinutes(5), reloj, ct); } catch (OperationCanceledException) { return; }
        using var t = new PeriodicTimer(TimeSpan.FromHours(24), reloj);
        try
        {
            do
            {
                try { await Actualizar(ct); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("No se pudo actualizar la base de geolocalización ({Motivo})", e.GetType().Name); }
            } while (await t.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    public async Task Actualizar(CancellationToken ct)
    {
        if (File.Exists(geo.Ruta) && reloj.GetUtcNow() - File.GetLastWriteTimeUtc(geo.Ruta) < TimeSpan.FromDays(35)) return;
        var hoy = reloj.GetUtcNow();
        // el archivo del mes en curso sale el día 1; si todavía no está, se prueba el del mes anterior
        foreach (var mes in new[] { hoy, hoy.AddMonths(-1) })
        {
            var url = $"https://download.db-ip.com/free/dbip-city-lite-{mes:yyyy-MM}.mmdb.gz";
            using var cliente = http.CreateClient("avisos");
            cliente.Timeout = TimeSpan.FromMinutes(10);
            using var r = await cliente.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!r.IsSuccessStatusCode) continue;
            var temporal = geo.Ruta + ".nuevo";
            await using (var origen = await r.Content.ReadAsStreamAsync(ct))
            await using (var gz = new GZipStream(origen, CompressionMode.Decompress))
            await using (var destino = File.Create(temporal))
                await gz.CopyToAsync(destino, ct);
            File.Move(temporal, geo.Ruta, overwrite: true);
            geo.Abrir();
            log.LogInformation("Base de geolocalización actualizada ({Mes})", mes.ToString("yyyy-MM"));
            return;
        }
    }
}

// Guarda cada sonda (con su sitio en el mapa si se sabe). La petición no espera: se encola y esto escribe en segundo plano.
public sealed class RegistroSondas(IServiceScopeFactory scopes, GeoIp geo, TimeProvider reloj, ILogger<RegistroSondas> log) : BackgroundService
{
    public static readonly TimeSpan Retencion = TimeSpan.FromDays(90);
    private readonly Channel<(DateTimeOffset, string, string)> _cola = Channel.CreateBounded<(DateTimeOffset, string, string)>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Encolar(string ip, string ruta) => _cola.Writer.TryWrite((reloj.GetUtcNow(), ip, ruta.Length > 200 ? ruta[..200] : ruta));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var ultimaLimpieza = DateTimeOffset.MinValue;
        try
        {
            await foreach (var (hora, ip, ruta) in _cola.Reader.ReadAllAsync(ct))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var g = geo.Buscar(ip);
                    db.SondasRegistradas.Add(new SondaRegistrada { Hora = hora, Ip = ip, Ruta = ruta, Pais = g?.Pais ?? "", Ciudad = g?.Ciudad ?? "", Lat = g?.Lat, Lon = g?.Lon });
                    if (hora - ultimaLimpieza > TimeSpan.FromHours(12))
                    {
                        ultimaLimpieza = hora;
                        var corte = hora - Retencion;
                        await db.SondasRegistradas.Where(s => s.Hora < corte).ExecuteDeleteAsync(ct);
                    }
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("No se pudo guardar una sonda ({Motivo})", e.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { }
    }
}

public static class MapaSondas
{
    // los puntos del globo: sondas agrupadas por sitio (coordenadas a 1 decimal, unos 10 km)
    public static List<PuntoSonda> Agrupar(IEnumerable<SondaRegistrada> sondas) =>
        sondas.Where(s => s.Lat is not null && s.Lon is not null).GroupBy(s => (Math.Round(s.Lat!.Value, 1), Math.Round(s.Lon!.Value, 1)))
            .Select(g => new PuntoSonda(g.Key.Item1, g.Key.Item2, g.First().Pais, g.First().Ciudad, g.Count(), g.Select(s => s.Ip).Distinct().Count(), g.Max(s => s.Hora),
                g.GroupBy(s => s.Ruta).OrderByDescending(r => r.Count()).Take(3).Select(r => r.Key).ToList()))
            .OrderByDescending(p => p.Sondas).ToList();

    public static void MapSondas(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/seguridad/sondas", async (int? dias, AppDbContext db, GeoIp geo, TimeProvider reloj, CancellationToken ct) =>
        {
            var d = Math.Clamp(dias ?? 30, 1, 90);
            var desde = reloj.GetUtcNow().AddDays(-d);
            var lista = await db.SondasRegistradas.AsNoTracking().Where(s => s.Hora >= desde).ToListAsync(ct);
            return Results.Ok(new
            {
                dias = d, total = lista.Count, ips = lista.Select(s => s.Ip).Distinct().Count(), sinSitio = lista.Count(s => s.Lat is null),
                geoIp = geo.Disponible, puntos = Agrupar(lista),
                paises = lista.Where(s => s.Pais.Length > 0).GroupBy(s => s.Pais).OrderByDescending(g => g.Count()).Take(8).Select(g => new { pais = g.Key, sondas = g.Count() }),
            });
        }).RequireAuthorization();
    }
}
