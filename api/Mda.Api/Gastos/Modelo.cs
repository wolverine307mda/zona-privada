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

    // si es un pago de una deuda (el coche, un préstamo…), de cuál
    public int? DeudaId { get; set; }

    // gasolina
    public decimal? Litros { get; set; }
    public decimal? PrecioLitro { get; set; }
    public int? Kilometros { get; set; }

    // foto del ticket (en la tabla de adjuntos, con la clave «ticket:{Id}»)
    public bool TieneTicket { get; set; }

    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;

    // entró solo (Apple Pay) y espera a que lo des por bueno; cuenta en el saldo igualmente
    public bool PorConfirmar { get; set; }

    // dónde se pagó (si el atajo del iPhone lo mandó), con ~100 m de precisión
    public double? Lat { get; set; }
    public double? Lon { get; set; }
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
        Frecuencia.Trimestral => Math.Round(Importe / 3, 2, MidpointRounding.AwayFromZero),
        Frecuencia.Anual => Math.Round(Importe / 12, 2, MidpointRounding.AwayFromZero),
        _ => Importe,
    };
}

// Algo que se va pagando poco a poco hasta llegar a un total: el coche, un préstamo, una compra a plazos…
// Lo pagado es la suma de los apuntes enlazados (Movimiento.DeudaId).
public class Deuda
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public decimal Total { get; set; }
    public string Categoria { get; set; } = "cuota-coche";
    public DateOnly Inicio { get; set; }
    public string? Notas { get; set; }
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
}

// Una hucha: dinero apartado para algo (un viaje, el colchón de emergencia…). Lo apartado sigue siendo tuyo:
// no cambia el saldo, solo dice cuánto de él ya tiene destino.
public class Hucha
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public decimal Objetivo { get; set; }
    public decimal Ahorrado { get; set; }
    public string Color { get; set; } = "";
    public string Emoji { get; set; } = "🐷";
    public DateOnly? Fecha { get; set; }
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
}

// Límite de gasto al mes en una categoría. Avisa una vez al pasar del 80 % y otra al pasar del 100 %.
public class Presupuesto
{
    public string Categoria { get; set; } = "";
    public decimal Limite { get; set; }
    public string? AvisadoMes { get; set; } // «2026-10»
    public int AvisadoNivel { get; set; }    // 0, 80 o 100
}

// Mantenimiento del coche: ITV, revisión, aceite… cada tantos km y/o meses, contado desde la última vez.
public class Mantenimiento
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public int? CadaKm { get; set; }
    public int? CadaMeses { get; set; }
    public DateOnly? UltimaFecha { get; set; }
    public int? UltimoKm { get; set; }
    public string? Notas { get; set; }
    public DateOnly? Avisado { get; set; } // último día en que se avisó de que toca
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
            ["coche-extras"] = ("Papeleo y extras", TipoMovimiento.Gasto, "Coche"),
            ["otros"] = ("Otros gastos", TipoMovimiento.Gasto, "Día a día"),
            ["nomina"] = ("Nómina", TipoMovimiento.Ingreso, "Ingresos"),
            ["extra"] = ("Extra", TipoMovimiento.Ingreso, "Ingresos"),
            ["ventas"] = ("Ventas", TipoMovimiento.Ingreso, "Ingresos"),
            ["otros-ingresos"] = ("Otros ingresos", TipoMovimiento.Ingreso, "Ingresos"),
        };

    public static bool Valida(string id, TipoMovimiento tipo) => Todas.TryGetValue(id, out var c) && c.Tipo == tipo;
}
