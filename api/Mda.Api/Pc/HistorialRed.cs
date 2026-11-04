using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// Una lectura de un aparato con historial (el router, por ejemplo): una por minuto.
public class LecturaRed
{
    public long Id { get; set; }
    public int DispositivoId { get; set; }
    public DateTimeOffset Hora { get; set; }
    public bool EnLinea { get; set; }
    public int? Ms { get; set; }
}

// Lo que queda de las lecturas viejas: una fila por hora (cuántas lecturas, cuántas sin respuesta, latencia media y máxima).
public class LecturaHora
{
    public int DispositivoId { get; set; }
    public DateTimeOffset Hora { get; set; }
    public int Muestras { get; set; }
    public int Caidas { get; set; }
    public int? MediaMs { get; set; }
    public int? MaxMs { get; set; }
}

// Historial de latencia y caídas: las lecturas de cada minuto se guardan 48 horas y después se resumen en una fila por hora durante 30 días,
// así una gráfica del último mes cabe en 720 puntos y la base no crece sin parar. La latencia se mide abriendo una conexión TCP (dentro del
// contenedor no hay permiso para ICMP/ping), que para ver si la red va lenta o se corta es igual de útil.
public static class HistorialRed
{
    public const int MaxPorDispositivo = 3;
    public static readonly TimeSpan Crudas = TimeSpan.FromHours(48), Total = TimeSpan.FromDays(30);

    public static DateTimeOffset Hora(DateTimeOffset t) { var u = t.ToUniversalTime(); return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, 0, 0, TimeSpan.Zero); }

    public static LecturaHora Agrupar(int dispositivo, DateTimeOffset hora, IReadOnlyCollection<LecturaRed> lecturas)
    {
        var ms = lecturas.Where(l => l.EnLinea && l.Ms is not null).Select(l => l.Ms!.Value).ToList();
        return new LecturaHora
        {
            DispositivoId = dispositivo, Hora = hora, Muestras = lecturas.Count, Caidas = lecturas.Count(l => !l.EnLinea),
            MediaMs = ms.Count > 0 ? (int)Math.Round(ms.Average()) : null, MaxMs = ms.Count > 0 ? ms.Max() : null,
        };
    }

    // resume en horas completas lo que tiene más de 48 h y borra lo de más de 30 días
    public static async Task Compactar(AppDbContext db, DateTimeOffset ahora, CancellationToken ct = default)
    {
        var corte = Hora(ahora - Crudas);
        var viejas = await db.LecturasRed.Where(l => l.Hora < corte).ToListAsync(ct);
        foreach (var g in viejas.GroupBy(l => (l.DispositivoId, Hora: Hora(l.Hora))))
            if (await db.LecturasHora.FindAsync([g.Key.DispositivoId, g.Key.Hora], ct) is null) db.LecturasHora.Add(Agrupar(g.Key.DispositivoId, g.Key.Hora, g.ToList()));
        db.LecturasRed.RemoveRange(viejas);
        db.LecturasHora.RemoveRange(await db.LecturasHora.Where(h => h.Hora < ahora - Total).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    public record Punto(DateTimeOffset Hora, int? Ms, bool EnLinea, int Caidas, int Muestras);

    // los puntos de una gráfica: como mucho unos 240 de las lecturas de cada minuto (se agrupan por tandas) y, para rangos largos, las horas resumidas
    public static async Task<(List<Punto> Puntos, object Resumen)> Leer(AppDbContext db, int dispositivo, TimeSpan rango, DateTimeOffset ahora, CancellationToken ct = default)
    {
        var desde = ahora - rango;
        var crudas = await db.LecturasRed.AsNoTracking().Where(l => l.DispositivoId == dispositivo && l.Hora >= desde).OrderBy(l => l.Hora).ToListAsync(ct);
        var horas = rango > Crudas ? await db.LecturasHora.AsNoTracking().Where(h => h.DispositivoId == dispositivo && h.Hora >= desde).OrderBy(h => h.Hora).ToListAsync(ct) : [];
        var puntos = horas.Select(h => new Punto(h.Hora, h.MediaMs, h.Caidas == 0, h.Caidas, h.Muestras)).ToList();
        var tanda = Math.Max(1, (int)Math.Ceiling(crudas.Count / 240.0));
        foreach (var t in crudas.Chunk(tanda))
        {
            var ms = t.Where(l => l.EnLinea && l.Ms is not null).Select(l => l.Ms!.Value).ToList();
            puntos.Add(new Punto(t[0].Hora, ms.Count > 0 ? (int)Math.Round(ms.Average()) : null, t.All(l => l.EnLinea), t.Count(l => !l.EnLinea), t.Length));
        }
        var muestras = puntos.Sum(p => p.Muestras); var caidas = puntos.Sum(p => p.Caidas);
        var conMs = puntos.Where(p => p.Ms is not null).ToList();
        var resumen = new
        {
            muestras, caidas,
            disponibilidad = muestras > 0 ? Math.Round(100.0 * (muestras - caidas) / muestras, 2) : (double?)null,
            mediaMs = conMs.Count > 0 ? (int)Math.Round(conMs.Sum(p => p.Ms!.Value * (double)p.Muestras) / conMs.Sum(p => (double)p.Muestras)) : (int?)null,
            maximaMs = (await db.LecturasRed.AsNoTracking().Where(l => l.DispositivoId == dispositivo && l.Hora >= desde && l.Ms != null).Select(l => l.Ms).ToListAsync(ct)).Concat(horas.Select(h => h.MaxMs)).Max(),
        };
        return (puntos, resumen);
    }
}
