// La temperatura del procesador según Windows: las zonas térmicas de ACPI (WMI, «MSAcpi_ThermalZoneTemperature») la dan en décimas de kelvin.
// Ojo: es la zona térmica de la placa, que en muchos portátiles sigue de cerca al procesador pero no es la lectura del propio chip, y hay equipos
// que no la exponen. Sin nada de Windows para poder probar la conversión.
static class Termica
{
    // 3321 décimas de kelvin = 332,1 K = 59 °C; fuera de un rango creíble (10 a 125 °C) se descarta (algunas placas dan valores falsos)
    public static double? DeDecimasDeKelvin(long valor)
    {
        var c = valor / 10.0 - 273.15;
        return c is >= 10 and <= 125 ? Math.Round(c, 1) : null;
    }

    // la zona más caliente de las que den un valor creíble
    public static double? Maxima(IEnumerable<long> zonas)
    {
        var temperaturas = zonas.Select(DeDecimasDeKelvin).Where(t => t is not null).Select(t => t!.Value).ToList();
        return temperaturas.Count > 0 ? temperaturas.Max() : null;
    }
}
