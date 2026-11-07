using System.Globalization;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Papeles;

// Garantías y devoluciones de lo que compras: hasta cuándo puedes reclamar o devolver y un aviso antes de que se pase el plazo.
// `Estado` es lo que decides tú (vigente · devuelta · reclamada); «caducada» y «por caducar» se calculan con la fecha.
public class Garantia
{
    public int Id { get; set; }
    public int? MovimientoId { get; set; }
    public string Producto { get; set; } = "";
    public string Comercio { get; set; } = "";
    public DateOnly FechaCompra { get; set; }
    public DateOnly GarantiaHasta { get; set; }
    public DateOnly? DevolucionHasta { get; set; }
    public string Estado { get; set; } = "vigente";
    public string Notas { get; set; } = "";
    // hasta qué aviso de la garantía se ha dado (1 = quedan 60 días, 2 = 30, 3 = 7) y si ya se avisó del fin de la devolución
    public int AvisadoGarantia { get; set; }
    public bool AvisadoDevolucion { get; set; }
}

public record GarantiaEntrada(string? Producto, string? Comercio, string? FechaCompra, string? GarantiaHasta, string? DevolucionHasta, bool? SinDevolucion, string? Estado, string? Notas, int? MovimientoId);
public record GarantiaDto(int Id, int? MovimientoId, string Producto, string Comercio, string FechaCompra, string GarantiaHasta, string? DevolucionHasta, string Estado, string Notas,
    int DiasGarantia, int? DiasDevolucion, string Situacion);

public static class Garantias
{
    public const int Maximo = 300, AniosPorDefecto = 3, DiasDevolucionPorDefecto = 14;
    public static readonly string[] Estados = ["vigente", "devuelta", "reclamada"];
    // días antes del fin de la garantía en que se avisa (de más lejos a más cerca) y días antes del fin de la devolución
    public static readonly int[] Escalones = [60, 30, 7];
    public const int AvisoDevolucion = 3;

    // la regla por defecto: 3 años de garantía legal para bienes nuevos (se puede cambiar a mano en cada compra)
    public static DateOnly GarantiaPorDefecto(DateOnly compra) => compra.AddYears(AniosPorDefecto);
    public static DateOnly DevolucionPorDefecto(DateOnly compra) => compra.AddDays(DiasDevolucionPorDefecto);

    public static int Dias(DateOnly fin, DateOnly hoy) => fin.DayNumber - hoy.DayNumber;

    // vigente · por-caducar (menos de 60 días) · caducada · devuelta · reclamada
    public static string Situacion(Garantia g, DateOnly hoy)
    {
        if (g.Estado is "devuelta" or "reclamada") return g.Estado;
        var d = Dias(g.GarantiaHasta, hoy);
        return d < 0 ? "caducada" : d <= Escalones[0] ? "por-caducar" : "vigente";
    }

    // 0 = sin aviso · 1, 2, 3 = el escalón de 60, 30 o 7 días que toca ya
    public static int Escalon(int dias)
    {
        if (dias < 0) return 0;
        for (var i = Escalones.Length - 1; i >= 0; i--) if (dias <= Escalones[i]) return i + 1;
        return 0;
    }

