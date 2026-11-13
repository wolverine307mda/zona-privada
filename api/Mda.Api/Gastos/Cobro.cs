using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Gastos;

// Tu día de cobro: el día del mes en que suele entrar la nómina. Sirve para decir cuánto puedes gastar al día HASTA COBRAR (no hasta fin de mes),
// avisar si ese día llega y no hay ingreso, y avisar al llegar la nómina para repartirla.
public record CobroDetectado(int Dia, int Meses, bool Fiable, decimal Tipico);
public record CobroEntrada(int? Dia);
// Fuente: «manual» (lo pusiste tú), «fijo» (una nómina en tus fijos) o «detectado» (de tus ingresos). Retrasado: ya tocaba y no ha entrado.
public record EstadoCobro(int? Dia, string? Fuente, bool Fiable, decimal Tipico, DateOnly? Proximo, int? DiasHasta, decimal? Libre, decimal? PorDia, bool Retrasado, int MesesVistos);

public static class DiaDeCobro
{
    public const string ClaveDia = "cobro-dia", ClaveFalta = "cobro-avisado", ClaveNomina = "nomina-avisada";
    public const int Meses = 6, MinimoMeses = 3, Margen = 3;
    // un ingreso cuenta como «la nómina» si llega al 60 % de lo habitual (así un bizum de 40 € no la confunde)
    public const decimal Umbral = 0.6m;

    private static decimal Mediana(List<decimal> v) { v.Sort(); var n = v.Count; return n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2; }

    // Mira el ingreso más grande de cada uno de los últimos 6 meses, descarta los meses sin nómina (el más grande sería pequeño) y saca el día habitual (la mediana).
    // Es fiable con 3 meses o más y si al menos el 60 % cae a ±3 días de ese día (los fines de semana la mueven un poco).
    public static CobroDetectado? Detectar(IEnumerable<Movimiento> movs, DateOnly hoy)
    {
        var desde = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(-Meses);
        var porMes = movs.Where(m => m.Tipo == TipoMovimiento.Ingreso && m.Fecha >= desde && m.Fecha <= hoy && m.Importe > 0)
            .GroupBy(m => (m.Fecha.Year, m.Fecha.Month)).Select(g => g.OrderByDescending(m => m.Importe).First()).ToList();
        if (porMes.Count == 0) return null;
        var tipico = Mediana(porMes.Select(m => m.Importe).ToList());
        var buenos = porMes.Where(m => m.Importe >= tipico * Umbral).ToList();
        if (buenos.Count < MinimoMeses) return buenos.Count == 0 ? null : new CobroDetectado((int)Mediana(buenos.Select(m => (decimal)m.Fecha.Day).ToList()), buenos.Count, false, Mediana(buenos.Select(m => m.Importe).ToList()));
        var dia = (int)Math.Round(Mediana(buenos.Select(m => (decimal)m.Fecha.Day).ToList()), MidpointRounding.AwayFromZero);
        var cerca = buenos.Count(m => Math.Abs(m.Fecha.Day - dia) <= Margen);
        return new CobroDetectado(dia, buenos.Count, (double)cerca / buenos.Count >= 0.6, Mediana(buenos.Select(m => m.Importe).ToList()));
    }

    // la nómina que ya tengas entre tus fijos (la mayor de las mensuales de ingresos)
    public static (int Dia, decimal Importe)? DeFijo(IEnumerable<Recurrente> fijos) =>
        fijos.Where(r => r.Activo && r.Tipo == TipoMovimiento.Ingreso && r.Frecuencia == Frecuencia.Mensual && r.Importe > 0).OrderByDescending(r => r.Importe)
            .Select(r => ((int Dia, decimal Importe)?)(r.Dia, r.Importe)).FirstOrDefault();

    // el día en que de verdad se cobra ese mes: un 31 en febrero es el 28 y, si cae en fin de semana, se paga el viernes anterior
    public static DateOnly Pagado(int anio, int mes, int dia)
    {
        var d = new DateOnly(anio, mes, Math.Clamp(dia, 1, DateTime.DaysInMonth(anio, mes)));
        return d.DayOfWeek switch { DayOfWeek.Saturday => d.AddDays(-1), DayOfWeek.Sunday => d.AddDays(-2), _ => d };
    }

