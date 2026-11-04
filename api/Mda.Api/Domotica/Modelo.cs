namespace Mda.Api.Domotica;

// Una habitación de la casa (Salón, Dormitorio, Despacho…): agrupa aparatos y es una sección del panel.
public class Estancia
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Icono { get; set; } = "🏠";
    public int Orden { get; set; }
}

// Un aparato de casa: un termómetro, una lámpara, el mando IR, la tele… «Driver» dice quién habla con él (demo, tuya, cast, hogar).
// «Config» lleva lo que necesita el driver (IP, id, clave local…): va cifrada con la clave maestra.
public class Aparato
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Driver { get; set; } = "demo";
    public int? EstanciaId { get; set; }
    public string Config { get; set; } = "";
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
}

// Un dato o un mando de un aparato (temperatura, encendido, brillo, color…), con su último valor.
public class Entidad
{
    public int Id { get; set; }
    public int AparatoId { get; set; }
    public string Clave { get; set; } = "";
    public string? Valor { get; set; }
    public DateTimeOffset? Actualizado { get; set; }
}

// Histórico de los datos numéricos (una lectura cada 5 minutos como mucho; se guardan 30 días)
public class LecturaCasa
{
    public long Id { get; set; }
    public int EntidadId { get; set; }
    public double Valor { get; set; }
    public DateTimeOffset Fecha { get; set; }
}

// Cómo tienes colocadas las tarjetas (JSON): se guarda en el servidor para verlo igual en todos tus dispositivos.
public class PanelCasa
{
    public int Id { get; set; }
    public string Clave { get; set; } = "principal";
    public string Json { get; set; } = "{}";
}
