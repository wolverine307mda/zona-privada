using System.Globalization;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Gastos;

public record SaldoEntrada(decimal Actual);
public record HuchaEntrada(string Nombre, decimal Objetivo, string? Color, string? Emoji, DateOnly? Fecha);
public record Aportacion(decimal Importe);
public record PresupuestoEntrada(decimal Limite);

public record EstadoSaldo(bool Configurado, decimal Actual, decimal InicioMes, decimal FinMes, decimal Previsto, decimal EnHuchas, decimal Libre);
public record EstadoHucha(int Id, string Nombre, decimal Objetivo, decimal Ahorrado, string Color, string Emoji, DateOnly? Fecha, double Porcentaje, decimal? AlMes);
public record EstadoPresupuesto(string Categoria, decimal Limite, decimal Gastado, double Porcentaje);
public record Regla(decimal Ingresos, decimal Necesidades, decimal Caprichos, decimal Ahorro);
public record Consejo(string Icono, string Titulo, string Texto, string Tono);
public record Consejos(decimal PorDia, int DiasRestantes, decimal Disponible, decimal Proyeccion, Regla Regla, IReadOnlyList<Consejo> Lista);

// El dinero de verdad: saldo actual (se arrastra de mes en mes y puede ser negativo), huchas, presupuestos
// por categoría y consejos para ahorrar calculados con tus propios apuntes.
public static class Cartera
{
    public const string ClaveSaldo = "saldo-base";
    private static readonly CultureInfo Es = new("es-ES");

    // necesidades y caprichos para la regla 50/30/20
    private static readonly HashSet<string> Necesidades = ["vivienda", "suministros", "supermercado", "salud", "transporte", "gasolina", "seguro", "mecanico", "cuota-coche", "coche-extras"];

