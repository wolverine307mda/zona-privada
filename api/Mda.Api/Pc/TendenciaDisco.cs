using System.Text.Json;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// A qué ritmo se llena un disco: recta de mínimos cuadrados sobre el espacio libre de los últimos 30 días (una foto al día, ya guardada en SaludDia).
// Devuelve los días que faltan para llegar a cero al ritmo de ahora, o null si no se está llenando o aún hay pocos datos.
public static class TendenciaDisco
{
    public const int VentanaDias = 30, MinimoMuestras = 7, MinimoSpanDias = 7;
    // menos de esto por día es ruido (un archivo temporal), no una tendencia
    public const double RuidoBytesDia = 1_000_000;
    public const int Tope = 3650;

    public static int? DiasHastaLleno(IReadOnlyList<(DateOnly Fecha, long Libre)> muestras)
    {
        if (muestras.Count < MinimoMuestras) return null;
        var ordenadas = muestras.OrderBy(m => m.Fecha).ToList();
        if (ordenadas[^1].Fecha.DayNumber - ordenadas[0].Fecha.DayNumber < MinimoSpanDias) return null;
        var x0 = ordenadas[0].Fecha.DayNumber;
        double n = ordenadas.Count, sx = 0, sy = 0, sxy = 0, sxx = 0;
        foreach (var (f, l) in ordenadas) { double x = f.DayNumber - x0; sx += x; sy += l; sxy += x * l; sxx += x * x; }
        var den = n * sxx - sx * sx;
        if (den == 0) return null;
        var pendiente = (n * sxy - sx * sy) / den;   // bytes por día; negativo = se está llenando
        if (pendiente > -RuidoBytesDia) return null;
        var dias = ordenadas[^1].Libre / -pendiente;
        return dias > Tope ? null : Math.Max(0, (int)Math.Ceiling(dias));
    }

    // Por cada volumen con historial, los días que le quedan (solo los que se están llenando)
    public static async Task<Dictionary<string, int>> Calcular(AppDbContext db, DateOnly hoy, CancellationToken ct = default)
    {
        var filas = await db.SaludDias.AsNoTracking().Where(d => d.Fecha >= hoy.AddDays(-VentanaDias)).OrderBy(d => d.Fecha).ToListAsync(ct);
        var porVolumen = new Dictionary<string, List<(DateOnly, long)>>();
        foreach (var f in filas)
        {
            try
            {
                foreach (var v in JsonDocument.Parse(f.Datos).RootElement.GetProperty("v").EnumerateArray())
                {
                    var nombre = v[0].GetString();
                    if (nombre is null) continue;
                    if (!porVolumen.TryGetValue(nombre, out var l)) porVolumen[nombre] = l = [];
                    l.Add((f.Fecha, v[1].GetInt64()));
                }
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { /* una fila rota no estropea las demás */ }
        }
        var r = new Dictionary<string, int>();
        foreach (var (nombre, m) in porVolumen) if (DiasHastaLleno(m) is { } d) r[nombre] = d;
        return r;
    }
}
