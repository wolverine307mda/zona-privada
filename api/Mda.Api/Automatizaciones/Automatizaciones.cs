using System.Globalization;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Automatizaciones;

// «Si el disco pasa del 90 %, avísame» · «si llevo más de 800 € gastados este mes, apunta una tarea».
// Cada 5 minutos se mira el dato de cada regla activa; si se cumple y ha pasado la espera desde la última vez, hace la acción (avisar o crear una tarea).
public class Automatizacion
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activa { get; set; } = true;
    public string Disparador { get; set; } = "disco";
    public double Umbral { get; set; }                // se cumple cuando el dato llega a este valor (o baja de él, según el disparador)
    public string Accion { get; set; } = "avisar";    // avisar o tarea
    public string Texto { get; set; } = "";           // vacío = un texto automático
    public int EsperaHoras { get; set; } = 24;        // mínimo entre una vez y la siguiente
    public DateTimeOffset? Ultima { get; set; }
    public double? UltimoValor { get; set; }
}

public record DefDisparador(string Clave, string Etiqueta, string Unidad, bool Mayor, string Ayuda);

public record AutomatizacionEntrada(string? Nombre, bool? Activa, string? Disparador, double? Umbral, string? Accion, string? Texto, int? EsperaHoras);
public record AutomatizacionDto(int Id, string Nombre, bool Activa, string Disparador, string Etiqueta, string Unidad, bool Mayor, double Umbral, string Accion, string Texto, int EsperaHoras, DateTimeOffset? Ultima, double? UltimoValor, double? Ahora, bool Cumple);

public static class Disparadores
{
    // «Mayor» = se cumple cuando el dato es mayor o igual que el umbral; si no, cuando es menor o igual
    public static readonly DefDisparador[] Todos =
    [
        new DefDisparador("disco", "Disco más lleno", "%", true, "Porcentaje usado del disco que más tiene"),
        new DefDisparador("cpu", "Procesador (media de los últimos 2 min)", "%", true, "Uso medio de la CPU"),
        new DefDisparador("ram", "Memoria (media de los últimos 2 min)", "%", true, "Uso medio de la memoria"),
        new DefDisparador("gasto-mes", "Gasto de este mes", "€", true, "Suma de los gastos del mes en curso"),
        new DefDisparador("tareas-vencidas", "Tareas vencidas", "tareas", true, "Cuántas tareas pendientes ya pasaron de fecha"),
        new DefDisparador("papeles", "Papeles que caducan en 30 días", "papeles", true, "Cuántos papeles caducan pronto o ya caducaron"),
    ];

    public static DefDisparador? Buscar(string? clave) => Todos.FirstOrDefault(d => d.Clave == clave);

    public static bool Cumple(DefDisparador d, double umbral, double valor) => d.Mayor ? valor >= umbral : valor <= umbral;

    // El valor de hoy del dato; null si no se puede saber (p. ej. aún no hay lecturas del PC)
    public static async Task<double?> Valor(string clave, AppDbContext db, EstadoSistema estado, DateOnly hoy, CancellationToken ct)
    {
        switch (clave)
        {
            case "disco":
                var h = estado.Historial();
                if (h.Length == 0) return null;
                var ult = h[^1];
                var discos = ult.Discos is { Count: > 0 } ? ult.Discos.Where(x => x.Total > 0).Select(x => 100.0 * x.Usado / x.Total).ToList() : new List<double>();
                if (discos.Count > 0) return discos.Max();
                return ult.DiscoTotal > 0 ? 100.0 * ult.DiscoUsado / ult.DiscoTotal : null;
            case "cpu":
                var c = estado.Historial();
                return c.Length == 0 ? null : c.Average(m => m.Cpu);
            case "ram":
                var r = estado.Historial().Where(m => m.MemoriaTotal > 0).ToList();
                return r.Count == 0 ? null : r.Average(m => 100.0 * m.MemoriaUsada / m.MemoriaTotal);
            case "gasto-mes":
                var inicio = new DateOnly(hoy.Year, hoy.Month, 1);
                var gastos = await db.Movimientos.AsNoTracking().Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha >= inicio && m.Fecha <= hoy).Select(m => m.Importe).ToListAsync(ct);
                return (double)gastos.Sum();
            case "tareas-vencidas":
                return await db.Tareas.CountAsync(t => !t.Hecha && t.Vence != null && t.Vence < hoy, ct);
            case "papeles":
                var limite = hoy.AddDays(30);
                return await db.Papeles.CountAsync(p => p.Vence <= limite, ct);
            default: return null;
        }
    }
}

