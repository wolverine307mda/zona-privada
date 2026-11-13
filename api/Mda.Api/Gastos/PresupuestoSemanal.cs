using System.Globalization;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Gastos;

// Límite de gasto a la semana en una categoría (de lunes a domingo). Funciona igual que el mensual: avisa una vez al pasar del 80 % y otra al pasar del 100 %.
// «Arrastrar»: lo que no gastaste la semana anterior se suma a esta (como mucho la mitad del límite, para que un buen mes no te deje gastar sin freno).
public class PresupuestoSemanal
{
    public string Categoria { get; set; } = "";
    public decimal Limite { get; set; }
    public bool Arrastrar { get; set; }
    public string? AvisadoSemana { get; set; } // «2026-W45»
    public int AvisadoNivel { get; set; }       // 0, 80 o 100
}

public record PresupuestoSemanalEntrada(decimal Limite, bool Arrastrar = false);
public record EstadoSemana(string Categoria, decimal Limite, decimal LimiteEfectivo, bool Arrastrar, decimal Gastado, double Porcentaje, int DiasRestantes, decimal PorDia, DateOnly Reinicio);

public static class Semana
{
    // la semana empieza el lunes
    public static DateOnly Lunes(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    // «2026-W45»: la semana ISO del lunes de esa semana (así una semana que cruza el año tiene una sola clave)
    public static string Clave(DateOnly d) { var l = Lunes(d).ToDateTime(TimeOnly.MinValue); return $"{ISOWeek.GetYear(l):D4}-W{ISOWeek.GetWeekOfYear(l):D2}"; }
    // días que quedan de la semana contando hoy: el lunes 7, el domingo 1
    public static int DiasQuedan(DateOnly hoy) => 7 - (((int)hoy.DayOfWeek + 6) % 7);
}

public static class PresupuestosSemana
{
    public const decimal TopeArrastre = 0.5m;

    public static decimal Arrastre(decimal limite, decimal gastadoAnterior, bool arrastrar) =>
        arrastrar ? Math.Min(Math.Max(0, limite - gastadoAnterior), limite * TopeArrastre) : 0;

    public static EstadoSemana Estado(string categoria, decimal limite, bool arrastrar, decimal gastado, decimal gastadoAnterior, DateOnly hoy)
    {
        var efectivo = limite + Arrastre(limite, gastadoAnterior, arrastrar);
        var quedan = Semana.DiasQuedan(hoy);
        return new EstadoSemana(categoria, limite, efectivo, arrastrar, gastado, efectivo > 0 ? Math.Round((double)(gastado / efectivo) * 100, 1) : 0,
            quedan, Math.Max(0, efectivo - gastado) / quedan, Semana.Lunes(hoy).AddDays(7));
    }

    public static int Nivel(decimal gastado, decimal efectivo) => efectivo <= 0 ? 0 : gastado >= efectivo ? 100 : gastado >= efectivo * 0.8m ? 80 : 0;

    // lo gastado en una categoría en la semana que empieza en `lunes`
    private static async Task<decimal> GastadoSemana(AppDbContext db, string categoria, DateOnly lunes, CancellationToken ct) =>
        await db.Movimientos.Where(m => m.Tipo == TipoMovimiento.Gasto && m.Categoria == categoria && m.Fecha >= lunes && m.Fecha < lunes.AddDays(7)).SumAsync(m => (decimal?)m.Importe, ct) ?? 0;

    public static async Task<EstadoSemana> EstadoDe(AppDbContext db, PresupuestoSemanal p, DateOnly hoy, CancellationToken ct)
    {
        var lunes = Semana.Lunes(hoy);
        var gastado = await GastadoSemana(db, p.Categoria, lunes, ct);
        var anterior = p.Arrastrar ? await GastadoSemana(db, p.Categoria, lunes.AddDays(-7), ct) : 0;
        return Estado(p.Categoria, p.Limite, p.Arrastrar, gastado, anterior, hoy);
    }

    // Avisa al pasar del 80 y del 100 % de la semana (una vez cada nivel por semana: la clave de la semana se guarda en la propia fila)
    public static async Task Comprobar(AppDbContext db, IAvisos avisos, string categoria, DateOnly fecha, DateOnly hoy, ILogger log, CancellationToken ct)
    {
        // un gasto de otra semana (uno viejo que se apunta ahora) no avisa de la semana de hoy
        if (Semana.Lunes(fecha) != Semana.Lunes(hoy)) return;
        var p = await db.PresupuestosSemanales.FindAsync([categoria], ct);
        if (p is null || p.Limite <= 0 || !Categorias.Todas.TryGetValue(categoria, out var cat)) return;
        var e = await EstadoDe(db, p, hoy, ct);
        var nivel = Nivel(e.Gastado, e.LimiteEfectivo);
        var semana = Semana.Clave(hoy);
        var yaAvisado = p.AvisadoSemana == semana ? p.AvisadoNivel : 0;
        if (nivel <= yaAvisado) return;
        p.AvisadoSemana = semana;
        p.AvisadoNivel = nivel;
        await db.SaveChangesAsync(ct);
        var titulo = nivel == 100 ? $"💸 Te has pasado esta semana en {cat.Nombre}" : $"⚠️ {cat.Nombre}: al {Math.Round(e.Porcentaje)} % de la semana";
        var extra = e.LimiteEfectivo > e.Limite ? $" (con {Cartera.Euros(e.LimiteEfectivo - e.Limite)} que sobraron la semana pasada)" : "";
        var resto = nivel == 100 ? $"La semana se reinicia el {e.Reinicio:dd/MM}." : $"Quedan {e.DiasRestantes} {(e.DiasRestantes == 1 ? "día" : "días")}: unos {Cartera.Euros(e.PorDia)} al día.";
        var texto = $"Llevas {Cartera.Euros(e.Gastado)} de {Cartera.Euros(e.Limite)} esta semana{extra}. {resto}";
        // en segundo plano y sin correo: una semana es corta y no hace falta un correo por cada aviso
        _ = Task.Run(async () =>
        {
            try { await avisos.Avisar("cartera", titulo, texto); }
            catch (Exception ex) { log.LogWarning(ex, "No se pudo avisar del presupuesto semanal"); }
        });
    }

    // El barrido del vigilante: gastos que no pasaron por la API (fijos, importaciones) también avisan
    public static async Task ComprobarTodos(AppDbContext db, IAvisos avisos, DateOnly hoy, ILogger log, CancellationToken ct)
    {
        foreach (var c in await db.PresupuestosSemanales.Where(p => p.Limite > 0).Select(p => p.Categoria).ToListAsync(ct))
            await Comprobar(db, avisos, c, hoy, hoy, log, ct);
    }
}