    // saldo = base + todo lo ingresado − todo lo gastado (hasta la fecha que se pida)
    public static async Task<decimal?> Base(AppDbContext db, CancellationToken ct = default) =>
        await db.Ajustes.FindAsync([ClaveSaldo], ct) is { } a && decimal.TryParse(a.Valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    public static async Task<decimal> NetoHasta(AppDbContext db, DateOnly antesDe, CancellationToken ct = default)
    {
        var ingresos = await db.Movimientos.Where(m => m.Fecha < antesDe && m.Tipo == TipoMovimiento.Ingreso).SumAsync(m => (decimal?)m.Importe, ct) ?? 0;
        var gastos = await db.Movimientos.Where(m => m.Fecha < antesDe && m.Tipo == TipoMovimiento.Gasto).SumAsync(m => (decimal?)m.Importe, ct) ?? 0;
        return ingresos - gastos;
    }

    public static async Task<decimal> SaldoActual(AppDbContext db, DateOnly hoy, CancellationToken ct = default) =>
        (await Base(db, ct) ?? 0) + await NetoHasta(db, hoy.AddDays(1), ct);

    public static DateOnly Hoy(TimeProvider reloj, Zona zona) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).Date);

    public static string Euros(decimal v) => v.ToString("#,##0.00 €", Es);

    public static void MapCartera(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos").RequireAuthorization();

        // ---- Saldo ----
        g.MapGet("/saldo", async (int? anio, int? mes, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona);
            var desde = new DateOnly(anio ?? hoy.Year, Math.Clamp(mes ?? hoy.Month, 1, 12), 1);
            var hasta = desde.AddMonths(1);
            var b = await Base(db, ct);
            var bv = b ?? 0;
            var actual = bv + await NetoHasta(db, hoy.AddDays(1), ct);
            var fin = bv + await NetoHasta(db, hasta, ct);
            // lo previsto: el final del mes con los fijos que faltan por aplicar (solo en el mes actual o futuros)
            var previsto = fin;
            if (hasta > hoy)
            {
                var registrados = await db.Movimientos.Where(m => m.RecurrenteId != null && m.Fecha >= desde && m.Fecha < hasta).Select(m => m.RecurrenteId!.Value).ToListAsync(ct);
                var rec = await db.Recurrentes.AsNoTracking().ToListAsync(ct);
                previsto += rec.Where(r => r.TocaEn(desde.Year, desde.Month) && !registrados.Contains(r.Id))
                    .Sum(r => r.Tipo == TipoMovimiento.Ingreso ? r.Importe : -r.Importe);
            }
            var huchas = await db.Huchas.SumAsync(h => (decimal?)h.Ahorrado, ct) ?? 0;
            return Results.Ok(new EstadoSaldo(b is not null, actual, bv + await NetoHasta(db, desde, ct), fin, previsto, huchas, actual - huchas));
        });

        // «Ahora mismo tengo X»: se recalcula la base para que el saldo de hoy sea ese (y todo lo nuevo lo mueve)
        g.MapPut("/saldo", async (SaldoEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (Math.Abs(e.Actual) > 100_000_000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["actual"] = ["Ese saldo no parece correcto."] });
            var hoy = Hoy(reloj, zona);
            var b = e.Actual - await NetoHasta(db, hoy.AddDays(1), ct);
            var a = await db.Ajustes.FindAsync([ClaveSaldo], ct);
            if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = ClaveSaldo });
            a.Valor = b.ToString(CultureInfo.InvariantCulture);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { actual = e.Actual });
        });

        // ---- Huchas ----
        g.MapGet("/huchas", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona);
            return Results.Ok((await db.Huchas.AsNoTracking().OrderBy(h => h.Id).ToListAsync(ct)).Select(h => EstadoDe(h, hoy)));
        });
        g.MapPost("/huchas", async (HuchaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (Validar(e) is { } error) return error;
            var h = new Hucha();
            Copiar(e, h);
            db.Huchas.Add(h);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/gastos/huchas/{h.Id}", h);
        });
        g.MapPut("/huchas/{id:int}", async (int id, HuchaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (Validar(e) is { } error) return error;
            var h = await db.Huchas.FindAsync([id], ct);
            if (h is null) return Results.NotFound();
            Copiar(e, h);
            await db.SaveChangesAsync(ct);
            return Results.Ok(h);
        });
        // meter (importe positivo) o sacar (negativo) dinero de la hucha
        g.MapPost("/huchas/{id:int}/aportar", (int id, Aportacion a, AppDbContext db, CancellationToken ct) => Aportar(id, a, db, ct));
        g.MapDelete("/huchas/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var h = await db.Huchas.FindAsync([id], ct);
            if (h is null) return Results.NotFound();
            db.Huchas.Remove(h);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- Presupuestos ----
        g.MapGet("/presupuestos", async (int? anio, int? mes, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona);
            var desde = new DateOnly(anio ?? hoy.Year, Math.Clamp(mes ?? hoy.Month, 1, 12), 1);
            var lista = await db.Presupuestos.AsNoTracking().ToListAsync(ct);
            var gastado = await db.Movimientos.Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha >= desde && m.Fecha < desde.AddMonths(1))
                .GroupBy(m => m.Categoria).Select(x => new { x.Key, Total = x.Sum(m => m.Importe) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
            return Results.Ok(lista.Select(p => new EstadoPresupuesto(p.Categoria, p.Limite, gastado.GetValueOrDefault(p.Categoria),
                p.Limite > 0 ? Math.Round((double)(gastado.GetValueOrDefault(p.Categoria) / p.Limite) * 100, 1) : 0)).OrderByDescending(p => p.Porcentaje));
        });
        g.MapPut("/presupuestos/{categoria}", async (string categoria, PresupuestoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (!Categorias.Valida(categoria, TipoMovimiento.Gasto)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["categoria"] = ["Esa categoría de gasto no existe."] });
            if (e.Limite <= 0 || e.Limite > 1_000_000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["limite"] = ["El límite tiene que ser mayor que 0."] });
            var p = await db.Presupuestos.FindAsync([categoria], ct);
            if (p is null) db.Presupuestos.Add(p = new Presupuesto { Categoria = categoria });
            p.Limite = e.Limite;
            p.AvisadoMes = null;
            p.AvisadoNivel = 0;
            await db.SaveChangesAsync(ct);
            return Results.Ok(p);
        });
        g.MapDelete("/presupuestos/{categoria}", async (string categoria, AppDbContext db, CancellationToken ct) =>
        {
            var p = await db.Presupuestos.FindAsync([categoria], ct);
            if (p is null) return Results.NotFound();
            db.Presupuestos.Remove(p);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- Consejos para ahorrar ----
        g.MapGet("/consejos", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona);
            var desde = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(-3);
            var movs = await db.Movimientos.AsNoTracking().Where(m => m.Fecha >= desde && m.Fecha <= hoy).ToListAsync(ct);
            var rec = await db.Recurrentes.AsNoTracking().ToListAsync(ct);
            var saldo = await SaldoActual(db, hoy, ct);
            var huchas = await db.Huchas.SumAsync(h => (decimal?)h.Ahorrado, ct) ?? 0;
            return Results.Ok(ConsejosDe(hoy, movs, rec, saldo, huchas));
        });
    }

    public static EstadoHucha EstadoDe(Hucha h, DateOnly hoy)
    {
        decimal? alMes = null;
        if (h.Fecha is { } f && f > hoy && h.Ahorrado < h.Objetivo)
        {
            var meses = Math.Max(1, (f.Year - hoy.Year) * 12 + f.Month - hoy.Month);
            alMes = Math.Round((h.Objetivo - h.Ahorrado) / meses, 2);
        }
        return new EstadoHucha(h.Id, h.Nombre, h.Objetivo, h.Ahorrado, h.Color, h.Emoji, h.Fecha,
            h.Objetivo > 0 ? Math.Min(100, Math.Round((double)(h.Ahorrado / h.Objetivo) * 100, 1)) : 0, alMes);
    }

    public static Consejos ConsejosDe(DateOnly hoy, IReadOnlyList<Movimiento> movs, IReadOnlyList<Recurrente> rec, decimal saldo, decimal huchas)
    {
        var primero = new DateOnly(hoy.Year, hoy.Month, 1);
        var diasMes = DateTime.DaysInMonth(hoy.Year, hoy.Month);
        var restantes = diasMes - hoy.Day + 1;
        var delMes = movs.Where(m => m.Fecha >= primero).ToList();
        var registrados = delMes.Where(m => m.RecurrenteId is not null).Select(m => m.RecurrenteId!.Value).ToHashSet();
        var pendientes = rec.Where(r => r.TocaEn(hoy.Year, hoy.Month) && !registrados.Contains(r.Id)).ToList();
        var pendienteNeto = pendientes.Sum(r => r.Tipo == TipoMovimiento.Ingreso ? r.Importe : -r.Importe);

        // lo que queda para gastar cada día sin tocar las huchas, contando con los fijos que faltan
        var disponible = saldo - huchas + pendienteNeto;
        var porDia = Math.Round(disponible / restantes, 2);

        // gasto del mes al ritmo actual: lo variable (no fijo) se extrapola; los fijos pendientes se suman
        var gastoMes = delMes.Where(m => m.Tipo == TipoMovimiento.Gasto).Sum(m => m.Importe);
        var variable = delMes.Where(m => m.Tipo == TipoMovimiento.Gasto && m.RecurrenteId is null && m.DeudaId is null).Sum(m => m.Importe);
        var proyeccion = Math.Round(gastoMes + variable / hoy.Day * (restantes - 1) + pendientes.Where(r => r.Tipo == TipoMovimiento.Gasto).Sum(r => r.Importe), 2);

        // regla 50/30/20 con el mes anterior completo (si no hay, con este)
        var ant = movs.Where(m => m.Fecha >= primero.AddMonths(-1) && m.Fecha < primero).ToList();
        var base5 = ant.Any(m => m.Tipo == TipoMovimiento.Ingreso) ? ant : delMes;
        var ingresos = base5.Where(m => m.Tipo == TipoMovimiento.Ingreso).Sum(m => m.Importe);
        var nec = base5.Where(m => m.Tipo == TipoMovimiento.Gasto && Necesidades.Contains(m.Categoria)).Sum(m => m.Importe);
        var cap = base5.Where(m => m.Tipo == TipoMovimiento.Gasto && !Necesidades.Contains(m.Categoria)).Sum(m => m.Importe);
        var regla = new Regla(ingresos, nec, cap, ingresos - nec - cap);

        var lista = new List<Consejo>();
        if (disponible < 0 && saldo + pendienteNeto >= 0)
            lista.Add(new("alerta", "Las huchas pasan de lo que tienes", $"Tienes {Euros(saldo)} y has apartado {Euros(huchas)} en huchas. Saca algo de una hucha o ajusta el saldo si no cuadra.", "aviso"));
        else if (disponible < 0)
            lista.Add(new("alerta", "Vas en números rojos", $"Contando los fijos que faltan, acabarías el mes con {Euros(disponible)}. Frena lo que no sea imprescindible.", "malo"));
        else
            lista.Add(new("ritmo", $"{Euros(porDia)} al día", restantes == 1 ? "Es lo que te queda hoy, último día del mes, sin tocar las huchas y contando lo que falta por aplicar." : $"Es lo que puedes gastar cada día hasta fin de mes ({restantes} días) sin tocar las huchas, contando lo que falta por aplicar.", "bueno"));

        // la categoría que más se ha disparado frente a la media de los tres meses anteriores
        var previos = movs.Where(m => m.Fecha < primero && m.Tipo == TipoMovimiento.Gasto).ToList();
        if (previos.Count > 0)
        {
            var meses = Math.Max(1, previos.Select(m => (m.Fecha.Year, m.Fecha.Month)).Distinct().Count());
            var subida = delMes.Where(m => m.Tipo == TipoMovimiento.Gasto && m.RecurrenteId is null).GroupBy(m => m.Categoria)
                .Select(x => new { Cat = x.Key, Ahora = x.Sum(m => m.Importe), Media = previos.Where(p => p.Categoria == x.Key).Sum(p => p.Importe) / meses })
                .Where(x => x.Media > 0 && x.Ahora > x.Media * 1.25m && x.Ahora - x.Media > 20)
                .MaxBy(x => x.Ahora - x.Media);
            if (subida is not null)
                lista.Add(new("sube", $"{Categorias.Todas[subida.Cat].Nombre}: +{Math.Round((subida.Ahora / subida.Media - 1) * 100)} %",
                    $"Llevas {Euros(subida.Ahora)} este mes y tu media es {Euros(Math.Round(subida.Media, 2))}. Recortar la diferencia son {Euros(Math.Round((subida.Ahora - subida.Media) * 12, 0))} al año.", "aviso"));
        }

        var subs = rec.Where(r => r.Activo && r.Tipo == TipoMovimiento.Gasto && r.Categoria == "suscripciones").ToList();
        if (subs.Count > 0)
        {
            var anual = subs.Sum(r => r.Mensual) * 12;
            var cara = subs.MaxBy(r => r.Mensual)!;
            lista.Add(new("suscripciones", $"{Euros(anual)} al año en suscripciones",
                $"Son {subs.Count}. La más cara es {cara.Nombre} ({Euros(cara.Mensual * 12)} al año): ¿la sigues usando?", "info"));
        }

        if (regla.Ingresos > 0)
        {
            var pc = (double)(regla.Caprichos / regla.Ingresos) * 100;
            var pa = (double)(regla.Ahorro / regla.Ingresos) * 100;
            if (pa < 20)
                lista.Add(new("hucha", $"Ahorras el {Math.Max(0, Math.Round(pa))} % de lo que entra",
                    pc > 30 ? $"La regla 50/30/20 propone 30 % para caprichos y tú vas por el {Math.Round(pc)} %. Pasar la diferencia a una hucha te acercaría al 20 % de ahorro."
                            : "La regla 50/30/20 propone apartar el 20 %. Prueba a mover a una hucha una cantidad fija el día que cobras.", "aviso"));
            else
                lista.Add(new("trofeo", $"Ahorras el {Math.Round(pa)} % de lo que entra", "Por encima del 20 % que propone la regla 50/30/20. ¡Muy bien!", "bueno"));
        }

        var gastoMedio = previos.Count > 0 ? previos.Sum(m => m.Importe) / Math.Max(1, previos.Select(m => (m.Fecha.Year, m.Fecha.Month)).Distinct().Count()) : 0;
        if (gastoMedio > 0)
        {
            var cubre = Math.Round((saldo) / gastoMedio, 1);
            lista.Add(new("colchon", cubre >= 3 ? $"Colchón de {cubre} meses" : "Construye un colchón",
                cubre >= 3 ? "Con tu dinero cubrirías más de tres meses de gastos: es el colchón de seguridad recomendado."
                           : $"Lo recomendable es tener 3 meses de gastos ({Euros(Math.Round(gastoMedio * 3, 0))}) para imprevistos. Una hucha «Colchón» te ayuda a llegar.", cubre >= 3 ? "bueno" : "info"));
        }

        return new Consejos(porDia, restantes, Math.Round(disponible, 2), proyeccion, regla, lista);
    }

    // Tras apuntar un gasto: si cruza el 80 % o el 100 % del presupuesto de su categoría, avisa (una vez por nivel y mes)
    // meter (importe positivo) o sacar (negativo) dinero de una hucha (lo usan la API y el bot de Telegram)
    internal static async Task<IResult> Aportar(int id, Aportacion a, AppDbContext db, CancellationToken ct)
    {
        var h = await db.Huchas.FindAsync([id], ct);
        if (h is null) return Results.NotFound();
        if (a.Importe == 0 || Math.Abs(a.Importe) > 1_000_000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["importe"] = ["Pon un importe."] });
        if (h.Ahorrado + a.Importe < 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["importe"] = [$"En la hucha solo hay {Euros(h.Ahorrado)}."] });
        h.Ahorrado += a.Importe;
        await db.SaveChangesAsync(ct);
        return Results.Ok(h);
    }

    public static async Task ComprobarPresupuesto(AppDbContext db, IAvisos avisos, string categoria, DateOnly fecha, ILogger log, CancellationToken ct)
    {
        var p = await db.Presupuestos.FindAsync([categoria], ct);
        if (p is null) return;
        var desde = new DateOnly(fecha.Year, fecha.Month, 1);
        var gastado = await db.Movimientos.Where(m => m.Tipo == TipoMovimiento.Gasto && m.Categoria == categoria && m.Fecha >= desde && m.Fecha < desde.AddMonths(1)).SumAsync(m => (decimal?)m.Importe, ct) ?? 0;
        var mes = $"{fecha.Year:D4}-{fecha.Month:D2}";
        var nivel = gastado >= p.Limite ? 100 : gastado >= p.Limite * 0.8m ? 80 : 0;
        var yaAvisado = p.AvisadoMes == mes ? p.AvisadoNivel : 0;
        if (nivel <= yaAvisado) return;
        p.AvisadoMes = mes;
        p.AvisadoNivel = nivel;
        await db.SaveChangesAsync(ct);
        var nombre = Categorias.Todas[categoria].Nombre;
        var titulo = nivel == 100 ? $"💸 Te has pasado en {nombre}" : $"⚠️ {nombre}: al {Math.Round(gastado / p.Limite * 100)} %";
        var texto = $"Llevas {Euros(gastado)} de {Euros(p.Limite)} este mes.";
        // el aviso sale en segundo plano: apuntar el gasto no espera al correo
        _ = Task.Run(async () =>
        {
            try { await avisos.PorPush(titulo, texto, "/gastos"); await avisos.PorTelegram($"{titulo}\n{texto}"); await avisos.PorCorreo(titulo, texto, "/gastos"); }
            catch (Exception e) { log.LogWarning(e, "No se pudo avisar del presupuesto"); }
        });
    }

    private static IResult? Validar(HuchaEntrada e)
    {
        var errores = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Length > 80) errores["nombre"] = ["Ponle un nombre (máximo 80 caracteres)."];
        if (e.Objetivo <= 0 || e.Objetivo > 100_000_000) errores["objetivo"] = ["El objetivo tiene que ser mayor que 0."];
        if (e.Emoji?.Length > 16) errores["emoji"] = ["Un solo emoji."];
        return errores.Count > 0 ? Results.ValidationProblem(errores) : null;
    }

    private static void Copiar(HuchaEntrada e, Hucha h)
    {
        h.Nombre = e.Nombre.Trim();
        h.Objetivo = e.Objetivo;
        h.Color = e.Color ?? "";
        h.Emoji = string.IsNullOrWhiteSpace(e.Emoji) ? "🐷" : e.Emoji.Trim();
        h.Fecha = e.Fecha;
    }
}