    // ¿ha entrado este mes una nómina? Con una nómina habitual conocida, un ingreso de al menos el 60 % de ella; sin ella, cualquier ingreso
    public static bool HaCobrado(IEnumerable<Movimiento> movs, DateOnly hoy, decimal tipico) =>
        movs.Any(m => m.Tipo == TipoMovimiento.Ingreso && m.Fecha.Year == hoy.Year && m.Fecha.Month == hoy.Month && m.Fecha <= hoy && m.Importe >= tipico * Umbral);

    // el próximo cobro: el de este mes si aún no ha pasado (o si hoy es el día y no ha entrado); si ya cobraste, el del mes que viene.
    // Si ya tocaba y no ha entrado, sigue siendo «hoy» (hay que verlo, no esconderlo hasta el mes siguiente).
    public static (DateOnly Fecha, bool Retrasado) Proximo(DateOnly hoy, int dia, bool yaCobrado)
    {
        var este = Pagado(hoy.Year, hoy.Month, dia);
        if (este > hoy) return (este, false);
        if (!yaCobrado) return (hoy, este < hoy);
        var s = hoy.AddMonths(1);
        return (Pagado(s.Year, s.Month, dia), false);
    }

    public static EstadoCobro Estado(DateOnly hoy, int? manual, (int Dia, decimal Importe)? fijo, CobroDetectado? detectado, IReadOnlyList<Movimiento> movs, decimal libre)
    {
        int? dia = null; string? fuente = null; var fiable = false;
        if (manual is >= 1 and <= 31) { dia = manual; fuente = "manual"; fiable = true; }
        else if (fijo is { } f) { dia = f.Dia; fuente = "fijo"; fiable = true; }
        else if (detectado is { Fiable: true } d) { dia = d.Dia; fuente = "detectado"; fiable = true; }
        var tipico = fijo?.Importe ?? detectado?.Tipico ?? 0;
        if (dia is null) return new EstadoCobro(null, null, false, tipico, null, null, libre, null, false, detectado?.Meses ?? 0);
        var (fecha, retrasado) = Proximo(hoy, dia.Value, HaCobrado(movs, hoy, tipico));
        var dias = fecha.DayNumber - hoy.DayNumber;
        return new EstadoCobro(dia, fuente, fiable, tipico, fecha, dias, libre, libre > 0 ? Math.Round(libre / Math.Max(1, dias), 2, MidpointRounding.AwayFromZero) : 0, retrasado, detectado?.Meses ?? 0);
    }

