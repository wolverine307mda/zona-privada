using System.Globalization;
using System.Text.RegularExpressions;

namespace Mda.Api.Domotica;

// Qué datos y mandos tiene cada tipo de aparato. Así la web sabe qué pintar y la API qué órdenes aceptar.
public record Campo(string Clave, string Tipo, string Etiqueta, string? Unidad = null, bool Escribible = false, double? Min = null, double? Max = null, string[]? Opciones = null);

public static class Catalogo
{
    public static readonly IReadOnlyDictionary<string, string> Tipos = new Dictionary<string, string>
    {
        ["sensor"] = "Termómetro y humedad", ["puerta"] = "Sensor de puerta", ["luz"] = "Lámpara", ["enchufe"] = "Enchufe",
        ["clima"] = "Aire acondicionado", ["tv"] = "Televisión", ["altavoz"] = "Altavoz / Chromecast", ["camara"] = "Cámara", ["ir"] = "Mando IR",
    };

    public static readonly string[] Drivers = ["demo", "tuya", "cast", "hogar"];

    public static IReadOnlyList<Campo> Campos(string tipo) => tipo switch
    {
        "sensor" => [new("temperatura", "numero", "Temperatura", "°C"), new("humedad", "numero", "Humedad", "%")],
        "puerta" => [new("abierto", "bool", "Abierta")],
        "luz" => [new("encendido", "bool", "Encendida", Escribible: true), new("brillo", "numero", "Brillo", "%", true, 1, 100), new("color", "color", "Color", Escribible: true)],
        "enchufe" => [new("encendido", "bool", "Encendido", Escribible: true), new("potencia", "numero", "Consumo", "W")],
        "clima" => [new("encendido", "bool", "Encendido", Escribible: true), new("objetivo", "numero", "Objetivo", "°C", true, 16, 30),
                    new("modo", "opcion", "Modo", Escribible: true, Opciones: ["auto", "frio", "calor", "eco"])],
        "tv" => [new("encendido", "bool", "Encendida", Escribible: true), new("volumen", "numero", "Volumen", "%", true, 0, 100)],
        "altavoz" => [new("reproduciendo", "bool", "Reproduciendo", Escribible: true), new("volumen", "numero", "Volumen", "%", true, 0, 100)],
        "camara" => [],
        "ir" => [],
        _ => [],
    };

    public static Campo? Buscar(string tipo, string clave) => Campos(tipo).FirstOrDefault(c => c.Clave == clave);

    private static readonly Regex Color = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    // Valida y normaliza el valor de una orden; devuelve el texto que se guarda o el error
    public static (string? Valor, string? Error) Normalizar(Campo c, string? bruto)
    {
        if (!c.Escribible) return (null, $"«{c.Etiqueta}» solo se lee.");
        switch (c.Tipo)
        {
            case "bool":
                if (bool.TryParse(bruto, out var b)) return (b ? "true" : "false", null);
                return (null, "Tiene que ser true o false.");
            case "numero":
                if (!double.TryParse(bruto, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || double.IsNaN(n)) return (null, "Tiene que ser un número.");
                if (c.Min is { } mi && n < mi || c.Max is { } ma && n > ma) return (null, $"De {c.Min} a {c.Max}.");
                return (n.ToString("0.##", CultureInfo.InvariantCulture), null);
            case "color":
                return bruto is not null && Color.IsMatch(bruto) ? (bruto.ToLowerInvariant(), null) : (null, "Un color como #ffb347.");
            case "opcion":
                return c.Opciones?.Contains(bruto) == true ? (bruto, null) : (null, $"Una de: {string.Join(", ", c.Opciones ?? [])}.");
            default:
                return (null, "Campo no soportado.");
        }
    }
}
