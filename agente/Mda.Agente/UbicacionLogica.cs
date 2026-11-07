using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// La parte de «ubicación del iPhone» que no toca el equipo (se puede probar en cualquier sitio).
// La orden lleva el parámetro en una gramática cerrada, que la API valida y el agente vuelve a validar:
//   estado
//   parar
//   fijar:LAT,LON:MINUTOS           (salto directo; se restaura sola a los MINUTOS)
//   ir:LAT,LON:MINUTOS:MODO         (camina desde donde estés simulando hasta el destino; MODO = andar | bici | coche)
static class UbicacionLogica
{
    public const int MinutosMaximos = 720;
    // km/h de cada modo
    public static readonly Dictionary<string, double> Velocidades = new() { ["andar"] = 5, ["bici"] = 15, ["coche"] = 50 };

    public record Peticion(string Accion, double Lat, double Lon, int Minutos, string Modo);

    public static bool CoordenadasValidas(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    // «40.41683» y nada más: sin exponentes, sin espacios, con punto decimal
    static readonly Regex Numero = new(@"^-?\d{1,3}(\.\d{1,6})?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Formato(double v) => Math.Round(v, 5).ToString("0.#####", CultureInfo.InvariantCulture);

    public static string Parametro(string accion, double lat, double lon, int minutos, string? modo = null) =>
        accion == "ir" ? $"ir:{Formato(lat)},{Formato(lon)}:{minutos}:{modo}" : $"fijar:{Formato(lat)},{Formato(lon)}:{minutos}";

    public static Peticion? Leer(string? parametro)
    {
        var p = (parametro ?? "").Trim();
        if (p is "estado" or "parar") return new(p, 0, 0, 0, "");
        var partes = p.Split(':');
        if (partes is not [var accion, var punto, var min, ..] || accion is not ("fijar" or "ir")) return null;
        if (partes.Length != (accion == "ir" ? 4 : 3)) return null;
        var modo = accion == "ir" ? partes[3] : "";
        if (accion == "ir" && !Velocidades.ContainsKey(modo)) return null;
        var coords = punto.Split(',');
        if (coords.Length != 2 || !Numero.IsMatch(coords[0]) || !Numero.IsMatch(coords[1])) return null;
        var lat = double.Parse(coords[0], CultureInfo.InvariantCulture);
        var lon = double.Parse(coords[1], CultureInfo.InvariantCulture);
        if (!CoordenadasValidas(lat, lon)) return null;
        if (!int.TryParse(min, NumberStyles.None, CultureInfo.InvariantCulture, out var minutos) || minutos is < 1 or > MinutosMaximos) return null;
        return new(accion, lat, lon, minutos, modo);
    }

    // Distancia en metros entre dos puntos (haversine)
    public static double Distancia(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        static double Rad(double g) => g * Math.PI / 180;
        var dLat = Rad(lat2 - lat1); var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    // Punto a la fracción f (0–1) del camino entre dos puntos. Línea recta (sin calles): para trayectos cortos es suficiente.
    public static (double Lat, double Lon) Interpolar(double lat1, double lon1, double lat2, double lon2, double f)
    {
        f = Math.Clamp(f, 0, 1);
        var dLon = lon2 - lon1;
        if (dLon > 180) dLon -= 360; else if (dLon < -180) dLon += 360; // por el lado corto del planeta
        var lon = lon1 + dLon * f;
        if (lon > 180) lon -= 360; else if (lon < -180) lon += 360;
        return (lat1 + (lat2 - lat1) * f, lon);
    }

    // Segundos que tarda el trayecto a la velocidad del modo (al menos 1)
    public static int Segundos(double metros, string modo) => Math.Max(1, (int)Math.Ceiling(metros / (Velocidades[modo] / 3.6)));

    // El recorrido como GPX con una hora por punto (pymobiledevice3 lo reproduce respetando el ritmo). Hasta unos 3.600 puntos: si el trayecto es largo, un punto cada varios segundos.
    public static string Gpx(double lat1, double lon1, double lat2, double lon2, int segundos, DateTime inicioUtc)
    {
        var paso = Math.Max(1, (int)Math.Ceiling(segundos / 3600.0));
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<gpx version=\"1.1\" creator=\"zona-privada\" xmlns=\"http://www.topografix.com/GPX/1/1\"><trk><trkseg>\n");
        for (var t = 0; ; t += paso)
        {
            var s = Math.Min(t, segundos);
            var (la, lo) = Interpolar(lat1, lon1, lat2, lon2, (double)s / segundos);
            sb.Append(CultureInfo.InvariantCulture, $"<trkpt lat=\"{la:0.000000}\" lon=\"{lo:0.000000}\"><time>{inicioUtc.AddSeconds(s):yyyy-MM-ddTHH:mm:ssZ}</time></trkpt>\n");
            if (s >= segundos) break;
        }
        return sb.Append("</trkseg></trk></gpx>\n").ToString();
    }

    // Lo que dice pymobiledevice3 cuando algo falla, en cristiano y con lo que hay que hacer
    public static string Motivo(string salida)
    {
        var s = salida ?? "";
        bool Tiene(string t) => s.Contains(t, StringComparison.OrdinalIgnoreCase);
        if (Tiene("No module named") || Tiene("no se reconoce") || Tiene("not recognized") || Tiene("cannot find the file")) return "falta pymobiledevice3 en el PC («pip install -U pymobiledevice3»)";
        if (Tiene("DeveloperMode") || Tiene("developer mode")) return "activa el modo desarrollador en el iPhone (Ajustes › Privacidad y seguridad › Modo desarrollador) y reinícialo";
        if (Tiene("NoDeviceConnected") || Tiene("No device") || Tiene("no devices")) return "no veo el iPhone: conéctalo por cable, desbloquéalo y pulsa «Confiar»";
        if (Tiene("PairingError") || Tiene("pair") && Tiene("trust")) return "el iPhone no se fía de este PC: desbloquéalo y pulsa «Confiar»";
        if (Tiene("admin") || Tiene("root") || Tiene("PermissionError") || Tiene("Access is denied")) return "el agente necesita ejecutarse como administrador para abrir el túnel con el iPhone";
        if (Tiene("Apple Mobile Device") || Tiene("usbmuxd") || Tiene("ConnectionRefused")) return "Windows no ve el servicio USB de Apple (instala iTunes o «Apple Devices»)";
        var ultima = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        return ultima.Length == 0 ? "pymobiledevice3 se cerró sin decir por qué" : "pymobiledevice3: " + (ultima.Length > 120 ? ultima[..120] : ultima);
    }

    // «usbmux list» devuelve (en las versiones recientes) un JSON con los aparatos; si no, se dice solo que hay algo conectado
    public static string? Aparato(string salida)
    {
        var i = (salida ?? "").IndexOf('[');
        var f = (salida ?? "").LastIndexOf(']');
        if (i < 0 || f <= i) return null;
        try
        {
            using var doc = JsonDocument.Parse(salida![i..(f + 1)]);
            foreach (var d in doc.RootElement.EnumerateArray())
            {
                if (d.ValueKind != JsonValueKind.Object) continue;
                string? Texto(string k) => d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var nombre = Texto("DeviceName") ?? Texto("Identifier") ?? Texto("SerialNumber") ?? "iPhone";
                var version = Texto("ProductVersion");
                return version is null ? nombre : $"{nombre} (iOS {version})";
            }
        }
        catch (JsonException) { /* otra salida: se trata como «no sé» */ }
        return null;
    }

    public static string Hora(DateTimeOffset h) => h.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
}
