using Mda.Api.Agenda;
using Mda.Api.Asistente;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Mda.Api.Habitos;
using Mda.Api.Luz;
using Mda.Api.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Atajos;

public record PresupuestoCerca(string Categoria, string Nombre, int Porcentaje, decimal Gastado, decimal Limite);
public record HabitoPendiente(int Id, string Nombre);
public record HabitosHoy(int Total, int Hechos, IReadOnlyList<HabitoPendiente> Pendientes);
public record ResumenDeHoy(string Fecha, decimal GastoHoy, string GastoHoyTexto, IReadOnlyList<PresupuestoCerca> Presupuestos, string Agenda, HabitosHoy Habitos, string? Salida, IReadOnlyList<string> Lineas, CompactoHoy? Compacto = null);
// Lo mínimo para los widgets de la pantalla de bloqueo del iPhone (accesorios de iOS 16+): una línea, un anillo y un par de datos. Los campos antiguos no cambian.
public record CompactoHoy(string? SaldoTexto, string? Proximo, int? PresupuestoPct, int TareasHoy, string? Cumple, bool? EntrenoHoy);

// «Hoy» en una sola respuesta: lo que querrías mirar al coger el móvil. Lo usa la pantalla /hoy de la app (con sesión) y el widget del iPhone (token de Atajos con
// permiso de lectura). Lleva también `lineas`, el mismo resumen ya escrito, para que Atajos o Scriptable lo enseñen sin más.
public static class Hoy
{
    public const int PorcentajeCerca = 80;

    public static async Task<ResumenDeHoy> Componer(AppDbContext db, ServicioLuz luz, ServicioTiempo tiempo, Zona zona, TimeProvider reloj, CancellationToken ct)
    {
        var hoy = Cartera.Hoy(reloj, zona);
        var ahora = reloj.GetUtcNow();
        var desdeMes = new DateOnly(hoy.Year, hoy.Month, 1);

        // gasto de hoy y presupuestos que van por el 80 % o más
        var delMes = await db.Movimientos.AsNoTracking().Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha >= desdeMes && m.Fecha < desdeMes.AddMonths(1)).Select(m => new { m.Categoria, m.Fecha, m.Importe }).ToListAsync(ct);
        var gastoHoy = delMes.Where(m => m.Fecha == hoy).Sum(m => m.Importe);
        var porCategoria = delMes.GroupBy(m => m.Categoria).ToDictionary(g => g.Key, g => g.Sum(m => m.Importe));
        var presupuestos = (await db.Presupuestos.AsNoTracking().Where(p => p.Limite > 0).ToListAsync(ct))
            .Where(p => Categorias.Todas.ContainsKey(p.Categoria))
            .Select(p => new PresupuestoCerca(p.Categoria, Categorias.Todas[p.Categoria].Nombre, (int)Math.Round(porCategoria.GetValueOrDefault(p.Categoria) / p.Limite * 100, MidpointRounding.AwayFromZero), porCategoria.GetValueOrDefault(p.Categoria), p.Limite))
            .Where(p => p.Porcentaje >= PorcentajeCerca).OrderByDescending(p => p.Porcentaje).ToList();

        var agenda = await AsistenteEndpoints.Agenda(db, zona, hoy, hoy, ct);

        // hábitos que tocan hoy
        var lista = (await ServicioHabitos.Resumen(db, hoy, false, ct)).Where(h => h.TocaHoy).ToList();
        var pendientes = lista.Where(h => !h.Hecho).Select(h => new HabitoPendiente(h.Id, h.Nombre)).ToList();
        var habitos = new HabitosHoy(lista.Count, lista.Count - pendientes.Count, pendientes);

        // la mejor salida de la semana (si falla la previsión no se cae todo el resumen)
        string? salida = null;
        try
        {
            var mejores = PlanSalidas.Mejores(await PlanSalidas.Calcular(db, luz, tiempo, zona, hoy, 7, ahora, ct), 1);
            if (mejores.Count > 0)
            {
                var s = mejores[0];
                var inicio = TimeZoneInfo.ConvertTime(s.Ventana.Inicio, zona.Info);
                salida = $"{inicio:ddd d} {inicio:HH:mm} · {s.Punto} · {s.Ventana.Titulo.ToLowerInvariant()} {s.Nota}/10";
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException) { /* sin previsión */ }

        return new ResumenDeHoy(hoy.ToString("yyyy-MM-dd"), gastoHoy, Cartera.Euros(gastoHoy), presupuestos, agenda, habitos, salida, Lineas(gastoHoy, presupuestos, agenda, habitos, salida), await Compactar(db, zona, hoy, ahora, ct));
    }

