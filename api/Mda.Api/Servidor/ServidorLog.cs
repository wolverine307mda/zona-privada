using Mda.Api.Data;
using Mda.Api.Pc;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Servidor;

public record SucesoServidor(DateTimeOffset Hora, string Tipo, string Titulo, string Detalle, string Gravedad);

// Lo que pasa en el servidor, apuntado y consultable: aprobaciones de entrada, la guardia, los runbooks, despertares del PC… y, además,
// los accesos y las órdenes al PC (que ya viven en sus tablas) en una sola línea de tiempo filtrable.
public static class ServidorLog
{
    public const int MaxGuardados = 5000;
    public static readonly string[] Tipos = ["acceso", "aprobacion", "guardia", "runbook", "pc", "alerta", "sistema"];

    public static async Task Anotar(AppDbContext db, TimeProvider reloj, string tipo, string titulo, string detalle = "", string gravedad = "info", CancellationToken ct = default)
    {
        db.EventosServidor.Add(new EventoServidor
        {
            Hora = reloj.GetUtcNow(), Tipo = tipo, Gravedad = gravedad,
            Titulo = titulo.Length > 160 ? titulo[..160] : titulo, Detalle = detalle.Length > 400 ? detalle[..400] : detalle,
        });
        await db.SaveChangesAsync(ct);
        // la tabla no crece sin fin: pasados los 5000 se borran los más viejos (de vez en cuando, no en cada apunte)
        if (Random.Shared.Next(50) == 0 && await db.EventosServidor.CountAsync(ct) > MaxGuardados)
        {
            var corte = await db.EventosServidor.OrderByDescending(e => e.Hora).Skip(MaxGuardados).Select(e => e.Hora).FirstOrDefaultAsync(ct);
            if (corte != default) { db.EventosServidor.RemoveRange(await db.EventosServidor.Where(e => e.Hora <= corte).ToListAsync(ct)); await db.SaveChangesAsync(ct); }
        }
    }

    // la línea: lo apuntado + accesos + órdenes al PC, del más nuevo al más viejo, con filtro por tipos (vacío = todos) y por texto
    public static List<SucesoServidor> Componer(IEnumerable<EventoServidor> eventos, IEnumerable<Acceso> accesos, IEnumerable<OrdenPc> ordenes,
        IReadOnlyCollection<string> tipos, string? texto, int maximo)
    {
        var l = new List<SucesoServidor>();
        l.AddRange(eventos.Select(e => new SucesoServidor(e.Hora, e.Tipo, e.Titulo, e.Detalle, e.Gravedad)));
        l.AddRange(accesos.Select(a => new SucesoServidor(a.Fecha, "acceso", a.Exito ? "Entrada a la app" : "Intento de acceso fallido",
            $"{a.Ip}{(a.Motivo.Length > 0 ? " · " + a.Motivo : "")}", a.Exito ? "info" : "aviso")));
        l.AddRange(ordenes.Select(o => new SucesoServidor(o.Creada, "pc", $"Orden al PC: {OrdenesPc.Texto(o)}", $"{o.Desde} · {o.Estado}", o.Estado == "error" ? "aviso" : "info")));
        IEnumerable<SucesoServidor> r = l;
        if (tipos.Count > 0) r = r.Where(x => tipos.Contains(x.Tipo));
        if (!string.IsNullOrWhiteSpace(texto)) r = r.Where(x => x.Titulo.Contains(texto, StringComparison.OrdinalIgnoreCase) || x.Detalle.Contains(texto, StringComparison.OrdinalIgnoreCase));
        return r.OrderByDescending(x => x.Hora).Take(Math.Clamp(maximo, 1, 500)).ToList();
    }

    public static void MapServidorLinea(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/servidor/linea", async (DateTimeOffset? desde, DateTimeOffset? hasta, string? tipos, string? q, int? max, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            var fin = (hasta ?? reloj.GetUtcNow()).ToUniversalTime();
            var ini = (desde ?? fin.AddDays(-7)).ToUniversalTime();
            if (ini >= fin || fin - ini > TimeSpan.FromDays(120)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["desde"] = ["El rango tiene que ser de 1 a 120 días."] });
            var lista = (tipos ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Tipos.Contains).ToList();
            var eventos = await db.EventosServidor.AsNoTracking().Where(e => e.Hora >= ini && e.Hora <= fin).OrderByDescending(e => e.Hora).Take(1000).ToListAsync(ct);
            var accesos = await db.Accesos.AsNoTracking().Where(a => a.Fecha >= ini && a.Fecha <= fin).OrderByDescending(a => a.Fecha).Take(1000).ToListAsync(ct);
            var ordenes = await db.OrdenesPc.AsNoTracking().Where(o => o.Creada >= ini && o.Creada <= fin).OrderByDescending(o => o.Creada).Take(1000).ToListAsync(ct);
            return Results.Ok(new { desde = ini, hasta = fin, sucesos = Componer(eventos, accesos, ordenes, lista, q, max ?? 200) });
        }).RequireAuthorization();
    }
}
