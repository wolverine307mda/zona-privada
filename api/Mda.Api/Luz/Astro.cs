namespace Mda.Api.Luz;

public record Tramo(DateTimeOffset Inicio, DateTimeOffset Fin)
{
    public TimeSpan Duracion => Fin - Inicio;
}

// Una hora de la previsión con lo que importa para el cielo (Open-Meteo, hora local del lugar)
public record Pos(double Alt, double Az);

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

    // acimut en grados desde el norte hacia el este (0 = N, 90 = E, 180 = S, 270 = O)
    private static double Acimut(double h, double phi, double dec) =>
        (Math.Atan2(Math.Sin(h), Math.Cos(h) * Math.Sin(phi) - Math.Tan(dec) * Math.Cos(phi)) / Rad + 180 + 360) % 360;

    private static Pos Posicion(double h, double lat, double dec) => new(Altura(h, Rad * lat, dec) / Rad, Acimut(h, Rad * lat, dec));

    // (ángulo horario, declinación) del sol y de la luna en ese instante
    private static (double H, double Dec) AngulosSol(DateTimeOffset t, double lon)
    {
        var d = Dias(t);
        var m = Rad * (357.5291 + 0.98560028 * d);
        var c = Rad * (1.9148 * Math.Sin(m) + 0.02 * Math.Sin(2 * m) + 0.0003 * Math.Sin(3 * m));
        var l = m + c + Rad * 102.9372 + Math.PI;
        return (Sideral(d, lon) - Ascension(l, 0), Declinacion(l, 0));
    }

    private static (double H, double Dec) AngulosLuna(DateTimeOffset t, double lon)
    {
        var d = Dias(t);
        var l0 = Rad * (218.316 + 13.176396 * d);
        var m = Rad * (134.963 + 13.064993 * d);
        var f = Rad * (93.272 + 13.229350 * d);
        var l = l0 + Rad * 6.289 * Math.Sin(m);
        var b = Rad * 5.128 * Math.Sin(f);
        return (Sideral(d, lon) - Ascension(l, b), Declinacion(l, b));
    }

    // el centro de la Vía Láctea (Sagitario): ascensión recta 17 h 45,7 min, declinación −28,94°
    private static (double H, double Dec) AngulosNucleo(DateTimeOffset t, double lon) => (Sideral(Dias(t), lon) - Rad * 266.4168, Rad * -28.9362);

    public static Pos PosSol(DateTimeOffset t, double lat, double lon) { var (h, dec) = AngulosSol(t, lon); return Posicion(h, lat, dec); }
    public static Pos PosLuna(DateTimeOffset t, double lat, double lon) { var (h, dec) = AngulosLuna(t, lon); return Posicion(h, lat, dec); }
    public static Pos PosNucleo(DateTimeOffset t, double lat, double lon) { var (h, dec) = AngulosNucleo(t, lon); return Posicion(h, lat, dec); }

    // alturas sobre el horizonte, en grados
    public static double AlturaSol(DateTimeOffset t, double lat, double lon) { var (h, dec) = AngulosSol(t, lon); return Altura(h, Rad * lat, dec) / Rad; }
    public static double AlturaLuna(DateTimeOffset t, double lat, double lon) { var (h, dec) = AngulosLuna(t, lon); return Altura(h, Rad * lat, dec) / Rad; }
    public static double AlturaNucleo(DateTimeOffset t, double lat, double lon) { var (h, dec) = AngulosNucleo(t, lon); return Altura(h, Rad * lat, dec) / Rad; }

    // lo que mide la sombra de algo de 1 m de alto (en metros); null con el sol bajo el horizonte o casi (la sombra se pierde en el infinito)
    public static double? Sombra(double alturaSol) => alturaSol < 1 ? null : Math.Min(40, 1 / Math.Tan(alturaSol * Rad));

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

    // ---- el día entero: salidas y puestas, crepúsculos y luna ----

    public record Efemerides(DateTimeOffset? Amanecer, DateTimeOffset? Atardecer, double? AzAmanecer, double? AzAtardecer,
        DateTimeOffset? CivilManana, DateTimeOffset? CivilTarde, DateTimeOffset? NauticoManana, DateTimeOffset? NauticoTarde,
        DateTimeOffset? AstroManana, DateTimeOffset? AstroTarde, DateTimeOffset Mediodia, double AlturaMediodia,
        DateTimeOffset? LunaSale, DateTimeOffset? LunaPone, string Fase, double IluminacionLuna);

    // Se recorre el día minuto a minuto y se anotan los cruces del sol por −0,833° (salida y puesta), −6°, −12° y −18°
    public static Efemerides Dia(DateOnly dia, TimeZoneInfo zona, double lat, double lon)
    {
        var ini = Local(dia, 0, zona);
        const int N = 1440;
        var sol = new double[N + 1];
        var luna = new double[N + 1];
        for (var i = 0; i <= N; i++) { var t = ini.AddMinutes(i); sol[i] = AlturaSol(t, lat, lon); luna[i] = AlturaLuna(t, lat, lon); }

        DateTimeOffset? Cruce(double[] a, double umbral, bool subiendo)
        {
            for (var i = 1; i <= N; i++)
                if (subiendo ? a[i - 1] < umbral && a[i] >= umbral : a[i - 1] >= umbral && a[i] < umbral) return ini.AddMinutes(i);
            return null;
        }
        var amanecer = Cruce(sol, -0.833, true);
        var atardecer = Cruce(sol, -0.833, false);
        var max = Enumerable.Range(0, N + 1).MaxBy(i => sol[i]);
        var noon = ini.AddMinutes(12 * 60);
        return new Efemerides(amanecer, atardecer, amanecer is { } a ? PosSol(a, lat, lon).Az : null, atardecer is { } b ? PosSol(b, lat, lon).Az : null,
            Cruce(sol, -6, true), Cruce(sol, -6, false), Cruce(sol, -12, true), Cruce(sol, -12, false), Cruce(sol, -18, true), Cruce(sol, -18, false),
            ini.AddMinutes(max), sol[max], Cruce(luna, 0, true), Cruce(luna, 0, false), NombreFase(noon), IluminacionLuna(noon));
    }

    // ---- una nota para cada momento ----

    // la «etapa» del cielo según la altura del sol
    public static string Etapa(double alturaSol) => alturaSol switch
    {
        < -18 => "noche", < -12 => "astronomico", < -6 => "nautico", < -4 => "azul", < 6 => "dorada", _ => "dia",
    };

    // De 0 a 10 para ese instante: dorada y azul premian la nube alta y castigan la baja, el día pleno es una luz dura (nota media),
    // y la noche cuenta si se ve el núcleo de la Vía Láctea y no hay luna. Sin previsión de nubes, null.
    public static int? PuntuarMomento(double alturaSol, double alturaNucleo, double alturaLuna, double iluminacionLuna, HoraNubes? h, int bortle)
    {
        if (h is null) return null;
        var una = new[] { h };
        switch (Etapa(alturaSol))
        {
            case "dorada": return PuntuarDorada(una);
            case "azul": return Math.Max(0, PuntuarDorada(una) - 1);
            case "nautico": return Math.Max(0, PuntuarDorada(una) - 3);
            case "astronomico": return Math.Max(0, PuntuarDorada(una) - 5);
            case "noche":
                var p = PuntuarNoche(una, iluminacionLuna, alturaLuna > 0, bortle);
                return alturaNucleo > 10 ? p : (int)Math.Round(p * 0.5, MidpointRounding.AwayFromZero);
            default:
                var d = 4.0 - h.Total / 100.0 + (alturaSol < 30 ? 1 : 0) - h.Lluvia / 100.0 * 3;
                return (int)Math.Round(Math.Clamp(d, 0, 6), MidpointRounding.AwayFromZero);
        }
    }

    // en una frase, por qué sale esa nota
    public static string Porque(IReadOnlyList<HoraNubes> horas)
    {
        if (horas.Count == 0) return "";
        var total = Media(horas.Select(h => h.Total)); var baja = Media(horas.Select(h => h.Baja));
        var alta = Media(horas.Select(h => (h.Media + h.Alta) / 2)); var lluvia = Media(horas.Select(h => h.Lluvia));
        if (lluvia >= 50) return "riesgo de lluvia";
        if (baja >= 60) return "nube baja que tapa el sol";
        if (total >= 90) return "cielo cubierto";
        if (total < 10) return "cielo despejado";
        if (baja < 30 && alta is >= 20 and <= 70) return "nubes altas que pueden teñirse y sin nube baja";
        return "nubosidad variable";
    }
}