    // ---- con la base de datos ----
    private static async Task<int?> Manual(AppDbContext db, CancellationToken ct) =>
        (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveDia, ct))?.Valor is { } v && int.TryParse(v, out var n) ? n : null;

    public static async Task<EstadoCobro> Cargar(AppDbContext db, DateOnly hoy, CancellationToken ct)
    {
        var desde = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(-Meses);
        var movs = await db.Movimientos.AsNoTracking().Where(m => m.Tipo == TipoMovimiento.Ingreso && m.Fecha >= desde && m.Fecha <= hoy).ToListAsync(ct);
        var fijos = await db.Recurrentes.AsNoTracking().Where(r => r.Tipo == TipoMovimiento.Ingreso).ToListAsync(ct);
        var libre = await Cartera.SaldoActual(db, hoy, ct) - (await db.Huchas.SumAsync(h => (decimal?)h.Ahorrado, ct) ?? 0);
        return Estado(hoy, await Manual(db, ct), DeFijo(fijos), Detectar(movs, hoy), movs, libre);
    }

    private static async Task<bool> YaAvisado(AppDbContext db, string clave, string mes, CancellationToken ct) =>
        (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == clave, ct))?.Valor == mes;

    private static async Task Anotar(AppDbContext db, string clave, string mes, CancellationToken ct)
    {
        var a = await db.Ajustes.FirstOrDefaultAsync(x => x.Clave == clave, ct);
        if (a is null) db.Ajustes.Add(new Ajuste { Clave = clave, Valor = mes }); else a.Valor = mes;
        await db.SaveChangesAsync(ct);
    }

    private static void Avisar(IAvisos avisos, ILogger log, string titulo, string texto) => _ = Task.Run(async () =>
    {
        try { await avisos.Avisar("cartera", titulo, texto); }
        catch (Exception e) { log.LogWarning(e, "No se pudo avisar del cobro"); }
    });

    // Por la tarde (a partir de las 20:00): si el día de cobro ya llegó y este mes no ha entrado la nómina, un aviso (una sola vez al mes)
    public static async Task Comprobar(AppDbContext db, IAvisos avisos, DateTime local, ILogger log, CancellationToken ct)
    {
        if (local.Hour < 20) return;
        var hoy = DateOnly.FromDateTime(local);
        var e = await Cargar(db, hoy, ct);
        if (e.Dia is not { } dia || e.Proximo != hoy) return;           // «hoy» es el próximo cobro: toca y no ha entrado (o ya tocaba)
        if (Pagado(hoy.Year, hoy.Month, dia) > hoy) return;
        var mes = $"{hoy.Year:D4}-{hoy.Month:D2}";
        if (await YaAvisado(db, ClaveFalta, mes, ct)) return;
        await Anotar(db, ClaveFalta, mes, ct);
        var pagado = Pagado(hoy.Year, hoy.Month, dia);
        Avisar(avisos, log, "💶 No ha entrado la nómina", pagado == hoy ? "Hoy es tu día de cobro y todavía no hay ningún ingreso apuntado." : $"Tu día de cobro era el {pagado:dd/MM} y este mes no hay ningún ingreso apuntado.");
    }

    // Al apuntar un ingreso grande (parecido a tu nómina): un aviso para repartirla, una sola vez al mes
    public static async Task AlRecibirIngreso(AppDbContext db, IAvisos avisos, Movimiento m, ILogger log, CancellationToken ct)
    {
        if (m.Tipo != TipoMovimiento.Ingreso || m.Importe <= 0) return;
        var desde = new DateOnly(m.Fecha.Year, m.Fecha.Month, 1).AddMonths(-Meses);
        var movs = await db.Movimientos.AsNoTracking().Where(x => x.Tipo == TipoMovimiento.Ingreso && x.Fecha >= desde && x.Fecha <= m.Fecha && x.Id != m.Id).ToListAsync(ct);
        var tipico = DeFijo(await db.Recurrentes.AsNoTracking().Where(r => r.Tipo == TipoMovimiento.Ingreso).ToListAsync(ct))?.Importe ?? Detectar(movs, m.Fecha)?.Tipico ?? 0;
        if (tipico <= 0 || m.Importe < tipico * Umbral) return;
        var mes = $"{m.Fecha.Year:D4}-{m.Fecha.Month:D2}";
        if (await YaAvisado(db, ClaveNomina, mes, ct)) return;
        await Anotar(db, ClaveNomina, mes, ct);
        Avisar(avisos, log, "💶 Nómina recibida", $"Han entrado {Cartera.Euros(m.Importe)}. Toca repartirla: aparta lo de tus huchas y revisa los fijos del mes.");
    }

    public static void MapCobro(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos/cobro").RequireAuthorization();
        g.MapGet("/", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) => Results.Ok(await Cargar(db, Cartera.Hoy(reloj, zona), ct)));
        // dia = null: vuelve al automático
        g.MapPut("/", async (CobroEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (e.Dia is { } d && (d < 1 || d > 31)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["dia"] = ["El día tiene que estar entre 1 y 31."] });
            var a = await db.Ajustes.FirstOrDefaultAsync(x => x.Clave == ClaveDia, ct);
            if (e.Dia is null) { if (a is not null) db.Ajustes.Remove(a); }
            else if (a is null) db.Ajustes.Add(new Ajuste { Clave = ClaveDia, Valor = e.Dia.Value.ToString() });
            else a.Valor = e.Dia.Value.ToString();
            await db.SaveChangesAsync(ct);
            return Results.Ok(await Cargar(db, Cartera.Hoy(reloj, zona), ct));
        });
    }
}
