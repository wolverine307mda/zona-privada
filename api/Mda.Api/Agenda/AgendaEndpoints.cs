using Mda.Api.Avisos;
using Mda.Api.Conexiones;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Agenda;

public record EventoEntrada(
    TipoEvento Tipo, string Titulo, string? Notas, string? Lugar, DateTimeOffset Inicio, DateTimeOffset? Fin, bool TodoElDia, string? Color,
    Repeticion Repetir, DateOnly? RepetirHasta, int? AvisoMinutos, bool PorCorreo, bool PorWhatsApp, bool EnGoogle, bool Hecho = false, bool PorPush = false, bool PorTelegram = false);

// Cada aparición de un evento en el calendario: el evento (o el de Google) con el inicio de esa repetición
public record Aparicion(
    string Clave, int? Id, string Origen, TipoEvento Tipo, string Titulo, string? Notas, string? Lugar, DateTimeOffset Inicio, DateTimeOffset? Fin,
    bool TodoElDia, string Color, Repeticion Repetir, int? AvisoMinutos, bool PorCorreo, bool PorWhatsApp, bool Hecho, bool EnGoogle, string? Url, bool PorPush = false, bool PorTelegram = false);

// Agenda: eventos y recordatorios propios más los de Google Calendar, si está enlazado
public static class AgendaEndpoints
{
    private static readonly string[] Colores = ["", "naranja", "rojo", "rosa", "morado", "azul", "verde", "amarillo"];

    public static void MapAgenda(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/agenda").RequireAuthorization();

        g.MapGet("/", async (DateTimeOffset desde, DateTimeOffset hasta, AppDbContext db, GoogleCalendar google, Zona zona, CancellationToken ct) =>
        {
            if (hasta <= desde || hasta - desde > TimeSpan.FromDays(400)) return Results.BadRequest();
            var lista = await Apariciones(db, desde, hasta, zona, ct);
            var deGoogle = await google.Leer(desde, hasta, ct);
            if (deGoogle is not null)
            {
                // los que ya son nuestros (copiados a Google) no se repiten
                var propios = (await db.Eventos.AsNoTracking().Where(e => e.GoogleId != null).Select(e => e.GoogleId!).ToListAsync(ct)).ToHashSet();
                lista.AddRange(deGoogle.Where(x => !propios.Contains(x.Id) && (x.Recurrente is null || !propios.Contains(x.Recurrente)))
                    .Select(x => new Aparicion($"g:{x.Id}", null, "google", TipoEvento.Evento, x.Titulo, x.Notas, x.Lugar, x.Inicio, x.Fin, x.TodoElDia,
                        "google", Repeticion.No, null, false, false, false, true, x.Url)));
            }
            return Results.Ok(new { google = deGoogle is not null, apariciones = lista.OrderBy(a => a.Inicio) });
        });

        // lo que viene: para el inicio y la lista de recordatorios
        g.MapGet("/proximos", async (int? dias, AppDbContext db, Zona zona, TimeProvider reloj, CancellationToken ct) =>
        {
            var ahora = reloj.GetUtcNow();
            var hoy = TimeZoneInfo.ConvertTime(ahora, zona.Info).Date;
            var desde = new DateTimeOffset(hoy, zona.Info.GetUtcOffset(hoy)).ToUniversalTime();
            var lista = await Apariciones(db, desde, ahora.AddDays(Math.Clamp(dias ?? 14, 1, 90)), zona, ct);
            // los recordatorios sin hacer de días pasados siguen pendientes
            var atrasados = await db.Eventos.AsNoTracking()
                .Where(e => e.Tipo == TipoEvento.Recordatorio && !e.Hecho && e.Repetir == Repeticion.No && e.Inicio < desde).ToListAsync(ct);
            lista.InsertRange(0, atrasados.Select(e => Aparicion(e, e.Inicio)));
            return Results.Ok(lista.Where(a => !(a.Tipo == TipoEvento.Recordatorio && a.Hecho)).OrderBy(a => a.Inicio).Take(50));
        });

        g.MapPost("/", (EventoEntrada e, AppDbContext db, GoogleCalendar google, ServicioConexiones con, Zona zona, TimeProvider reloj, CancellationToken ct) =>
            Crear(e, db, google, con, zona, reloj, ct));

        g.MapPut("/{id:int}", async (int id, EventoEntrada e, AppDbContext db, GoogleCalendar google, ServicioConexiones con, Zona zona, TimeProvider reloj, CancellationToken ct) =>
        {
            if (Validar(e) is { } error) return error;
            var ev = await db.Eventos.FindAsync([id], ct);
            if (ev is null) return Results.NotFound();
            var estabaEnGoogle = ev.GoogleId;
            Copiar(e, ev, zona, reloj.GetUtcNow());
            if (!ev.EnGoogle && estabaEnGoogle is not null) { await google.Borrar(estabaEnGoogle, ct); ev.GoogleId = null; }
            var aviso = await Sincronizar(ev, google, con, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ev.Id, googleError = aviso });
        });

