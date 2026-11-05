using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

// Un equipo desde el que has entrado: el sistema y el navegador (sin versiones, para que una actualización no lo haga «nuevo») más el país.
// Si el equipo es nuevo, se avisa; si además es un país en el que nunca has entrado, el aviso lo dice.
public class EquipoConocido
{
    public int Id { get; set; }
    public string Huella { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Pais { get; set; } = "";
    public string Ciudad { get; set; } = "";
    public string UltimaIp { get; set; } = "";
    public DateTimeOffset Primera { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Ultima { get; set; } = DateTimeOffset.UtcNow;
    public int Entradas { get; set; } = 1;
    // marcado a mano: un equipo de confianza (por ejemplo, tu móvil) cambia de país sin dar el aviso de «país nuevo»
    public bool Confianza { get; set; }
}

public record EntradaEquipo(EquipoConocido Equipo, bool Nuevo, bool PaisNuevo, bool Aprendiendo);

public static class Equipos
{
    // «iPhone · Safari|ES». Sin país conocido (la base de geolocalización no está o la IP es local) va «?», para no confundir un equipo con otro.
    public static string Huella(string agente, string? pais) => $"{Dispositivo.Nombre(agente)}|{(string.IsNullOrWhiteSpace(pais) ? "?" : pais.Trim().ToUpperInvariant())}";

    // ¿Es la primera vez que se entra desde ese país? Lo desconocido («?») no cuenta como país nuevo.
    public static bool EsPaisNuevo(IEnumerable<string> paisesConocidos, string? pais) =>
        !string.IsNullOrWhiteSpace(pais) && pais != "?" && !paisesConocidos.Any(p => string.Equals(p, pais, StringComparison.OrdinalIgnoreCase));

    // Apunta la entrada. Con la tabla vacía (la primera vez que se usa esto) aprende en silencio: tus equipos de siempre no tienen por qué avisar.
    public static async Task<EntradaEquipo> Registrar(AppDbContext db, string agente, string ip, Geo? geo, DateTimeOffset ahora, CancellationToken ct = default)
    {
        var pais = geo?.Pais is { Length: > 0 } p ? p : null;
        var huella = Huella(agente, pais);
        var existente = await db.Equipos.FirstOrDefaultAsync(e => e.Huella == huella, ct);
        if (existente is not null)
        {
            existente.Ultima = ahora; existente.UltimaIp = ip; existente.Entradas++;
            if (geo?.Ciudad is { Length: > 0 } c) existente.Ciudad = c;
            await db.SaveChangesAsync(ct);
            return new EntradaEquipo(existente, false, false, false);
        }
        var aprendiendo = !await db.Equipos.AnyAsync(ct);
        var paises = await db.Equipos.Select(e => e.Pais).Distinct().ToListAsync(ct);
        var nuevo = new EquipoConocido { Huella = huella, Nombre = Dispositivo.Nombre(agente), Pais = pais ?? "?", Ciudad = geo?.Ciudad ?? "", UltimaIp = ip, Primera = ahora, Ultima = ahora };
        db.Equipos.Add(nuevo);
        await db.SaveChangesAsync(ct);
        return new EntradaEquipo(nuevo, !aprendiendo, !aprendiendo && EsPaisNuevo(paises, pais), aprendiendo);
    }
}
