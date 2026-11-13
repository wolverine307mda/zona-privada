using System.Security.Claims;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

// «No lo reconozco»: si en la lista de equipos aparece uno que no es tuyo, un botón cierra sus sesiones, lo marca como sospechoso (el semáforo
// de Seguridad pasa a ámbar), te avisa por Telegram con su IP y su ciudad y te recuerda cambiar la contraseña. Además, el estado de cada equipo
// («en línea», «reciente», «hace tiempo») y un mapa con dónde están los equipos y las redes de los atajos (ubicación aproximada, por IP).
public record PlanNoLoReconozco(bool Permitido, string Motivo, IReadOnlyList<string> SesionesACerrar);
public record PuntoEquipo(string Tipo, string Nombre, double Lat, double Lon, string Pais, string Ciudad, bool Confianza, bool Sospechoso, DateTimeOffset? Visto);

public static class EquiposConfianza
{
    // Pura: qué sesiones hay que cerrar. Son las abiertas del mismo tipo de equipo y la misma IP que la última vez que se vio.
    // El equipo desde el que estás pulsando el botón no se deja (te echarías a ti): se dice claro.
    public static PlanNoLoReconozco Plan(EquipoConocido equipo, IEnumerable<SesionAbierta> abiertas, SesionAbierta? actual)
    {
        bool Es(SesionAbierta s) => s.Ip == equipo.UltimaIp && Dispositivo.Nombre(s.Agente) == equipo.Nombre;
        if (actual is not null && Es(actual)) return new(false, "Es el equipo desde el que estás entrando ahora: no puedes echarte a ti mismo. Si no es tuyo, cambia la contraseña y usa «Cerrar las demás sesiones» desde otro equipo.", []);
        return new(true, "", abiertas.Where(s => s.Cerrada is null && Es(s)).Select(s => s.Id).ToList());
    }

    // «en línea» (sesión activa hace menos de 2 min), «reciente» (menos de 3 días) o «hace tiempo»
    public static string Estado(DateTimeOffset ultima, bool enLinea, DateTimeOffset ahora) =>
        enLinea ? "en-linea" : ahora - ultima < TimeSpan.FromDays(3) ? "reciente" : "hace-tiempo";

    // «hace 3 h desde Madrid, España»
    public static string Visto(DateTimeOffset ultima, string pais, string ciudad, DateTimeOffset ahora)
    {
        var t = ahora - ultima;
        var cuando = t < TimeSpan.FromMinutes(1) ? "ahora mismo" : t < TimeSpan.FromHours(1) ? $"hace {(int)t.TotalMinutes} min" : t < TimeSpan.FromDays(1) ? $"hace {(int)t.TotalHours} h" : $"hace {(int)t.TotalDays} d";
        var lugar = pais == "?" || pais.Length == 0 ? "" : ciudad.Length > 0 ? $" desde {ciudad}, {pais}" : $" desde {pais}";
        return cuando + lugar;
    }

    // «1.2.3.0/24» o «2a00:1:2::/48» → una IP de esa red para buscarla en la base
    public static string? IpDeRed(string red) => red.Split('/')[0] is { Length: > 0 } ip && System.Net.IPAddress.TryParse(ip, out _) ? ip : null;

    public static void MapEquiposConfianza(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/seguridad/equipos").RequireAuthorization();

        g.MapPost("/{id:int}/no-reconozco", async (int id, ClaimsPrincipal user, AppDbContext db, IAvisos avisos, TimeProvider reloj, CancellationToken ct) =>
        {
            var e = await db.Equipos.FindAsync([id], ct);
            if (e is null) return Results.NotFound();
            var abiertas = await db.Sesiones.Where(s => s.Cerrada == null).ToListAsync(ct);
            var actualId = Sesiones.Actual(user);
            var plan = Plan(e, abiertas, abiertas.FirstOrDefault(s => s.Id == actualId));
            if (!plan.Permitido) return Results.Conflict(new { detalle = plan.Motivo });

            var ahora = reloj.GetUtcNow();
            foreach (var s in abiertas.Where(s => plan.SesionesACerrar.Contains(s.Id))) s.Cerrada = ahora;
            e.Sospechoso = true; e.SospechosoDesde = ahora; e.Confianza = false;
            await db.SaveChangesAsync(ct);

            var lugar = e.Pais == "?" ? "lugar desconocido" : e.Ciudad.Length > 0 ? $"{e.Ciudad}, {e.Pais}" : e.Pais;
            var texto = $"Has marcado «{e.Nombre}» como NO reconocido. IP {e.UltimaIp} ({lugar}). Se han cerrado {plan.SesionesACerrar.Count} sesión(es). Cambia tu contraseña en Perfil por si acaso.";
            try { await avisos.Avisar("seguridad", "🚨 Equipo no reconocido", texto, "/perfil", $"🚨 {texto}", ct); } catch (Exception) { /* el aviso es un extra: lo importante ya está hecho */ }
            return Results.Ok(new { cerradas = plan.SesionesACerrar.Count, cambiarClave = true, ip = e.UltimaIp, lugar });
        });

        // el mapa: equipos y redes de los atajos, con la ubicación aproximada de su IP; detrás del PIN
        g.MapGet("/mapa", async (AppDbContext db, GeoIp geo) =>
        {
            var puntos = new List<PuntoEquipo>();
            foreach (var e in await db.Equipos.AsNoTracking().ToListAsync())
                if (geo.Buscar(e.UltimaIp) is { } p) puntos.Add(new PuntoEquipo("equipo", e.Nombre, p.Lat, p.Lon, p.Pais, p.Ciudad, e.Confianza, e.Sospechoso, e.Ultima));
            foreach (var t in await db.TokensAtajo.AsNoTracking().Where(t => !t.Revocado).ToListAsync())
                foreach (var red in t.Redes.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (IpDeRed(red) is { } ip && geo.Buscar(ip) is { } p) puntos.Add(new PuntoEquipo("atajo", t.Nombre, p.Lat, p.Lon, p.Pais, p.Ciudad, false, false, t.UltimoUso));
            return Results.Ok(new { disponible = geo.Disponible, aproximada = true, puntos });
        }).AddEndpointFilter<PinFiltro>();
    }
}
