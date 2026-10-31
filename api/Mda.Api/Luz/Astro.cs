namespace Mda.Api.Luz;

public record Tramo(DateTimeOffset Inicio, DateTimeOffset Fin)
{
    public TimeSpan Duracion => Fin - Inicio;
}

// Una hora de la previsión con lo que importa para el cielo (Open-Meteo, hora local del lugar)
public record HoraNubes(DateTime Hora, int Total, int Baja, int Media, int Alta, int Lluvia, double VisibilidadKm, double Viento);

// Posición del sol, la luna y el centro de la Vía Láctea calculada aquí, sin ninguna API (fórmulas de baja precisión de
// «Astronomical Algorithms», de sobra para decidir si merece la pena salir: errores de uno o dos minutos y menos de un grado).
public static class Astro
{
    private const double Rad = Math.PI / 180;
    private const double Oblicuidad = Rad * 23.4397;
    private const double MesSinodico = 29.530588853;
    private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LunaNuevaRef = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

    private static double Dias(DateTimeOffset t) => (t.UtcDateTime - J2000).TotalDays;
    private static double Ascension(double l, double b) => Math.Atan2(Math.Sin(l) * Math.Cos(Oblicuidad) - Math.Tan(b) * Math.Sin(Oblicuidad), Math.Cos(l));
    private static double Declinacion(double l, double b) => Math.Asin(Math.Sin(b) * Math.Cos(Oblicuidad) + Math.Cos(b) * Math.Sin(Oblicuidad) * Math.Sin(l));
    private static double Sideral(double d, double lon) => Rad * (280.16 + 360.9856235 * d) + Rad * lon;
    private static double Altura(double h, double phi, double dec) => Math.Asin(Math.Sin(phi) * Math.Sin(dec) + Math.Cos(phi) * Math.Cos(dec) * Math.Cos(h));

    // alturas sobre el horizonte, en grados
    public static double AlturaSol(DateTimeOffset t, double lat, double lon)
    {
        var d = Dias(t);
        var m = Rad * (357.5291 + 0.98560028 * d);
        var c = Rad * (1.9148 * Math.Sin(m) + 0.02 * Math.Sin(2 * m) + 0.0003 * Math.Sin(3 * m));
        var l = m + c + Rad * 102.9372 + Math.PI;
        return Altura(Sideral(d, lon) - Ascension(l, 0), Rad * lat, Declinacion(l, 0)) / Rad;
    }

    public static double AlturaLuna(DateTimeOffset t, double lat, double lon)
    {
        var d = Dias(t);
        var l0 = Rad * (218.316 + 13.176396 * d);
        var m = Rad * (134.963 + 13.064993 * d);
        var f = Rad * (93.272 + 13.229350 * d);
        var l = l0 + Rad * 6.289 * Math.Sin(m);
        var b = Rad * 5.128 * Math.Sin(f);
        return Altura(Sideral(d, lon) - Ascension(l, b), Rad * lat, Declinacion(l, b)) / Rad;
    }

    // el centro de la Vía Láctea (Sagitario): ascensión recta 17 h 45,7 min, declinación −28,94°
    public static double AlturaNucleo(DateTimeOffset t, double lat, double lon) =>
        Altura(Sideral(Dias(t), lon) - Rad * 266.4168, Rad * lat, Rad * -28.9362) / Rad;

    // 0 = luna nueva, 1 = llena
    public static double IluminacionLuna(DateTimeOffset t)
    {
        var edad = ((t.UtcDateTime - LunaNuevaRef).TotalDays % MesSinodico + MesSinodico) % MesSinodico;
        return (1 - Math.Cos(2 * Math.PI * edad / MesSinodico)) / 2;
    }

    public static string NombreFase(DateTimeOffset t)
    {
        var edad = ((t.UtcDateTime - LunaNuevaRef).TotalDays % MesSinodico + MesSinodico) % MesSinodico;
        return edad switch { < 1.8 or >= 27.7 => "luna nueva", < 6.5 => "creciente", < 8.5 => "cuarto creciente", < 13.3 => "gibosa creciente", < 16.2 => "luna llena",
            < 21 => "gibosa menguante", < 23 => "cuarto menguante", _ => "menguante" };
    }