        // marcar un recordatorio como hecho (o volver a dejarlo pendiente)
        g.MapPost("/{id:int}/hecho", async (int id, bool? hecho, AppDbContext db, Zona zona, TimeProvider reloj, CancellationToken ct) =>
        {
            var ev = await db.Eventos.FindAsync([id], ct);
            if (ev is null) return Results.NotFound();
            ev.Hecho = hecho ?? !ev.Hecho;
            Reprogramar(ev, reloj.GetUtcNow(), zona);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ev.Id, ev.Hecho });
        });

        g.MapDelete("/{id:int}", (int id, AppDbContext db, GoogleCalendar google, CancellationToken ct) => Borrar(id, db, google, ct));

        // qué canales de aviso hay, si Google está enlazado y tu horario de trabajo
        g.MapGet("/estado", async (IAvisos avisos, ServicioConexiones con, AppDbContext db, CancellationToken ct) =>
        {
            var c = await con.Leer(ServicioConexiones.Google, ct);
            return Results.Ok(new
            {
                canales = avisos.Disponibles(),
                google = new { configurado = con.Configurado(ServicioConexiones.Google), conectado = c is not null, cuenta = c?.Cuenta },
                horario = await Horario.Leer(db, ct),
            });
        });

        g.MapGet("/horario", async (AppDbContext db, CancellationToken ct) => Results.Ok(await Horario.Leer(db, ct)));
        g.MapPut("/horario", (Horario h, AppDbContext db, CancellationToken ct) => Horario.Poner(db, h, ct));
    }

    // crear y borrar (los usan la API y el bot de Telegram)
    internal static async Task<IResult> Crear(EventoEntrada e, AppDbContext db, GoogleCalendar google, ServicioConexiones con, Zona zona, TimeProvider reloj, CancellationToken ct)
    {
        if (Validar(e) is { } error) return error;
        var ev = new Evento();
        Copiar(e, ev, zona, reloj.GetUtcNow());
        db.Eventos.Add(ev);
        await db.SaveChangesAsync(ct);
        var aviso = await Sincronizar(ev, google, con, ct);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/agenda/{ev.Id}", new { ev.Id, googleError = aviso });
    }

    internal static async Task<IResult> Borrar(int id, AppDbContext db, GoogleCalendar google, CancellationToken ct)
    {
        var ev = await db.Eventos.FindAsync([id], ct);
        if (ev is null) return Results.NotFound();
        if (ev.GoogleId is { } gid) await google.Borrar(gid, ct);
        db.Eventos.Remove(ev);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    internal static async Task<List<Aparicion>> Apariciones(AppDbContext db, DateTimeOffset desde, DateTimeOffset hasta, Zona zona, CancellationToken ct)
    {
        // PostgreSQL solo admite fechas en UTC como parámetro (el front manda la hora de Madrid)
        desde = desde.ToUniversalTime();
        hasta = hasta.ToUniversalTime();
        // los que no se repiten, filtrados en la base; los que se repiten, todos (son pocos) y se calculan aquí
        var eventos = await db.Eventos.AsNoTracking()
            .Where(e => e.Repetir != Repeticion.No || (e.Inicio < hasta && (e.Fin ?? e.Inicio) >= desde)).ToListAsync(ct);
        return eventos.SelectMany(e => Ocurrencias.Entre(e, desde, hasta, zona.Info).Select(o => Aparicion(e, o))).ToList();
    }

    private static Aparicion Aparicion(Evento e, DateTimeOffset inicio) => new(
        $"{e.Id}:{inicio.ToUnixTimeSeconds()}", e.Id, "propio", e.Tipo, e.Titulo, e.Notas, e.Lugar, inicio, e.Fin is { } f ? inicio + (f - e.Inicio) : null,
        e.TodoElDia, e.Color, e.Repetir, e.AvisoMinutos, e.PorCorreo, e.PorWhatsApp, e.Hecho, e.EnGoogle, null, e.PorPush, e.PorTelegram);

    // Copia en Google si toca; devuelve un aviso para el front si no se pudo
    private static async Task<string?> Sincronizar(Evento ev, GoogleCalendar google, ServicioConexiones con, CancellationToken ct)
    {
        if (!ev.EnGoogle) return null;
        if (await con.Leer(ServicioConexiones.Google, ct) is null) return "Google Calendar no está enlazado: se ha guardado solo aquí.";
        var id = await google.Guardar(ev, ct);
        if (id is null) return "No se pudo copiar en Google Calendar: se ha guardado solo aquí.";
        ev.GoogleId = id;
        return null;
    }

    public static void Reprogramar(Evento ev, DateTimeOffset ahora, Zona zona)
    {
        var p = Ocurrencias.ProximoAviso(ev, ahora, ahora, zona.Info);
        // una repetición ya avisada no se vuelve a avisar al editar el evento (guardar toca cualquier cosa, también el título)
        while (p is { } x && ev.UltimaOcurrenciaAvisada == x.De) p = Ocurrencias.ProximoAviso(ev, x.De.AddSeconds(1), ahora, zona.Info);
        // el estado de envío es de una repetición concreta: si cambia la pendiente, se empieza de cero
        if (p?.De != ev.AvisoDe) { ev.CanalesEnviados = 0; ev.CanalesRendidos = 0; ev.Intentos = 0; ev.ReintentoDesde = null; }
        ev.ProximoAviso = p?.Aviso;
        ev.AvisoDe = p?.De;
    }

    private static IResult? Validar(EventoEntrada e)
    {
        var errores = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(e.Titulo) || e.Titulo.Length > 200) errores["titulo"] = ["Escribe un título (máximo 200 caracteres)."];
        if (e.Notas?.Length > 4000) errores["notas"] = ["Las notas no pueden pasar de 4000 caracteres."];
        if (e.Lugar?.Length > 200) errores["lugar"] = ["El lugar no puede pasar de 200 caracteres."];
        if (e.Fin is { } f && f < e.Inicio) errores["fin"] = ["El final es anterior al inicio."];
        if (e.Fin is { } f2 && f2 - e.Inicio > TimeSpan.FromDays(366)) errores["fin"] = ["Un evento no puede durar más de un año."];
        if (!Colores.Contains(e.Color ?? "")) errores["color"] = ["Ese color no existe."];
        if (e.AvisoMinutos is < -1440 or > 43200) errores["avisoMinutos"] = ["El aviso tiene que ser como mucho 30 días antes."];
        if (e.RepetirHasta is { } h && h < DateOnly.FromDateTime(e.Inicio.Date)) errores["repetirHasta"] = ["La fecha de fin de la repetición es anterior al inicio."];
        return errores.Count > 0 ? Results.ValidationProblem(errores) : null;
    }

    private static void Copiar(EventoEntrada e, Evento ev, Zona zona, DateTimeOffset ahora)
    {
        ev.Tipo = e.Tipo;
        ev.Titulo = e.Titulo.Trim();
        ev.Notas = string.IsNullOrWhiteSpace(e.Notas) ? null : e.Notas.Trim();
        ev.Lugar = string.IsNullOrWhiteSpace(e.Lugar) ? null : e.Lugar.Trim();
        ev.Inicio = e.Inicio.ToUniversalTime();
        ev.Fin = e.Tipo == TipoEvento.Recordatorio ? null : e.Fin?.ToUniversalTime();
        ev.TodoElDia = e.TodoElDia;
        ev.Color = e.Color ?? "";
        ev.Repetir = e.Repetir;
        ev.RepetirHasta = e.Repetir == Repeticion.No ? null : e.RepetirHasta;
        ev.AvisoMinutos = e.AvisoMinutos;
        ev.PorCorreo = e.PorCorreo;
        ev.PorWhatsApp = e.PorWhatsApp;
        ev.PorPush = e.PorPush;
        ev.PorTelegram = e.PorTelegram;
        ev.EnGoogle = e.EnGoogle;
        ev.Hecho = e.Hecho;
        Reprogramar(ev, ahora, zona);
    }
}
