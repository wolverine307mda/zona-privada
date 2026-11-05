using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Mda.Api.Pc;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

public record AreaVida(string Id, string Nombre, string Estado, string Motivo);
public record Semaforo(string Estado, IReadOnlyList<AreaVida> Areas);
public record DatosVida(
    bool CopiaVigilada, double? CopiaHoras, bool PanicoActivo, int FallosAcceso, bool DosPasos,
    decimal? SaldoLibre, int PresupuestosPasados, bool AgenteConectado, int AlertasPc, bool? FunnelResponde, int? DiasCertificado, int DispositivosCaidos);

// Un vistazo: cada área de tu vida digital en verde, ámbar o rojo con el motivo. El estado global es el peor de todos.
public static class SemaforoVida
{
    public const string Verde = "verde", Ambar = "ambar", Rojo = "rojo";
    private static int Peso(string e) => e == Rojo ? 2 : e == Ambar ? 1 : 0;

    public static Semaforo Evaluar(DatosVida d)
    {
        var areas = new List<AreaVida>
        {
            Copias(d), Seguridad(d), Dinero(d), Pc(d), Red(d),
        };
        return new Semaforo(areas.MaxBy(a => Peso(a.Estado))!.Estado, areas);
    }

    private static AreaVida Copias(DatosVida d) =>
        !d.CopiaVigilada ? new("copias", "Copias", Ambar, "Las copias no se vigilan: falta la carpeta de copias.")
        : d.CopiaHoras is null ? new("copias", "Copias", Rojo, "Todavía no hay ninguna copia de seguridad.")
        : d.CopiaHoras > 72 ? new("copias", "Copias", Rojo, $"La última copia tiene {(int)(d.CopiaHoras / 24)} días.")
        : d.CopiaHoras > 30 ? new("copias", "Copias", Ambar, $"La última copia tiene {(int)d.CopiaHoras} horas.")
        : new("copias", "Copias", Verde, "Copia de las últimas 24 horas.");

    private static AreaVida Seguridad(DatosVida d) =>
        d.PanicoActivo ? new("seguridad", "Seguridad", Rojo, "El botón de pánico está activo: el acceso está bloqueado.")
        : d.FallosAcceso >= 10 ? new("seguridad", "Seguridad", Ambar, $"{d.FallosAcceso} intentos de acceso fallidos este mes.")
        : !d.DosPasos ? new("seguridad", "Seguridad", Ambar, "No tienes verificación en dos pasos activada.")
        : new("seguridad", "Seguridad", Verde, "Sin incidencias.");

    private static AreaVida Dinero(DatosVida d) =>
        d.SaldoLibre is < 0 ? new("dinero", "Dinero", Rojo, "Tu dinero libre (sin huchas) está en negativo.")
        : d.PresupuestosPasados > 0 ? new("dinero", "Dinero", Ambar, $"{d.PresupuestosPasados} presupuesto(s) pasado(s) este mes.")
        : new("dinero", "Dinero", Verde, "Presupuestos y saldo en orden.");

    private static AreaVida Pc(DatosVida d) =>
        d.AlertasPc > 0 ? new("pc", "Mi PC", Ambar, $"{d.AlertasPc} aviso(s) de salud del equipo.")
        : !d.AgenteConectado ? new("pc", "Mi PC", Ambar, "El agente del PC no está conectado.")
        : new("pc", "Mi PC", Verde, "Equipo conectado y sano.");

    private static AreaVida Red(DatosVida d) =>
        d.FunnelResponde == false ? new("red", "Red", Rojo, "La app no responde desde Internet.")
        : d.DiasCertificado is <= 10 ? new("red", "Red", Ambar, $"El certificado HTTPS caduca en {Math.Max(0, d.DiasCertificado.Value)} día(s).")
        : d.DispositivosCaidos > 0 ? new("red", "Red", Ambar, $"{d.DispositivosCaidos} aparato(s) de la red caído(s).")
        : new("red", "Red", Verde, "Todo responde.");

    public static void MapInicioResumen(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inicio/resumen", async (AppDbContext db, Vigilante vigilante, EstadoAgente agente, EstadoFunnel funnel, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var ahora = reloj.GetUtcNow();
            var copia = vigilante.UltimaCopia();
            var panico = await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Panico.Clave, ct);
            var panicoActivo = panico?.Valor is { } v && DateTimeOffset.TryParse(v, out var hasta) && hasta > ahora;
            var hoy = Cartera.Hoy(reloj, zona);
            var desdeMes = new DateOnly(hoy.Year, hoy.Month, 1);
            var gastado = await db.Movimientos.AsNoTracking().Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha >= desdeMes && m.Fecha <= hoy)
                .GroupBy(m => m.Categoria).Select(g => new { g.Key, Total = g.Sum(m => m.Importe) }).ToDictionaryAsync(g => g.Key, g => g.Total, ct);
            var pasados = (await db.Presupuestos.AsNoTracking().ToListAsync(ct)).Count(p => p.Limite > 0 && gastado.GetValueOrDefault(p.Categoria) >= p.Limite);
            decimal? libre = await Cartera.Base(db, ct) is null ? null : await Cartera.SaldoActual(db, hoy, ct) - (await db.Huchas.SumAsync(h => (decimal?)h.Ahorrado, ct) ?? 0);
            var alertas = 0;
            if ((await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == SaludPcClave, ct))?.Valor is { Length: > 0 } json)
                try { alertas = JsonSerializer.Deserialize<Dictionary<string, string>>(json)?.Count ?? 0; } catch (JsonException) { }
            var caidos = await db.DispositivosRed.AsNoTracking().CountAsync(d => d.Avisar && d.EnLinea == false, ct);
            var desde = ahora.AddDays(-30);
            var dosPasos = await db.Users.AnyAsync(u => u.TwoFactorEnabled, ct);
            return Results.Ok(Evaluar(new DatosVida(copia.Vigilada, copia.Fecha is { } f ? (ahora - f).TotalHours : null, panicoActivo,
                await db.Accesos.CountAsync(a => !a.Exito && !a.Revisado && a.Fecha > desde, ct), dosPasos, libre, pasados, agente.Conectado(TimeSpan.FromMinutes(2)), alertas,
                funnel.Ultimo?.Responde, funnel.Ultimo?.DiasCertificado, caidos)));
        }).RequireAuthorization();
    }

    private const string SaludPcClave = "salud-avisadas";
}