    public static GarantiaDto Dto(Garantia g, DateOnly hoy) => new(g.Id, g.MovimientoId, g.Producto, g.Comercio, Iso(g.FechaCompra), Iso(g.GarantiaHasta),
        g.DevolucionHasta is { } d ? Iso(d) : null, g.Estado, g.Notas, Dias(g.GarantiaHasta, hoy), g.DevolucionHasta is { } dv ? Dias(dv, hoy) : null, Situacion(g, hoy));

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static bool Fecha(string? s, out DateOnly f) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out f);

    public static Dictionary<string, string[]> Validar(GarantiaEntrada e, bool nueva)
    {
        var mal = new Dictionary<string, string[]>();
        if (nueva || e.Producto is not null) { if (string.IsNullOrWhiteSpace(e.Producto) || e.Producto.Trim().Length > 80) mal["producto"] = ["Ponle un nombre al producto (máximo 80 letras)."]; }
        if (e.Comercio is { Length: > 80 }) mal["comercio"] = ["El comercio llega a 80 letras."];
        if ((nueva || e.FechaCompra is not null) && !Fecha(e.FechaCompra, out _)) mal["fechaCompra"] = ["Pon la fecha de la compra."];
        if (e.GarantiaHasta is not null && !Fecha(e.GarantiaHasta, out _)) mal["garantiaHasta"] = ["Fecha de fin de garantía no válida."];
        if (e.DevolucionHasta is not null && !Fecha(e.DevolucionHasta, out _)) mal["devolucionHasta"] = ["Fecha de fin de devolución no válida."];
        if (e.Estado is not null && !Estados.Contains(e.Estado)) mal["estado"] = ["Estado no válido."];
        if (e.Notas is { Length: > 500 }) mal["notas"] = ["Las notas llegan a 500 letras."];
        return mal;
    }

    // Una vez al día en el vigilante: avisa en cada escalón de la garantía (una sola vez) y a 3 días de que acabe la devolución
    public static async Task Comprobar(AppDbContext db, IAvisos avisos, DateOnly hoy, ILogger log, CancellationToken ct)
    {
        var limite = hoy.AddDays(Escalones[0] + 1);
        var candidatas = await db.Garantias.Where(g => g.Estado == "vigente" && (g.GarantiaHasta <= limite || (g.DevolucionHasta != null && g.DevolucionHasta <= hoy.AddDays(AvisoDevolucion)))).ToListAsync(ct);
        var textos = new List<string>();
        foreach (var g in candidatas)
        {
            var dias = Dias(g.GarantiaHasta, hoy);
            var esc = Escalon(dias);
            if (esc > g.AvisadoGarantia) { textos.Add($"La garantía de «{g.Producto}» acaba {(dias == 0 ? "HOY" : $"en {dias} día(s) ({g.GarantiaHasta:dd/MM/yyyy})")}. Si falla, reclama ya."); g.AvisadoGarantia = esc; }
            if (g.DevolucionHasta is { } dv && !g.AvisadoDevolucion)
            {
                var dd = Dias(dv, hoy);
                if (dd is >= 0 && dd <= AvisoDevolucion) { textos.Add($"Te queda{(dd == 1 ? "" : "n")} {dd} día(s) para devolver «{g.Producto}»{(g.Comercio.Length > 0 ? $" a {g.Comercio}" : "")} ({dv:dd/MM/yyyy})."); g.AvisadoDevolucion = true; }
                else if (dd < 0) g.AvisadoDevolucion = true;       // pasó sin avisar (compra antigua): no avisar de algo que ya no se puede hacer
            }
        }
        if (textos.Count == 0) { if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct); return; }
        await db.SaveChangesAsync(ct);
        foreach (var t in textos)
        {
            try { await avisos.PorPush("🧾 Garantía o devolución", t, "/papeles", ct); await avisos.PorTelegram($"🧾 {t}", ct); }
            catch (Exception e) { log.LogWarning(e, "No se pudo avisar de una garantía"); }
        }
    }

    public static async Task<List<GarantiaDto>> Lista(AppDbContext db, DateOnly hoy, CancellationToken ct)
    {
        var l = await db.Garantias.AsNoTracking().ToListAsync(ct);
        // primero las que se acaban antes, y al final las cerradas
        return l.OrderBy(g => g.Estado == "vigente" ? 0 : 1).ThenBy(g => g.GarantiaHasta).ThenBy(g => g.Id).Select(g => Dto(g, hoy)).ToList();
    }
}

public static class GarantiasEndpoints
{
    public static void MapGarantias(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/garantias").RequireAuthorization();
        static DateOnly Hoy(TimeProvider r, Zona z) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.GetUtcNow(), z.Info).Date);
        static DateOnly F(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        g.MapGet("/", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
            Results.Ok(new { reglaAnios = Garantias.AniosPorDefecto, reglaDevolucionDias = Garantias.DiasDevolucionPorDefecto, garantias = await Garantias.Lista(db, Hoy(reloj, zona), ct) }));

        g.MapPost("/", async (GarantiaEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (Garantias.Validar(e, true) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.Garantias.CountAsync(ct) >= Garantias.Maximo)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["producto"] = [$"Como mucho {Garantias.Maximo} garantías: borra alguna."] });
            var compra = F(e.FechaCompra!);
            var n = new Garantia
            {
                Producto = e.Producto!.Trim(), Comercio = (e.Comercio ?? "").Trim(), FechaCompra = compra, MovimientoId = e.MovimientoId, Notas = (e.Notas ?? "").Trim(),
                GarantiaHasta = e.GarantiaHasta is not null ? F(e.GarantiaHasta) : Garantias.GarantiaPorDefecto(compra),
                // sin devolución si lo pides; si no, la que indiques o los 14 días por defecto
                DevolucionHasta = e.SinDevolucion == true ? null : e.DevolucionHasta is not null ? F(e.DevolucionHasta) : Garantias.DevolucionPorDefecto(compra),
            };
            db.Garantias.Add(n);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/garantias/{n.Id}", Garantias.Dto(n, Hoy(reloj, zona)));
        });

        g.MapPut("/{id:int}", async (int id, GarantiaEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (Garantias.Validar(e, false) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var n = await db.Garantias.FindAsync([id], ct);
            if (n is null) return Results.NotFound();
            if (e.Producto is not null) n.Producto = e.Producto.Trim();
            if (e.Comercio is not null) n.Comercio = e.Comercio.Trim();
            if (e.FechaCompra is not null) n.FechaCompra = F(e.FechaCompra);
            if (e.GarantiaHasta is not null) { n.GarantiaHasta = F(e.GarantiaHasta); n.AvisadoGarantia = 0; }
            if (e.SinDevolucion == true) { n.DevolucionHasta = null; n.AvisadoDevolucion = false; }
            else if (e.DevolucionHasta is not null) { n.DevolucionHasta = F(e.DevolucionHasta); n.AvisadoDevolucion = false; }
            if (e.Estado is not null) n.Estado = e.Estado;
            if (e.Notas is not null) n.Notas = e.Notas.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(Garantias.Dto(n, Hoy(reloj, zona)));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var n = await db.Garantias.FindAsync([id], ct);
            if (n is null) return Results.NotFound();
            db.Garantias.Remove(n);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
