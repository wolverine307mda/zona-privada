using Mda.Api.Agenda;
using Mda.Api.Asistente;
using Mda.Api.Data;
using Mda.Api.Domotica;
using Mda.Api.Telegram;
using Mda.Api.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Contexto;

// «Me voy a dormir» y «Despierto»: la rutina de la noche y la de la mañana. Aplican tus reglas de contexto con evento `duermo` / `despierto`
// (escena de casa, escena del PC, aviso), y devuelven un mensaje con lo que viene: la agenda, el tiempo, a qué hora despertar y,
// por la noche, lo que se queda abierto o encendido (las mismas comprobaciones que al salir de casa).
// Las escenas del PC que apagan el equipo nunca se aplican solas.
public static class Dormir
{
    public const int MargenAlarmaMinutos = 90;      // se despierta una hora y media antes de lo primero del día
    public const string ClaveUltima = "dormir-ultima";

    // Pura: hora a la que despertar para llegar con margen a lo primero del día; sin nada por la mañana, no se sugiere ninguna
    public static TimeOnly? Alarma(TimeOnly? primera, int margenMinutos = MargenAlarmaMinutos)
    {
        if (primera is not { } p || p.ToTimeSpan() < TimeSpan.FromMinutes(margenMinutos + 60)) return null;   // antes de las 2:30 no tiene sentido sugerir nada
        return p.AddMinutes(-margenMinutos);
    }

    // Pura: el mensaje que vuelve al atajo
    public static string Mensaje(bool noche, string agenda, string? tiempo, TimeOnly? alarma, RevisionSalida? casa, int tareas, IReadOnlyList<string> hecho)
    {
        var p = new List<string> { noche ? "Buenas noches." : "Buenos días." };
        p.Add(agenda);
        if (tiempo is { Length: > 0 }) p.Add($"Tiempo: {tiempo}.");
        if (noche && alarma is { } a) p.Add($"Despierta a las {a:HH\\:mm}.");
        if (!noche && tareas > 0) p.Add($"Tareas para hoy: {tareas}.");
        if (casa is { } r && (r.Abiertos.Count > 0 || r.Encendidos.Count > 0))
            p.Add("Ojo: " + string.Join("; ", r.Abiertos.Select(x => $"{x.Nombre} abierta").Concat(r.Encendidos.Select(x => $"{x.Nombre} encendido"))) + ".");
        if (hecho.Count > 0) p.Add("Hecho: " + string.Join(" · ", hecho) + ".");
        return string.Join(" ", p);
    }

    public static async Task<string> Ejecutar(AppDbContext db, MotorContexto motor, ServicioTiempo tiempo, Zona zona, TimeProvider reloj, bool noche, CancellationToken ct)
    {
        var evento = noche ? "duermo" : "despierto";
        var ahora = reloj.GetUtcNow();
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, zona.Info).DateTime);
        var dia = noche ? hoy.AddDays(1) : hoy;

        // las reglas del evento se aplican con sus condiciones (franja, días, tiempo, espera) y dejan su resumen
        var antes = await db.ReglasContexto.AsNoTracking().Where(r => r.Evento == evento).ToDictionaryAsync(r => r.Id, r => r.Ultima, ct);
        await motor.Disparar(db, evento, ct);
        var hecho = (await db.ReglasContexto.AsNoTracking().Where(r => r.Evento == evento).ToListAsync(ct))
            .Where(r => r.Ultima != antes.GetValueOrDefault(r.Id)).Select(r => $"«{r.Nombre}»: {r.Resultado}").ToList();

        var agenda = await AsistenteEndpoints.Agenda(db, zona, dia, hoy, ct);
        // lo primero del día (que no sea «todo el día») para sugerir la hora de despertar
        var ini = dia.ToDateTime(TimeOnly.MinValue);
        var desde = new DateTimeOffset(ini, zona.Info.GetUtcOffset(ini));
        var primera = (await AgendaEndpoints.Apariciones(db, desde, desde.AddDays(1), zona, ct)).Where(a => !a.TodoElDia && !(a.Tipo == TipoEvento.Recordatorio && a.Hecho))
            .OrderBy(a => a.Inicio).Select(a => (DateTimeOffset?)a.Inicio).FirstOrDefault();
        var alarma = noche ? Alarma(primera is { } f ? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(f, zona.Info).DateTime) : null) : null;

        var prev = await tiempo.Leer(ct);
        var clima = prev is null ? null : $"{prev.Ahora.Temperatura:0} ° y {prev.Ahora.Texto.ToLowerInvariant()}";

        // por la noche se mira lo que se queda abierto o encendido (los aparatos que vigilas al salir de casa)
        RevisionSalida? casa = null;
        if (noche)
        {
            var (abiertos, encendidos) = SalidaCasa.Revisar(await db.Aparatos.AsNoTracking().ToListAsync(ct), await db.Entidades.AsNoTracking().ToListAsync(ct), await SalidaCasa.Leer(db, ct), ahora);
            casa = new RevisionSalida(abiertos, encendidos, [], []);
        }
        var tareas = noche ? 0 : await db.Tareas.AsNoTracking().CountAsync(t => !t.Hecha && t.Vence != null && t.Vence <= hoy, ct);

        var texto = Mensaje(noche, agenda.Replace("\n", " · "), clima, alarma, casa, tareas, hecho);
        var fila = await db.Ajustes.FirstOrDefaultAsync(a => a.Clave == ClaveUltima, ct);
        var valor = $"{ahora:O}|{evento}|{(texto.Length > 300 ? texto[..300] : texto)}";
        if (fila is null) db.Ajustes.Add(new Mda.Api.Seguridad.Ajuste { Clave = ClaveUltima, Valor = valor }); else fila.Valor = valor;
        await db.SaveChangesAsync(ct);
        return texto;
    }

    public static void MapDormir(this IEndpointRouteBuilder app)
    {
        // desde la app (el botón de Inicio) para probar o usarlo sin el iPhone
        app.MapPost("/api/contexto/dormir", async (EventoContexto e, AppDbContext db, MotorContexto motor, ServicioTiempo tiempo, Zona zona, TimeProvider reloj, CancellationToken ct) =>
        {
            if (e.Evento is not ("duermo" or "despierto")) return Results.ValidationProblem(new Dictionary<string, string[]> { ["evento"] = ["El evento es duermo o despierto."] });
            return Results.Ok(new { mensaje = await Ejecutar(db, motor, tiempo, zona, reloj, e.Evento == "duermo", ct) });
        }).RequireAuthorization();
    }
}
