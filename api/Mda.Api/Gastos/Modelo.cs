namespace Mda.Api.Gastos;

public enum TipoMovimiento { Gasto, Ingreso }

public enum Frecuencia { Mensual, Trimestral, Anual }

// Un apunte: un gasto o un ingreso de un día concreto.
// Los repostajes llevan además litros, precio por litro y los kilómetros del coche (para el consumo).
public class Movimiento
{
    public int Id { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public DateOnly Fecha { get; set; }
    public decimal Importe { get; set; }
    public string Categoria { get; set; } = "";
    public string Concepto { get; set; } = "";
    public string? Notas { get; set; }

    // si viene de un fijo (alquiler, Netflix, nómina…), cuál
    public int? RecurrenteId { get; set; }
    public Recurrente? Recurrente { get; set; }

    // gasolina
    public decimal? Litros { get; set; }
    public decimal? PrecioLitro { get; set; }
    public int? Kilometros { get; set; }

    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
}

// Un gasto o ingreso que se repite: suscripciones, alquiler, cuota del coche, seguro, nómina…
// «Variable» si cada mes cambia (la luz, por ejemplo): al registrarlo se pide el importe real.
public class Recurrente
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public TipoMovimiento Tipo { get; set; }
    public string Categoria { get; set; } = "";
    public decimal Importe { get; set; }
    public bool Variable { get; set; }
    public int Dia { get; set; } = 1;
    public Frecuencia Frecuencia { get; set; } = Frecuencia.Mensual;
    public DateOnly Inicio { get; set; }
    public DateOnly? Fin { get; set; }
    public bool Activo { get; set; } = true;

    // ¿Le toca este mes?
    public bool TocaEn(int anio, int mes)
    {
        var primero = new DateOnly(anio, mes, 1);
        var ultimo = primero.AddMonths(1).AddDays(-1);
        if (!Activo || Inicio > ultimo || (Fin is { } f && f < primero)) return false;
        var meses = (anio - Inicio.Year) * 12 + (mes - Inicio.Month);
        return Frecuencia switch
        {
            Frecuencia.Mensual => true,
            Frecuencia.Trimestral => meses % 3 == 0,
            Frecuencia.Anual => meses % 12 == 0,
            _ => false,
        };
    }

    // Lo que cuesta al mes, repartiendo los trimestrales y anuales
    public decimal Mensual => Frecuencia switch
    {
        Frecuencia.Trimestral => Math.Round(Importe / 3, 2),
        Frecuencia.Anual => Math.Round(Importe / 12, 2),
        _ => Importe,
    };
}

// Categorías que acepta la API (el front pone los iconos y colores con estos mismos id)
public static class Categorias
{
    public static readonly IReadOnlyDictionary<string, (string Nombre, TipoMovimiento Tipo, string Grupo)> Todas =
        new Dictionary<string, (string, TipoMovimiento, string)>
        {
            ["vivienda"] = ("Vivienda", TipoMovimiento.Gasto, "Casa"),
            ["suministros"] = ("Luz, agua e internet", TipoMovimiento.Gasto, "Casa"),
            ["supermercado"] = ("Supermercado", TipoMovimiento.Gasto, "Día a día"),
            ["restaurantes"] = ("Restaurantes", TipoMovimiento.Gasto, "Día a día"),
            ["ocio"] = ("Ocio", TipoMovimiento.Gasto, "Día a día"),
            ["ropa"] = ("Ropa", TipoMovimiento.Gasto, "Día a día"),
            ["salud"] = ("Salud", TipoMovimiento.Gasto, "Día a día"),
            ["tecnologia"] = ("Tecnología", TipoMovimiento.Gasto, "Día a día"),
            ["transporte"] = ("Transporte", TipoMovimiento.Gasto, "Día a día"),
            ["suscripciones"] = ("Suscripciones", TipoMovimiento.Gasto, "Suscripciones"),
            ["gasolina"] = ("Gasolina", TipoMovimiento.Gasto, "Coche"),
            ["seguro"] = ("Seguro", TipoMovimiento.Gasto, "Coche"),
            ["mecanico"] = ("Mecánico", TipoMovimiento.Gasto, "Coche"),
            ["cuota-coche"] = ("Cuota del coche", TipoMovimiento.Gasto, "Coche"),
            ["otros"] = ("Otros gastos", TipoMovimiento.Gasto, "Día a día"),
            ["nomina"] = ("Nómina", TipoMovimiento.Ingreso, "Ingresos"),
            ["extra"] = ("Extra", TipoMovimiento.Ingreso, "Ingresos"),
            ["ventas"] = ("Ventas", TipoMovimiento.Ingreso, "Ingresos"),
            ["otros-ingresos"] = ("Otros ingresos", TipoMovimiento.Ingreso, "Ingresos"),
        };

    public static bool Valida(string id, TipoMovimiento tipo) => Todas.TryGetValue(id, out var c) && c.Tipo == tipo;
}
