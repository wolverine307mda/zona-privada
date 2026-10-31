using System.Globalization;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Agenda;

// Tu horario de trabajo (por ejemplo, de lunes a viernes de 8:30 a 17:30). No son eventos: se pinta en el
// calendario como fondo y sirve para avisarte cuando una cita cae dentro. Días: 1 = lunes … 7 = domingo.
public record Horario(bool Activo, string Nombre, int[] Dias, string Entrada, string Salida)
{
    public const string Clave = "horario";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] NombresDias = ["", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo"];

    public TimeOnly HoraEntrada => TimeOnly.ParseExact(Entrada, "HH:mm", CultureInfo.InvariantCulture);
    public TimeOnly HoraSalida => TimeOnly.ParseExact(Salida, "HH:mm", CultureInfo.InvariantCulture);

    public static int DiaDe(DateTime d) => d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;
    public bool Trabaja(DateOnly d) => Activo && Dias.Contains(DiaDe(d.ToDateTime(TimeOnly.MinValue)));

    // ¿se pisa [inicio, fin) con el horario de ese día? (hora local)
    public bool Pisa(DateTime inicio, DateTime fin) =>
        Trabaja(DateOnly.FromDateTime(inicio)) && inicio.TimeOfDay < HoraSalida.ToTimeSpan() && fin.TimeOfDay > HoraEntrada.ToTimeSpan()
        && DateOnly.FromDateTime(inicio) == DateOnly.FromDateTime(fin.AddTicks(-1));

    // «de lunes a viernes, de 8:30 a 17:30»
    public string Texto()
    {
        var d = Dias.Order().ToArray();
        string dias = d.Length == 7 ? "todos los días"
            : d.SequenceEqual([1, 2, 3, 4, 5]) ? "de lunes a viernes"
            : d.Length > 2 && d[^1] - d[0] == d.Length - 1 ? $"de {NombresDias[d[0]]} a {NombresDias[d[^1]]}"
            : "los " + string.Join(", ", d[..^1].Select(x => NombresDias[x])) + (d.Length > 1 ? " y " : "") + NombresDias[d[^1]];
        return $"{dias}, de {Corta(HoraEntrada)} a {Corta(HoraSalida)}";
    }

    public static string Corta(TimeOnly t) => t.Minute == 0 ? $"{t.Hour}:00" : $"{t.Hour}:{t.Minute:D2}";

    public Dictionary<string, string[]> Errores()
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Nombre) || Nombre.Length > 40) e["nombre"] = ["Ponle un nombre (máximo 40 caracteres)."];
        if (Dias is null || Dias.Length == 0 || Dias.Any(x => x is < 1 or > 7) || Dias.Distinct().Count() != Dias.Length) e["dias"] = ["Elige al menos un día."];
        var ok = TimeOnly.TryParseExact(Entrada, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)
               & TimeOnly.TryParseExact(Salida, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var b);
        if (!ok) e["entrada"] = ["Las horas van como 08:30."];
        else if (b <= a) e["salida"] = ["La salida tiene que ser después de la entrada."];
        return e;
    }

    public static async Task<Horario?> Leer(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor;
        try { return v is null ? null : JsonSerializer.Deserialize<Horario>(v, Json); } catch (JsonException) { return null; }
    }

    public static async Task Guardar(AppDbContext db, Horario h, CancellationToken ct)
    {
        var a = await db.Ajustes.FindAsync([Clave], ct) ?? db.Ajustes.Add(new Ajuste { Clave = Clave }).Entity;
        a.Valor = JsonSerializer.Serialize(h with { Nombre = h.Nombre.Trim(), Dias = h.Dias.Order().ToArray() }, Json);
        await db.SaveChangesAsync(ct);
    }

    // Guardar desde la API o desde el bot, con la misma validación
    internal static async Task<IResult> Poner(AppDbContext db, Horario h, CancellationToken ct)
    {
        if (h.Errores() is { Count: > 0 } e) return Results.ValidationProblem(e);
        await Guardar(db, h, ct);
        return Results.Ok(h with { Nombre = h.Nombre.Trim(), Dias = h.Dias.Order().ToArray() });
    }
}
