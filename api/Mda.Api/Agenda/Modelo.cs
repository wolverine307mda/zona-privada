namespace Mda.Api.Agenda;

public enum TipoEvento { Evento, Recordatorio }
public enum Repeticion { No, Diaria, Semanal, Mensual, Anual }

// Un evento (con duración) o un recordatorio (un momento). Puede repetirse, avisar antes por correo o WhatsApp
// y, si Google Calendar está enlazado, copiarse allí (GoogleId).
public class Evento
{
    public int Id { get; set; }
    public TipoEvento Tipo { get; set; }
    public string Titulo { get; set; } = "";
    public string? Notas { get; set; }
    public string? Lugar { get; set; }
    public DateTimeOffset Inicio { get; set; }
    public DateTimeOffset? Fin { get; set; }
    public bool TodoElDia { get; set; }
    public string Color { get; set; } = "";
    public Repeticion Repetir { get; set; }
    public DateOnly? RepetirHasta { get; set; }

    // aviso: minutos antes del inicio (negativo = después, para «el mismo día a las 9» en los de todo el día)
    public int? AvisoMinutos { get; set; }
    public bool PorCorreo { get; set; }
    public bool PorWhatsApp { get; set; }
    public bool PorPush { get; set; }
    public bool PorTelegram { get; set; }
    public DateTimeOffset? ProximoAviso { get; set; }
    public DateTimeOffset? AvisoDe { get; set; } // la repetición a la que corresponde ese aviso
    public DateTimeOffset? UltimoAviso { get; set; }
    // estado del envío de la repetición pendiente (AvisoDe): canales ya entregados y canales a los que se renunció tras los reintentos
    // (bits de Via: 1 correo, 2 WhatsApp, 4 Telegram, 8 push), fallos seguidos y desde cuándo se puede reintentar
    public int CanalesEnviados { get; set; }
    public int CanalesRendidos { get; set; }
    public int Intentos { get; set; }
    public DateTimeOffset? ReintentoDesde { get; set; }
    // la última repetición cuyo aviso ya se resolvió: editar el evento no la vuelve a avisar
    public DateTimeOffset? UltimaOcurrenciaAvisada { get; set; }

    public bool Hecho { get; set; }
    public bool EnGoogle { get; set; }
    public string? GoogleId { get; set; }
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
}

// Zona horaria de la casa: las repeticiones («cada mes a las 10») se cuentan en hora local
public record Zona(TimeZoneInfo Info)
{
    public static Zona Desde(IConfiguration config)
    {
        try { return new(TimeZoneInfo.FindSystemTimeZoneById(config["App:Zona"] ?? "Europe/Madrid")); }
        catch (TimeZoneNotFoundException) { return new(TimeZoneInfo.Local); }
    }
}

public static class Ocurrencias
{
    private const int Maximo = 5000;

    private static DateTime Paso(DateTime local, Repeticion r, int i) => r switch
    {
        Repeticion.Diaria => local.AddDays(i),
        Repeticion.Semanal => local.AddDays(7 * i),
        Repeticion.Mensual => local.AddMonths(i), // el 31 cae el último día en los meses cortos
        Repeticion.Anual => local.AddYears(i),
        _ => local,
    };

    // Saltos iniciales para no recorrer años de repeticiones diarias desde el principio
    private static int Salto(DateTime local, Repeticion r, DateTime objetivo) => objetivo <= local ? 0 : r switch
    {
        Repeticion.Diaria => Math.Max(0, (int)(objetivo - local).TotalDays - 1),
        Repeticion.Semanal => Math.Max(0, (int)(objetivo - local).TotalDays / 7 - 1),
        Repeticion.Mensual => Math.Max(0, (objetivo.Year - local.Year) * 12 + objetivo.Month - local.Month - 1),
        Repeticion.Anual => Math.Max(0, objetivo.Year - local.Year - 1),
        _ => 0,
    };

    // Inicios de cada repetición que se solapa con [desde, hasta)
    public static IEnumerable<DateTimeOffset> Entre(Evento e, DateTimeOffset desde, DateTimeOffset hasta, TimeZoneInfo zona)
    {
        var dur = (e.Fin ?? e.Inicio) - e.Inicio;
        if (e.Repetir == Repeticion.No)
        {
            if (e.Inicio < hasta && e.Inicio + dur >= desde) yield return e.Inicio;
            yield break;
        }
        var local = TimeZoneInfo.ConvertTime(e.Inicio, zona).DateTime;
        var limite = e.RepetirHasta?.ToDateTime(TimeOnly.MaxValue);
        var i0 = Salto(local, e.Repetir, TimeZoneInfo.ConvertTime(desde - dur, zona).DateTime);
        for (var i = i0; i < i0 + Maximo; i++)
        {
            var l = Paso(local, e.Repetir, i);
            if (limite is not null && l > limite) yield break;
            var o = new DateTimeOffset(l, zona.GetUtcOffset(l));
            if (o >= hasta) yield break;
            if (o + dur >= desde) yield return o;
        }
    }

    // La primera repetición que empieza en «desde» o después
    public static DateTimeOffset? Siguiente(Evento e, DateTimeOffset desde, TimeZoneInfo zona)
    {
        foreach (var o in Entre(e, desde, desde.AddYears(5), zona))
            if (o >= desde) return o;
        return null;
    }

    // El próximo aviso: para la primera repetición desde «desde» (si su hora de aviso ya pasó, en cuanto se pueda).
    // Null si no avisa, ya está hecho o no quedan repeticiones.
    public static (DateTimeOffset Aviso, DateTimeOffset De)? ProximoAviso(Evento e, DateTimeOffset desde, DateTimeOffset ahora, TimeZoneInfo zona)
    {
        if (e.AvisoMinutos is not { } min || e.Hecho || (!e.PorCorreo && !e.PorWhatsApp && !e.PorPush && !e.PorTelegram)) return null;
        var antes = TimeSpan.FromMinutes(min);
        // con aviso «después» (negativo), la repetición que ya empezó hoy aún cuenta
        if (Siguiente(e, antes < TimeSpan.Zero ? desde + antes : desde, zona) is not { } o) return null;
        var a = o - antes;
        return (a < ahora ? ahora : a, o);
    }
}
