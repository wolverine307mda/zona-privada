using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// Una medida de la salud de la batería del portátil, una por día (en mWh). Sale de «powercfg /batteryreport»: la capacidad de diseño es lo que cabía
// cuando era nueva y la «completa» lo que cabe ahora; de la diferencia sale el desgaste.
public class SaludBateria
{
    public DateOnly Fecha { get; set; }
    public long Diseno { get; set; }
    public long Completa { get; set; }
    public int? Ciclos { get; set; }
}

public record MedidaBateria(long Diseno, long Completa, int? Ciclos);

public static class BateriaEndpoints
{
    // 0 % = como nueva; nunca negativo (algunas baterías nuevas informan algo más que su capacidad de diseño)
    public static double Desgaste(long diseno, long completa) => diseno > 0 ? Math.Round(Math.Max(0, 100.0 * (1 - (double)completa / diseno)), 1) : 0;

    public static void MapBateria(this IEndpointRouteBuilder app)
    {
        // la última medida y el historial de los últimos 90 días
        app.MapGet("/api/pc/bateria", async (AppDbContext db, CancellationToken ct) =>
        {
            var filas = await db.SaludBaterias.AsNoTracking().OrderByDescending(x => x.Fecha).Take(90).ToListAsync(ct);
            var lista = filas.OrderBy(x => x.Fecha).Select(x => new { fecha = x.Fecha, x.Diseno, x.Completa, x.Ciclos, desgastePct = Desgaste(x.Diseno, x.Completa) }).ToList();
            return Results.Ok(new { actual = lista.LastOrDefault(), historial = lista });
        }).RequireAuthorization();

        // lo manda el agente (token compartido) cuando se pide el informe; una medida por día: la segunda del día sustituye a la primera
        app.MapPost("/api/sistema/agente/bateria", async (HttpRequest req, MedidaBateria m, AppDbContext db, IConfiguration config, TimeProvider reloj, Agenda.Zona zona, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            if (m.Diseno is < 1000 or > 1_000_000 || m.Completa < 1 || m.Completa > m.Diseno * 2)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["completa"] = ["Capacidades de batería que no cuadran."] });
            var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).DateTime);
            var fila = await db.SaludBaterias.FindAsync([hoy], ct);
            if (fila is null) db.SaludBaterias.Add(fila = new SaludBateria { Fecha = hoy });
            (fila.Diseno, fila.Completa, fila.Ciclos) = (m.Diseno, m.Completa, m.Ciclos is > 0 ? m.Ciclos : null);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting(Limites.Agente);
    }
}
