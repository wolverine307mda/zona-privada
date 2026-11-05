using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using SGPdotNET.CoordinateSystem;
using SGPdotNET.Observation;
using SGPdotNET.Util;

namespace Mda.Api.Luz;

// Una pasada de la Estación Espacial Internacional que se ve: sube de 10° sobre el horizonte, el Sol está al menos 6° bajo el horizonte
// (cielo ya oscuro) y la estación sigue iluminada por el Sol (no ha entrado en la sombra de la Tierra).
public record PasadaIss(DateTimeOffset Inicio, DateTimeOffset Maximo, DateTimeOffset Fin, double AlturaMax, double AzInicio, double AzMax, double AzFin, string DesdeRumbo, string HastaRumbo);

public static class Pasadas
{
    public const double ElevacionMinima = 10, SolMaximo = -6;
    public const int PasoSegundos = 20;
    private const double RadioTierraKm = 6378.137;
    private static readonly string[] Rumbos = ["N", "NE", "E", "SE", "S", "SO", "O", "NO"];

    public static string Rumbo(double azimut) => Rumbos[(int)Math.Round(((azimut % 360) + 360) % 360 / 45) % 8];

    // Dirección del Sol en coordenadas ecuatoriales (fórmula de baja precisión del Almanaque Astronómico: de sobra para saber si algo está en sombra)
    public static (double X, double Y, double Z) DireccionSol(DateTime utc)
    {
        var n = (utc - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;
        var rad = Math.PI / 180;
        var l = (280.460 + 0.9856474 * n) % 360;
        var g = ((357.528 + 0.9856003 * n) % 360) * rad;
        var lambda = (l + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * rad;
        var eps = (23.439 - 0.0000004 * n) * rad;
        return (Math.Cos(lambda), Math.Cos(eps) * Math.Sin(lambda), Math.Sin(eps) * Math.Sin(lambda));
    }

    // ¿Le da el Sol al satélite? Sombra cilíndrica: está en la sombra si está del lado de noche y a menos de un radio terrestre del eje Tierra–Sol.
    public static bool Iluminado(double x, double y, double z, (double X, double Y, double Z) sol)
    {
        var d = x * sol.X + y * sol.Y + z * sol.Z;
        if (d >= 0) return true;
        var (px, py, pz) = (x - d * sol.X, y - d * sol.Y, z - d * sol.Z);
        return Math.Sqrt(px * px + py * py + pz * pz) > RadioTierraKm;
    }

    // Las pasadas de los próximos `dias` días desde ese lugar. Con soloVisibles = false salen todas las que suben de 10° (de día también).
    public static List<PasadaIss> Calcular(string linea1, string linea2, double lat, double lon, DateTimeOffset desde, int dias, bool soloVisibles = true)
    {
        var sat = new Satellite("ISS (ZARYA)", linea1, linea2);
        var estacion = new GroundStation(new GeodeticCoordinate(Angle.FromDegrees(lat), Angle.FromDegrees(lon), 0));
        var pasadas = new List<PasadaIss>();
        var muestras = new List<(DateTime T, double Elev, double Az)>();

        void Cerrar()
        {
            if (muestras.Count == 0) return;
            var max = muestras.MaxBy(m => m.Elev);
            var primera = muestras[0]; var ultima = muestras[^1];
            // un instante suelto (menos de un minuto) no es una pasada
            if (muestras.Count >= 3)
                pasadas.Add(new PasadaIss(new DateTimeOffset(primera.T, TimeSpan.Zero), new DateTimeOffset(max.T, TimeSpan.Zero), new DateTimeOffset(ultima.T, TimeSpan.Zero),
                    Math.Round(max.Elev, 1), Math.Round(primera.Az), Math.Round(max.Az), Math.Round(ultima.Az), Rumbo(primera.Az), Rumbo(ultima.Az)));
            muestras.Clear();
        }

        var inicio = desde.UtcDateTime;
        var fin = inicio.AddDays(dias);
        for (var t = inicio; t <= fin; t = t.AddSeconds(PasoSegundos))
        {
            var obs = estacion.Observe(sat, t);
            var elev = obs.Elevation.Degrees;
            var cuenta = elev >= ElevacionMinima;
            if (cuenta && soloVisibles)
            {
                var p = sat.Predict(t).Position;
                cuenta = Astro.AlturaSol(new DateTimeOffset(t, TimeSpan.Zero), lat, lon) <= SolMaximo && Iluminado(p.X, p.Y, p.Z, DireccionSol(t));
            }
            if (cuenta) muestras.Add((t, elev, obs.Azimuth.Degrees)); else Cerrar();
        }
        Cerrar();
        return pasadas;
    }

    // Qué pasada merece un aviso ahora: una buena (sube de 40°) que empiece en los próximos 45 minutos, o, por la tarde-noche, la que haya de madrugada
    // (el vigilante no avisa entre las 23 y las 9). Nunca dos veces la misma: `ultimaAvisada` es el inicio de la última que se avisó.
    public static PasadaIss? ParaAvisar(IReadOnlyList<PasadaIss> pasadas, DateTimeOffset ahora, TimeZoneInfo zona, DateTimeOffset? ultimaAvisada, double alturaBuena = 40)
    {
        var local = TimeZoneInfo.ConvertTime(ahora, zona);
        foreach (var p in pasadas.Where(p => p.AlturaMax >= alturaBuena && p.Inicio > ahora && (ultimaAvisada is null || p.Inicio > ultimaAvisada)).OrderBy(p => p.Inicio))
        {
            var cuando = p.Inicio - ahora;
            if (cuando <= TimeSpan.FromMinutes(45)) return p;
            var inicioLocal = TimeZoneInfo.ConvertTime(p.Inicio, zona);
            // a partir de las 20:00, la de antes de las 9:00 de mañana (no habrá otro repaso antes de que ocurra)
            if (local.Hour >= 20 && inicioLocal.Date == local.Date.AddDays(1) && inicioLocal.Hour < 9) return p;
        }
        return null;
    }

    // Las líneas de un TLE de CelesTrak (con o sin la línea del nombre); null si no valen
    public static (string L1, string L2)? LeerTle(string texto)
    {
        var lineas = texto.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var l1 = lineas.FirstOrDefault(l => l.StartsWith("1 ") && l.Length >= 69);
        var l2 = lineas.FirstOrDefault(l => l.StartsWith("2 ") && l.Length >= 69);
        return l1 is null || l2 is null ? null : (l1, l2);
    }

    // Época del TLE (día del año con fracción, en las columnas 19-32 de la línea 1)
    public static DateTimeOffset? Epoca(string linea1)
    {
        if (linea1.Length < 32 || !int.TryParse(linea1.AsSpan(18, 2), out var aa) || !double.TryParse(linea1.AsSpan(20, 12), NumberStyles.Float, CultureInfo.InvariantCulture, out var dia)) return null;
        var año = aa < 57 ? 2000 + aa : 1900 + aa;
        return new DateTimeOffset(new DateTime(año, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(dia - 1), TimeSpan.Zero);
    }
}

// El TLE de la ISS de CelesTrak (gratis y sin clave). Se guarda 12 horas y nunca se pide más de una vez cada 2 horas aunque falle; si no hay red,
// vale el último bueno (un TLE de la ISS aguanta bien una o dos semanas).
public class ServicioIss(IHttpClientFactory http, IMemoryCache cache, TimeProvider reloj, ILogger<ServicioIss> log)
{
    public const string Url = "https://celestrak.org/NORAD/elements/gp.php?CATNR=25544&FORMAT=tle";
    private (string L1, string L2)? _ultimoBueno;
    private DateTimeOffset _ultimoIntento = DateTimeOffset.MinValue;

    public async Task<(string L1, string L2, DateTimeOffset? Epoca)?> Tle(CancellationToken ct)
    {
        if (cache.TryGetValue("iss-tle", out (string, string)? guardado) && guardado is { } g) return (g.Item1, g.Item2, Pasadas.Epoca(g.Item1));
        if (reloj.GetUtcNow() - _ultimoIntento >= TimeSpan.FromHours(2))
        {
            _ultimoIntento = reloj.GetUtcNow();
            try
            {
                var texto = await http.CreateClient("tiempo").GetStringAsync(Url, ct);
                if (Pasadas.LeerTle(texto) is { } tle)
                {
                    _ultimoBueno = tle;
                    cache.Set("iss-tle", tle, TimeSpan.FromHours(12));
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { log.LogWarning("No se pudo bajar el TLE de la ISS ({Motivo}); se usa el último", e.GetType().Name); }
        }
        return _ultimoBueno is { } u ? (u.L1, u.L2, Pasadas.Epoca(u.L1)) : null;
    }
}