public static class ServicioAutomatizaciones
{
    public const int Maximo = 20;
    private static readonly CultureInfo Es = new("es-ES");

    public static string Numero(double v, string unidad) => unidad == "€" ? v.ToString("0.##", Es) + " €" : unidad == "%" ? v.ToString("0.#", Es) + " %" : v.ToString("0.#", Es);

    public static Dictionary<string, string[]> Validar(AutomatizacionEntrada e, bool nueva)
    {
        var mal = new Dictionary<string, string[]>();
        if (nueva || e.Nombre is not null) { if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 60) mal["nombre"] = ["Ponle un nombre (máximo 60 letras)."]; }
        if ((nueva || e.Disparador is not null) && Disparadores.Buscar(e.Disparador) is null) mal["disparador"] = ["Elige qué vigilar."];
        if (nueva && e.Umbral is null) mal["umbral"] = ["Pon el valor a partir del cual saltar."];
        if (e.Umbral is { } u && (double.IsNaN(u) || double.IsInfinity(u) || u < 0 || u > 1_000_000)) mal["umbral"] = ["El valor va de 0 a 1.000.000."];
        if (e.Accion is not null && e.Accion is not ("avisar" or "tarea")) mal["accion"] = ["La acción es avisar o crear una tarea."];
        if (e.Texto is { Length: > 200 }) mal["texto"] = ["Como mucho 200 letras."];
        if (e.EsperaHoras is { } h && h is < 1 or > 720) mal["esperaHoras"] = ["La espera va de 1 hora a 30 días."];
        return mal;
    }

    public static async Task<AutomatizacionDto> Dto(Automatizacion a, AppDbContext db, EstadoSistema estado, DateOnly hoy, CancellationToken ct)
    {
        var d = Disparadores.Buscar(a.Disparador) ?? Disparadores.Todos[0];
        var ahora = await Disparadores.Valor(a.Disparador, db, estado, hoy, ct);
        return new AutomatizacionDto(a.Id, a.Nombre, a.Activa, a.Disparador, d.Etiqueta, d.Unidad, d.Mayor, a.Umbral, a.Accion, a.Texto, a.EsperaHoras, a.Ultima, a.UltimoValor, ahora,
            ahora is { } v && Disparadores.Cumple(d, a.Umbral, v));
    }

    public static string TextoDe(Automatizacion a, double valor)
    {
        var d = Disparadores.Buscar(a.Disparador) ?? Disparadores.Todos[0];
        return string.IsNullOrWhiteSpace(a.Texto) ? $"{a.Nombre}: {d.Etiqueta.ToLowerInvariant()} está en {Numero(valor, d.Unidad)}." : $"{a.Texto.Trim()} ({Numero(valor, d.Unidad)})";
    }
}