    // Los datos cortos para los widgets pequeños; si algo falla, ese dato queda en null y el resto sigue
    public static async Task<CompactoHoy> Compactar(AppDbContext db, Zona zona, DateOnly hoy, DateTimeOffset ahora, CancellationToken ct)
    {
        string? saldo = null, proximo = null, cumple = null; int? pct = null; bool? entreno = null;
        try
        {
            if (await Cartera.Base(db, ct) is not null) saldo = TextoSaldo(await Cartera.SaldoActual(db, hoy, ct) - (await db.Huchas.SumAsync(h => (decimal?)h.Ahorrado, ct) ?? 0));
            var desdeMes = new DateOnly(hoy.Year, hoy.Month, 1);
            var gastado = await db.Movimientos.AsNoTracking().Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha >= desdeMes && m.Fecha <= hoy).GroupBy(m => m.Categoria).Select(g => new { g.Key, Total = g.Sum(m => m.Importe) }).ToDictionaryAsync(g => g.Key, g => g.Total, ct);
            var ps = (await db.Presupuestos.AsNoTracking().Where(p => p.Limite > 0).ToListAsync(ct)).Select(p => (int)Math.Round(gastado.GetValueOrDefault(p.Categoria) / p.Limite * 100, MidpointRounding.AwayFromZero)).ToList();
            if (ps.Count > 0) pct = ps.Max();
        }
        catch (Exception e) when (e is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException) { /* ese dato se queda sin poner */ }
        try
        {
            var ini = hoy.ToDateTime(TimeOnly.MinValue);
            var desde = new DateTimeOffset(ini, zona.Info.GetUtcOffset(ini));
            var siguiente = (await AgendaEndpoints.Apariciones(db, desde, desde.AddDays(2), zona, ct)).Where(a => !a.TodoElDia && a.Inicio >= ahora && !(a.Tipo == TipoEvento.Recordatorio && a.Hecho)).OrderBy(a => a.Inicio).FirstOrDefault();
            if (siguiente is not null) proximo = TextoProximo(TimeZoneInfo.ConvertTime(siguiente.Inicio, zona.Info), siguiente.Titulo, hoy);
        }
        catch (Exception e) when (e is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException) { /* sin agenda */ }
        var tareas = await db.Tareas.AsNoTracking().CountAsync(t => !t.Hecha && t.Vence != null && t.Vence <= hoy, ct);
        var cumples = (await db.Personas.AsNoTracking().Where(p => p.CumpleDia != null && p.CumpleMes != null).ToListAsync(ct)).Where(p => Mda.Api.Personas.Cumples.Dias(p.CumpleDia!.Value, p.CumpleMes!.Value, hoy) == 0).Select(p => p.Apodo.Length > 0 ? p.Apodo : p.Nombre).ToList();
        if (cumples.Count > 0) cumple = "Cumple hoy: " + string.Join(", ", cumples.Take(2));
        if (await Mda.Api.Bienestar.BienestarServicio.PlanActivo(db, ct) is { } pa && Mda.Api.Bienestar.BienestarServicio.SesionDe(pa.Plan, pa.Fila.Inicio, hoy) is not null) entreno = await db.Entrenos.AnyAsync(e => e.Fecha == hoy, ct);
        return new CompactoHoy(saldo, proximo, pct, tareas, cumple, entreno);
    }

    // «Disponible 523 €» (sin céntimos si son pocos dígitos no importa: el widget es pequeño)
    public static string TextoSaldo(decimal libre) => $"Disponible {Math.Round(libre, 0, MidpointRounding.AwayFromZero):N0} €".Replace(',', '.');

    // «12:00 Cita» si es hoy y «mañana 09:30 Cita» si no
    public static string TextoProximo(DateTimeOffset inicioLocal, string titulo, DateOnly hoy)
    {
        var t = titulo.Length > 28 ? titulo[..27] + "…" : titulo;
        return DateOnly.FromDateTime(inicioLocal.DateTime) == hoy ? $"{inicioLocal:HH:mm} {t}" : $"mañana {inicioLocal:HH:mm} {t}";
    }

    // El mismo resumen en frases cortas (una por línea)
    public static List<string> Lineas(decimal gastoHoy, IReadOnlyList<PresupuestoCerca> presupuestos, string agenda, HabitosHoy habitos, string? salida)
    {
        var l = new List<string> { gastoHoy > 0 ? $"Gastado hoy: {Cartera.Euros(gastoHoy)}" : "Hoy no has gastado nada" };
        l.AddRange(presupuestos.Take(3).Select(p => p.Porcentaje >= 100 ? $"{p.Nombre}: te has pasado ({p.Porcentaje} %)" : $"{p.Nombre} al {p.Porcentaje} % del presupuesto"));
        l.Add(UnaLinea(agenda));
        if (habitos.Total > 0)
            l.Add(habitos.Pendientes.Count == 0 ? $"Hábitos: todo hecho ({habitos.Total})" : $"Hábitos: {habitos.Hechos} de {habitos.Total} · faltan {string.Join(", ", habitos.Pendientes.Take(4).Select(h => h.Nombre))}{(habitos.Pendientes.Count > 4 ? "…" : "")}");
        if (salida is not null) l.Add($"Mejor salida de la semana: {salida}");
        return l;
    }

    // «Hoy tienes:\n⏰ 10:00 Llamar\n📅 12:00 Cita» → «Hoy tienes: ⏰ 10:00 Llamar · 📅 12:00 Cita»
    public static string UnaLinea(string texto)
    {
        var partes = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length > 1 && partes[0].EndsWith(':')) return partes[0] + " " + string.Join(" · ", partes[1..]);
        return string.Join(" · ", partes);
    }

    public static void MapHoy(this IEndpointRouteBuilder app)
    {
        // para la pantalla /hoy de la app (con sesión)
        app.MapGet("/api/hoy", async (AppDbContext db, ServicioLuz luz, ServicioTiempo tiempo, Zona zona, TimeProvider reloj, CancellationToken ct) =>
            Results.Ok(await Componer(db, luz, tiempo, zona, reloj, ct))).RequireAuthorization();
    }
}
