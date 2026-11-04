namespace Mda.Api.Luz;

// Lo que pasa en el cielo y no depende del tiempo: las lluvias de meteoros de cada año y los eclipses (fechas fijas, calculadas de antemano).
// El pico de las lluvias se pone a las 03:00 locales de la noche de máximo; la luna se calcula con Astro, las nubes (si hay previsión) las pone el endpoint.
public record EventoCielo(string Tipo, string Nombre, DateTimeOffset Pico, string Detalle, int? Zhr, int LunaPct, string Cielo);

public static class Eventos
{
    // nombre, mes, día del máximo, meteoros por hora en el mejor caso, de dónde vienen
    private static readonly (string Nombre, int Mes, int Dia, int Zhr, string Radiante)[] Lluvias =
    [
        ("Cuadrántidas", 1, 4, 80, "Boyero, hacia el noreste de madrugada"),
        ("Líridas", 4, 22, 18, "Lira, alta de madrugada"),
        ("Eta Acuáridas", 5, 6, 50, "Acuario, sale poco antes del amanecer"),
        ("Delta Acuáridas del sur", 7, 30, 25, "Acuario, al sur de madrugada"),
        ("Perseidas", 8, 12, 100, "Perseo, al noreste desde la medianoche"),
        ("Oriónidas", 10, 21, 20, "Orión, al sur de madrugada"),
        ("Táuridas del sur", 11, 5, 5, "Tauro, toda la noche (pocas pero brillantes)"),
        ("Leónidas", 11, 17, 15, "Leo, de madrugada"),
        ("Gemínidas", 12, 14, 150, "Géminis, casi toda la noche"),
        ("Úrsidas", 12, 22, 10, "Osa Menor, de madrugada"),
    ];

    // (tipo, fecha y hora UTC del máximo, qué es y desde dónde se ve). Solo los que se ven, al menos en parte, desde España.
    private static readonly (string Tipo, string Cuando, string Nombre, string Detalle)[] Eclipses =
    [
        ("eclipse-solar", "2026-08-12T17:46:00Z", "Eclipse total de Sol", "Total en el norte de España; parcial en el resto. Protege los ojos y la cámara con filtro solar."),
        ("eclipse-lunar", "2026-08-28T04:13:00Z", "Eclipse parcial de Luna", "Se ve entero desde España de madrugada."),
        ("eclipse-solar", "2027-08-02T10:07:00Z", "Eclipse total de Sol", "Total en el sur de la península; parcial en el resto. Filtro solar obligatorio."),
        ("eclipse-solar", "2028-01-26T15:08:00Z", "Eclipse anular de Sol", "Anular en el suroeste peninsular; parcial en el resto. Filtro solar obligatorio."),
        ("eclipse-lunar", "2028-07-06T18:20:00Z", "Eclipse parcial de Luna", "Empieza con la Luna recién salida por el este: busca un horizonte despejado."),
        ("eclipse-lunar", "2028-12-31T16:52:00Z", "Eclipse total de Luna", "Total; la Luna sale ya eclipsada o a punto, con el horizonte este despejado."),
    ];

    private static string Cielo(int lunaPct) => lunaPct switch { < 15 => "cielo oscuro", < 45 => "luna discreta", < 75 => "luna molesta", _ => "luna que borra casi todo" };

    // lo que viene en los próximos `dias` días, por orden de fecha
    public static List<EventoCielo> Proximos(DateTimeOffset ahora, TimeZoneInfo zona, int dias = 120)
    {
        var hasta = ahora.AddDays(dias);
        var lista = new List<EventoCielo>();
        for (var año = ahora.Year; año <= hasta.Year; año++)
            foreach (var (nombre, mes, dia, zhr, radiante) in Lluvias)
            {
                var local = new DateTime(año, mes, dia, 3, 0, 0);
                var pico = new DateTimeOffset(local, zona.GetUtcOffset(local));
                if (pico < ahora.AddHours(-6) || pico > hasta) continue;
                var luna = (int)Math.Round(Astro.IluminacionLuna(pico) * 100);
                lista.Add(new EventoCielo("lluvia", nombre, pico, $"Máximo de la noche; hasta {zhr} por hora con cielo oscuro. Radiante: {radiante}.", zhr, luna, Cielo(luna)));
            }
        foreach (var (tipo, cuando, nombre, detalle) in Eclipses)
        {
            var pico = DateTimeOffset.Parse(cuando, System.Globalization.CultureInfo.InvariantCulture);
            if (pico < ahora.AddHours(-6) || pico > hasta) continue;
            lista.Add(new EventoCielo(tipo, nombre, pico, detalle, null, (int)Math.Round(Astro.IluminacionLuna(pico) * 100), ""));
        }
        return lista.OrderBy(e => e.Pico).ToList();
    }
}
