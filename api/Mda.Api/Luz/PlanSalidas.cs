using Mda.Api.Data;
using Mda.Api.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Luz;

public record SalidaPlan(int? PuntoId, string Punto, string Orientacion, VentanaLuz Ventana, int Nota, string? Orienta);

// Las mejores salidas de la semana entre todos tus puntos. La nota de cada ventana ya viene de la previsión (nubes, luna…); aquí se ajusta por hacia
// dónde mira el punto: si mira hacia donde cae el Sol (o está el núcleo de la Vía Láctea), +1; si mira al lado contrario, −1. Es una heurística,
// no una medida: se dice en la pantalla.
public static class PlanSalidas
{
    private static readonly string[] Rumbos = ["N", "NE", "E", "SE", "S", "SO", "O", "NO"];

    public static double? Azimut(string orientacion) { var i = Array.IndexOf(Rumbos, orientacion); return i < 0 ? null : i * 45.0; }
    public static double Diferencia(double a, double b) { var d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }

    // (nota ajustada, por qué) para una ventana de ese tipo cuando el objetivo (Sol o núcleo) está en ese azimut
    public static (int Nota, string? Orienta) Ajustar(string tipo, int nota, string orientacion, double azimutObjetivo)
    {
        if (Azimut(orientacion) is not { } mira) return (nota, null);
        var diff = Diferencia(mira, azimutObjetivo);
        var objetivo = tipo == "noche" ? "el núcleo de la Vía Láctea" : tipo == "amanecer" ? "por donde sale el Sol" : "por donde se pone el Sol";
        if (diff <= 45) return (Math.Min(10, nota + 1), $"mira hacia {objetivo}");
        if (diff >= 135) return (Math.Max(1, nota - 1), $"mira al lado contrario de {objetivo}");
        return (nota, null);
    }

    // Las `cuantas` mejores, la nota primero y, a igualdad, la más cercana en el tiempo; como mucho una por punto y día (la mejor)
    public static List<SalidaPlan> Mejores(IEnumerable<SalidaPlan> todas, int cuantas) =>
        todas.GroupBy(s => (s.PuntoId, s.Ventana.Inicio.Date))
            .Select(g => g.OrderByDescending(s => s.Nota).ThenBy(s => s.Ventana.Inicio).First())
            .OrderByDescending(s => s.Nota).ThenBy(s => s.Ventana.Inicio).Take(cuantas).ToList();

    public static async Task<List<SalidaPlan>> Calcular(AppDbContext db, ServicioLuz luz, ServicioTiempo tiempo, Mda.Api.Agenda.Zona zona, DateOnly hoy, int dias, DateTimeOffset ahora, CancellationToken ct)
    {
        var a = await ServicioLuz.Leer(db, ct) with { Amanecer = true, Astro = true };
        var sitios = (await db.PuntosFoto.AsNoTracking().OrderBy(p => p.Nombre).ToListAsync(ct))
            .Select(p => (Id: (int?)p.Id, p.Nombre, p.Orientacion, Lugar: new Lugar(p.Nombre, p.Lat, p.Lon))).ToList();
        // sin puntos guardados se usa el lugar de siempre
        if (sitios.Count == 0 && await tiempo.LugarGuardado(ct) is { } l) sitios.Add((null, l.Nombre, "", l));
        var salidas = new List<SalidaPlan>();
        foreach (var s in sitios)
            for (var i = 0; i < dias; i++)
                foreach (var v in await luz.Ventanas(hoy.AddDays(i), s.Lugar, a, ct))
                {
                    if (v.Puntuacion is not { } nota || v.Fin <= ahora) continue;
                    var medio = v.Inicio + (v.Fin - v.Inicio) / 2;
                    var az = v.Tipo == "noche" ? Astro.PosNucleo(medio, s.Lugar.Lat, s.Lugar.Lon).Az : Astro.PosSol(medio, s.Lugar.Lat, s.Lugar.Lon).Az;
                    var (ajustada, orienta) = Ajustar(v.Tipo, nota, s.Orientacion, az);
                    salidas.Add(new SalidaPlan(s.Id, s.Nombre, s.Orientacion, v, ajustada, orienta));
                }
        return salidas;
    }
}
