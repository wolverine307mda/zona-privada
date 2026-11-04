using System.Globalization;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Telegram;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Mda.Api.Gastos;

public record LineaCategoria(string Nombre, decimal Total, double Porcentaje);
public record LineaGasto(DateOnly Fecha, string Concepto, decimal Importe);
public record LineaPresupuesto(string Nombre, decimal Limite, decimal Gastado, double Porcentaje);
public record InformeMes(int Anio, int Mes, decimal Ingresos, decimal Gastos, decimal IngresosAnt, decimal GastosAnt, int Movimientos,
    IReadOnlyList<LineaCategoria> Categorias, IReadOnlyList<LineaGasto> Mayores, IReadOnlyList<LineaPresupuesto> Presupuestos, decimal? SaldoFin)
{
    public decimal Ahorro => Ingresos - Gastos;
    public string Titulo => new DateTime(Anio, Mes, 1).ToString("MMMM 'de' yyyy", new CultureInfo("es-ES"));
}

// El resumen de un mes en un PDF (QuestPDF, licencia comunitaria: gratis para uso personal): ingresos, gastos, ahorro frente al mes anterior,
// en qué se fue el dinero, los gastos más grandes y cómo fueron los presupuestos. El día 1 se manda solo por Telegram.
public static class Informe
{
    private static readonly CultureInfo Es = new("es-ES");

    public static InformeMes Calcular(int anio, int mes, IReadOnlyList<Movimiento> delMes, IReadOnlyList<Movimiento> anterior, IReadOnlyList<Presupuesto> presupuestos, decimal? saldoFin)
    {
        decimal Suma(IEnumerable<Movimiento> m, TipoMovimiento t) => m.Where(x => x.Tipo == t).Sum(x => x.Importe);
        var gastos = delMes.Where(m => m.Tipo == TipoMovimiento.Gasto).ToList();
        var total = gastos.Sum(m => m.Importe);
        var porCat = gastos.GroupBy(m => m.Categoria).Select(g => (Cat: g.Key, Total: g.Sum(m => m.Importe))).OrderByDescending(x => x.Total).ToList();
        string Nombre(string c) => Categorias.Todas.TryGetValue(c, out var d) ? d.Nombre : c;
        var cats = porCat.Take(8).Select(x => new LineaCategoria(Nombre(x.Cat), x.Total, total > 0 ? Math.Round((double)(x.Total / total) * 100, 1) : 0)).ToList();
        var mayores = gastos.OrderByDescending(m => m.Importe).Take(5).Select(m => new LineaGasto(m.Fecha, m.Concepto, m.Importe)).ToList();
        var gastado = porCat.ToDictionary(x => x.Cat, x => x.Total);
        var pres = presupuestos.Where(p => p.Limite > 0).Select(p => new LineaPresupuesto(Nombre(p.Categoria), p.Limite, gastado.GetValueOrDefault(p.Categoria), Math.Round((double)(gastado.GetValueOrDefault(p.Categoria) / p.Limite) * 100, 1)))
            .OrderByDescending(p => p.Porcentaje).ToList();
        return new InformeMes(anio, mes, Suma(delMes, TipoMovimiento.Ingreso), total, Suma(anterior, TipoMovimiento.Ingreso), Suma(anterior, TipoMovimiento.Gasto), delMes.Count, cats, mayores, pres, saldoFin);
    }

    private static string Eur(decimal v) => v.ToString("#,##0.00 €", Es);
    private static string Variacion(decimal ahora, decimal antes) => antes == 0 ? "—" : $"{(ahora >= antes ? "+" : "")}{Math.Round((ahora / antes - 1) * 100)} % que el mes anterior";