    // los tramos de [desde, hasta) (de minuto en minuto) en los que se cumple la condición
    public static List<Tramo> Tramos(Func<DateTimeOffset, bool> cond, DateTimeOffset desde, DateTimeOffset hasta)
    {
        var lista = new List<Tramo>();
        DateTimeOffset? ini = null;
        for (var t = desde; t < hasta; t = t.AddMinutes(1))
        {
            if (cond(t)) ini ??= t;
            else if (ini is { } i) { lista.Add(new Tramo(i, t)); ini = null; }
        }
        if (ini is { } u) lista.Add(new Tramo(u, hasta));
        return lista;
    }

    private static DateTimeOffset Local(DateOnly dia, int hora, TimeZoneInfo zona)
    {
        var naive = dia.ToDateTime(new TimeOnly(hora, 0));
        return new DateTimeOffset(naive, zona.GetUtcOffset(naive));
    }

    // Hora dorada: el sol entre −4° y +6° de altura, bajando (tarde) o subiendo (mañana)
    public static (Tramo? Manana, Tramo? Tarde) HoraDorada(DateOnly dia, TimeZoneInfo zona, double lat, double lon)
    {
        var ini = Local(dia, 0, zona);
        var fin = ini.AddHours(24);
        bool Dorada(DateTimeOffset t) { var a = AlturaSol(t, lat, lon); return a is > -4 and < 6; }
        var tramos = Tramos(Dorada, ini, fin);
        Tramo? m = null, tarde = null;
        foreach (var t in tramos)
        {
            var sube = AlturaSol(t.Fin, lat, lon) > AlturaSol(t.Inicio, lat, lon);
            if (sube) m ??= t; else tarde ??= t;
        }
        return (m, tarde);
    }

    // La mejor ventana de cielo oscuro de la noche que empieza el «dia»: sol a más de 18° bajo el horizonte, el núcleo
    // de la Vía Láctea a más de 10° y, o no hay luna, o es muy fina. Null si esa noche no hay.
    public static Tramo? NocheOscura(DateOnly dia, TimeZoneInfo zona, double lat, double lon)
    {
        var ini = Local(dia, 18, zona);
        var fin = ini.AddHours(14);
        bool Oscura(DateTimeOffset t) =>
            AlturaSol(t, lat, lon) < -18 && AlturaNucleo(t, lat, lon) > 10 && (AlturaLuna(t, lat, lon) < 0 || IluminacionLuna(t) < 0.25);
        return Tramos(Oscura, ini, fin).Where(t => t.Duracion >= TimeSpan.FromMinutes(20)).OrderByDescending(t => t.Duracion).FirstOrDefault();
    }

    // ---- puntuaciones de 0 a 10 ----

    private static double Media(IEnumerable<int> v) => v.DefaultIfEmpty(0).Average();

    // Hora dorada: lo mejor es poca nube baja (tapa el sol) y algo de nube media o alta (la tiñe); lluvia y mala visibilidad restan
    public static int PuntuarDorada(IReadOnlyList<HoraNubes> horas)
    {
        if (horas.Count == 0) return 0;
        var baja = Media(horas.Select(h => h.Baja));
        var alta = Media(horas.Select(h => (h.Media + h.Alta) / 2));
        var total = Media(horas.Select(h => h.Total));
        var lluvia = Media(horas.Select(h => h.Lluvia));
        var vis = horas.Average(h => h.VisibilidadKm);
        var p = 9.0 - baja / 100 * 6 - lluvia / 100 * 5;
        if (alta is >= 20 and <= 70) p += 1.5;
        if (total > 90) p -= 2;
        if (total < 5) p -= 1; // cielo vacío: bonito pero sin color
        if (vis < 10) p -= 1;
        return (int)Math.Round(Math.Clamp(p, 0, 10), MidpointRounding.AwayFromZero);
    }

    // Noche de Vía Láctea: nubes y lluvia restan mucho; la luna (si está alta) y la contaminación lumínica (Bortle 1-9) también
    public static int PuntuarNoche(IReadOnlyList<HoraNubes> horas, double iluminacionLuna, bool lunaSobreElHorizonte, int bortle)
    {
        if (horas.Count == 0) return 0;
        var total = Media(horas.Select(h => h.Total));
        var lluvia = Media(horas.Select(h => h.Lluvia));
        var p = 10.0 - total / 100 * 8 - lluvia / 100 * 4 - (Math.Clamp(bortle, 1, 9) - 1) * 0.45;
        if (lunaSobreElHorizonte) p -= iluminacionLuna * 4;
        return (int)Math.Round(Math.Clamp(p, 0, 10), MidpointRounding.AwayFromZero);
    }
}