// Mira las reglas cada 5 minutos
public sealed class MotorAutomatizaciones(IServiceScopeFactory ambitos, IAvisos avisos, EstadoSistema estado, TimeProvider reloj, Zona zona, RegistroVigilancia registro, ILogger<MotorAutomatizaciones> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), reloj, ct);
            using var t = new PeriodicTimer(TimeSpan.FromMinutes(5), reloj);
            do
            {
                try { using var scope = ambitos.CreateScope(); await Vuelta(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct); registro.Anotar("automatizaciones"); }
                catch (Exception e) when (e is not OperationCanceledException) { registro.Anotar("automatizaciones", e); log.LogWarning(e, "No se pudieron evaluar las automatizaciones"); }
            } while (await t.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { /* se apaga la API */ }
    }

    // Una pasada por todas las reglas activas (pública para probarla sin esperar)
    public async Task Vuelta(AppDbContext db, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, zona.Info).Date);
        var reglas = await db.Automatizaciones.Where(r => r.Activa).ToListAsync(ct);
        foreach (var r in reglas)
        {
            var d = Disparadores.Buscar(r.Disparador);
            if (d is null || await Disparadores.Valor(d.Clave, db, estado, hoy, ct) is not { } v) continue;
            if (!Disparadores.Cumple(d, r.Umbral, v)) continue;
            if (r.Ultima is { } u && ahora - u < TimeSpan.FromHours(Math.Max(1, r.EsperaHoras))) continue;
            r.Ultima = ahora; r.UltimoValor = v;
            await Actuar(db, r, v, hoy, prueba: false, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    // Hace lo que dice la regla. Con `prueba` lo marca como tal (el botón «Probar» de la pantalla).
    public async Task Actuar(AppDbContext db, Automatizacion r, double valor, DateOnly hoy, bool prueba, CancellationToken ct)
    {
        var texto = ServicioAutomatizaciones.TextoDe(r, valor);
        if (r.Accion == "tarea")
        {
            // si ya hay una tarea pendiente igual no se apunta otra
            var titulo = (string.IsNullOrWhiteSpace(r.Texto) ? r.Nombre : r.Texto.Trim());
            if (titulo.Length > 120) titulo = titulo[..120];
            if (!await db.Tareas.AnyAsync(t => !t.Hecha && t.Titulo == titulo, ct))
                db.Tareas.Add(new Mda.Api.Tareas.Tarea { Titulo = titulo, Notas = texto.Length > 1000 ? texto[..1000] : texto, Prioridad = 2, Vence = hoy, Creada = reloj.GetUtcNow() });
        }
        else
        {
            var cab = prueba ? "🤖 (prueba) " : "🤖 ";
            await avisos.Avisar("automatizaciones", cab + r.Nombre, texto, textoTelegram: cab + texto, ct: ct);
        }
    }
}

public static class AutomatizacionesEndpoints
{
    public static void MapAutomatizaciones(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/automatizaciones").RequireAuthorization();
        DateOnly Hoy(TimeProvider reloj, Zona zona) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).Date);

        g.MapGet("/", async (AppDbContext db, EstadoSistema estado, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona);
            var l = await db.Automatizaciones.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
            var dtos = new List<AutomatizacionDto>();
            foreach (var a in l) dtos.Add(await ServicioAutomatizaciones.Dto(a, db, estado, hoy, ct));
            return Results.Ok(new { disparadores = Disparadores.Todos, reglas = dtos });
        });

        g.MapPost("/", async (AutomatizacionEntrada e, AppDbContext db, EstadoSistema estado, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (ServicioAutomatizaciones.Validar(e, true) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.Automatizaciones.CountAsync(ct) >= ServicioAutomatizaciones.Maximo)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Como mucho {ServicioAutomatizaciones.Maximo} automatizaciones: borra alguna."] });
            var a = new Automatizacion
            {
                Nombre = e.Nombre!.Trim(), Activa = e.Activa ?? true, Disparador = e.Disparador!, Umbral = e.Umbral!.Value,
                Accion = e.Accion ?? "avisar", Texto = (e.Texto ?? "").Trim(), EsperaHoras = e.EsperaHoras ?? 24,
            };
            db.Automatizaciones.Add(a);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/automatizaciones/{a.Id}", await ServicioAutomatizaciones.Dto(a, db, estado, Hoy(reloj, zona), ct));
        });

        g.MapPut("/{id:int}", async (int id, AutomatizacionEntrada e, AppDbContext db, EstadoSistema estado, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (ServicioAutomatizaciones.Validar(e, false) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var a = await db.Automatizaciones.FindAsync([id], ct);
            if (a is null) return Results.NotFound();
            if (e.Nombre is not null) a.Nombre = e.Nombre.Trim();
            if (e.Activa is { } act) a.Activa = act;
            if (e.Disparador is not null) { a.Disparador = e.Disparador; a.Ultima = null; }
            if (e.Umbral is { } u) { a.Umbral = u; a.Ultima = null; }
            if (e.Accion is not null) a.Accion = e.Accion;
            if (e.Texto is not null) a.Texto = e.Texto.Trim();
            if (e.EsperaHoras is { } h) a.EsperaHoras = h;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await ServicioAutomatizaciones.Dto(a, db, estado, Hoy(reloj, zona), ct));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var a = await db.Automatizaciones.FindAsync([id], ct);
            if (a is null) return Results.NotFound();
            db.Automatizaciones.Remove(a);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // «Probar»: hace la acción ya, sin mirar si se cumple (no cambia la espera)
        g.MapPost("/{id:int}/probar", async (int id, AppDbContext db, EstadoSistema estado, MotorAutomatizaciones motor, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var a = await db.Automatizaciones.FindAsync([id], ct);
            if (a is null) return Results.NotFound();
            var hoy = Hoy(reloj, zona);
            var v = await Disparadores.Valor(a.Disparador, db, estado, hoy, ct) ?? 0;
            await motor.Actuar(db, a, v, hoy, prueba: true, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { texto = ServicioAutomatizaciones.TextoDe(a, v) });
        });
    }
}