    public static byte[] Pdf(InformeMes i)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        const string Acento = "#ff7a1f", Suave = "#6b7280";
        return Document.Create(doc => doc.Page(p =>
        {
            p.Size(PageSizes.A4); p.Margin(36); p.DefaultTextStyle(t => t.FontSize(10.5f));
            p.Header().Column(c =>
            {
                c.Item().Text("Informe del mes").FontSize(11).FontColor(Suave);
                c.Item().Text(char.ToUpper(i.Titulo[0]) + i.Titulo[1..]).FontSize(24).Bold().FontColor(Acento);
            });
            p.Content().PaddingVertical(14).Column(c =>
            {
                c.Spacing(14);
                c.Item().Row(r =>
                {
                    void Caja(string etq, string valor, string sub) => r.RelativeItem().Border(1).BorderColor("#e5e7eb").Padding(10).Column(k =>
                    { k.Item().Text(etq).FontColor(Suave).FontSize(9); k.Item().Text(valor).Bold().FontSize(16); k.Item().Text(sub).FontColor(Suave).FontSize(8.5f); });
                    Caja("Ingresos", Eur(i.Ingresos), Variacion(i.Ingresos, i.IngresosAnt)); r.ConstantItem(8);
                    Caja("Gastos", Eur(i.Gastos), Variacion(i.Gastos, i.GastosAnt)); r.ConstantItem(8);
                    Caja("Ahorro", Eur(i.Ahorro), i.SaldoFin is { } s ? $"Saldo a fin de mes: {Eur(s)}" : $"{i.Movimientos} movimientos");
                });

                c.Item().Text("En qué se fue el dinero").Bold().FontSize(13);
                if (i.Categorias.Count == 0) c.Item().Text("No hay gastos este mes.").FontColor(Suave);
                foreach (var l in i.Categorias)
                    c.Item().Column(k =>
                    {
                        k.Item().Row(r => { r.RelativeItem().Text(l.Nombre); r.AutoItem().Text($"{Eur(l.Total)}  ·  {l.Porcentaje:0.#} %"); });
                        k.Item().PaddingTop(2).Height(5).Background("#f3f4f6").Row(r => { r.RelativeItem((float)Math.Max(1, l.Porcentaje)).Background(Acento); r.RelativeItem((float)Math.Max(1, 100 - l.Porcentaje)); });
                    });

                if (i.Mayores.Count > 0)
                {
                    c.Item().PaddingTop(4).Text("Los gastos más grandes").Bold().FontSize(13);
                    foreach (var g in i.Mayores) c.Item().Row(r => { r.ConstantItem(60).Text(g.Fecha.ToString("dd/MM")).FontColor(Suave); r.RelativeItem().Text(g.Concepto); r.AutoItem().Text(Eur(g.Importe)); });
                }

                if (i.Presupuestos.Count > 0)
                {
                    c.Item().PaddingTop(4).Text("Presupuestos").Bold().FontSize(13);
                    foreach (var g in i.Presupuestos)
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Text(g.Nombre);
                            r.AutoItem().Text($"{Eur(g.Gastado)} de {Eur(g.Limite)}  ·  {g.Porcentaje:0} %").FontColor(g.Porcentaje >= 100 ? "#dc2626" : g.Porcentaje >= 80 ? "#d97706" : "#16a34a");
                        });
                }
            });
            p.Footer().AlignCenter().Text(t => { t.Span("Zona privada · generado el " + DateTime.Now.ToString("d 'de' MMMM 'de' yyyy", Es) + " · página ").FontSize(8).FontColor(Suave); t.CurrentPageNumber().FontSize(8).FontColor(Suave); });
        })).GeneratePdf();
    }

    public static async Task<InformeMes> De(AppDbContext db, int anio, int mes, CancellationToken ct)
    {
        var desde = new DateOnly(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        var delMes = await db.Movimientos.AsNoTracking().Where(m => m.Fecha >= desde && m.Fecha < hasta).ToListAsync(ct);
        var anterior = await db.Movimientos.AsNoTracking().Where(m => m.Fecha >= desde.AddMonths(-1) && m.Fecha < desde).ToListAsync(ct);
        var pres = await db.Presupuestos.AsNoTracking().ToListAsync(ct);
        decimal? saldo = await Cartera.Base(db, ct) is { } b ? b + await Cartera.NetoHasta(db, hasta, ct) : null;
        return Calcular(anio, mes, delMes, anterior, pres, saldo);
    }

    public static string NombreArchivo(int anio, int mes) => $"informe-{anio:D4}-{mes:D2}.pdf";

    public static void MapInforme(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/gastos/informe", async (int? anio, int? mes, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Cartera.Hoy(reloj, zona);
            var a = anio ?? hoy.Year; var m = mes ?? hoy.Month;
            if (a is < 2000 or > 2100 || m is < 1 or > 12) return Results.ValidationProblem(new Dictionary<string, string[]> { ["mes"] = ["Mes no válido."] });
            return Results.File(Pdf(await De(db, a, m, ct)), "application/pdf", NombreArchivo(a, m));
        }).RequireAuthorization().RequireRateLimiting(Limites.Sensible);
    }
}

// El día 1 de cada mes, a partir de las 9, manda por Telegram el informe del mes que acaba de terminar (una sola vez).
public class InformeMensual(IServiceScopeFactory scopes, Bot bot, Zona zona, TimeProvider reloj, ILogger<InformeMensual> log) : BackgroundService
{
    public const string Clave = "informe-enviado";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), reloj, ct); } catch (OperationCanceledException) { return; }
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(30), reloj);
        do
        {
            try { await Revisar(ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Fallo al mandar el informe mensual"); }
        } while (await t.WaitForNextTickAsync(ct));
    }

    public async Task Revisar(CancellationToken ct)
    {
        var ahora = TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).DateTime;
        if (ahora.Day != 1 || ahora.Hour < 9 || !bot.Vinculado) return;
        var anterior = new DateOnly(ahora.Year, ahora.Month, 1).AddMonths(-1);
        var etiqueta = $"{anterior.Year:D4}-{anterior.Month:D2}";
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if ((await db.Ajustes.FindAsync([Clave], ct))?.Valor == etiqueta) return;
        var datos = await Informe.De(db, anterior.Year, anterior.Month, ct);
        if (datos.Movimientos == 0) { await Marcar(db, etiqueta, ct); return; }
        var leyenda = $"📄 Informe de {datos.Titulo}: ingresos {Cartera.Euros(datos.Ingresos)}, gastos {Cartera.Euros(datos.Gastos)}, ahorro {Cartera.Euros(datos.Ahorro)}.";
        if (await bot.EnviarDocumento(Informe.Pdf(datos), Informe.NombreArchivo(anterior.Year, anterior.Month), leyenda, ct)) await Marcar(db, etiqueta, ct);
    }

    private static async Task Marcar(AppDbContext db, string etiqueta, CancellationToken ct)
    {
        var a = await db.Ajustes.FindAsync([Clave], ct) ?? db.Ajustes.Add(new Ajuste { Clave = Clave }).Entity;
        a.Valor = etiqueta;
        await db.SaveChangesAsync(ct);
    }
}
