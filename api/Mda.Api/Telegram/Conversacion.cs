using System.Globalization;
using System.Text;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Asistente;
using Mda.Api.Avisos;
using Mda.Api.Conexiones;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Mda.Api.Tiempo;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Telegram;

// Lo que pasa cuando escribes al bot: le hablas como al asistente de la app («ayer 12 € en el Mercadona»,
// «recuérdame mañana a las 9 llamar al taller», «¿cuánto llevo en restaurantes?», «¿va a llover mañana?»).
// Lo que hay que guardar llega con botones para confirmarlo, y después se puede deshacer.
public class Conversacion(
    Bot bot, AppDbContext db, IAvisos avisos, GoogleCalendar google, ServicioConexiones con, ServicioTiempo tiempo,
    Zona zona, TimeProvider reloj, IMemoryCache cache, ILogger<Conversacion> log)
{
    private static readonly CultureInfo Es = new("es-ES");

    public const string Ayuda =
        "Escríbeme como al asistente de la app:\n\n" +
        "💸 <i>ayer 12 € en el Mercadona</i>\n" +
        "💰 <i>cobré la nómina 1.450 €</i>\n" +
        "⏰ <i>recuérdame mañana a las 9 llamar al taller</i>\n" +
        "📲 <i>nuevo recordatorio por WhatsApp el viernes a las 18 sacar la basura</i>\n" +
        "   (sin decir nada te aviso por la app; también «por correo», «por Telegram» o «por todas»)\n" +
        "📅 <i>cita con el médico el jueves de 10 a 11 en el centro de salud</i>\n" +
        "💼 <i>curro de 8:30 a 17:30 de lunes a viernes</i>\n" +
        "🗓️ <i>¿qué tengo mañana?</i>\n" +
        "🐷 <i>50 € a la hucha del viaje</i>\n" +
        "❓ <i>¿cuánto llevo en restaurantes?</i>\n" +
        "🌦️ <i>¿va a llover mañana?</i>\n\n" +
        "Y estos atajos: /hoy (tu día), /tiempo, /saldo, /ayuda";

    public async Task Atender(JsonElement u, CancellationToken ct)
    {
        if (u.TryGetProperty("callback_query", out var cb)) { await Boton(cb, ct); return; }
        if (!u.TryGetProperty("message", out var m) || !m.TryGetProperty("chat", out var chat)) return;
        var id = chat.GetProperty("id").GetInt64();
        // solo chats privados con el bot: en un grupo escribiría cualquiera de sus miembros
        if (chat.TryGetProperty("type", out var tipoChat) && tipoChat.GetString() != "private") return;
        var texto = m.TryGetProperty("text", out var t) ? t.GetString()?.Trim() ?? "" : "";
        var quien = m.TryGetProperty("from", out var de) && de.TryGetProperty("first_name", out var n) ? n.GetString() ?? "" : "";

        // vincular con el enlace de un solo uso de Seguridad › Avisos
        if (texto.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var codigo = texto.Length > 6 ? texto[6..].Trim() : "";
            if (codigo.Length > 0 && bot.CodigoValido(codigo))
            {
                await bot.Vincular(id, quien, ct);
                log.LogInformation("Telegram vinculado con el chat de {Quien}", quien);
                await bot.Enviar($"¡Hola{(quien.Length > 0 ? ", " + H(quien) : "")}! 👋 Ya estamos conectados: te avisaré por aquí de tus recordatorios y de lo importante.\n\n{Ayuda}", html: true, a: id, ct: ct);
                return;
            }
            if (bot.Chat != id) { await bot.Enviar("Este bot es privado. Para usarlo, vincúlalo desde tu zona privada: Seguridad › Avisos › Telegram.", a: id, ct: ct); return; }
            await bot.Enviar(Ayuda, html: true, a: id, ct: ct);
            return;
        }
        // nadie más que el chat vinculado
        if (bot.Chat != id) { await bot.Enviar("Este bot es privado.", a: id, ct: ct); return; }
        if (texto.Length == 0) { await bot.Enviar("Por ahora solo entiendo texto. " + Ayuda, html: true, ct: ct); return; }

        var comando = texto.Split(' ', '@')[0].ToLowerInvariant();
        switch (comando)
        {
            case "/ayuda" or "/help": await bot.Enviar(Ayuda, html: true, ct: ct); return;
            case "/hoy": await bot.Enviar(await Hoy(ct), html: true, ct: ct); return;
            case "/tiempo": texto = "qué tiempo hace hoy"; break;
            case "/saldo": texto = "¿cuánto tengo?"; break;
        }
        if (texto.Length > 300) { await bot.Enviar("Es muy largo: dímelo en una frase.", ct: ct); return; }

        var ctx = await AsistenteEndpoints.ContextoDe(db, reloj, zona, ct);
        var p = Interprete.Entender(texto, ctx);
        if (p.Pregunta is { } q)
        {
            await bot.Enviar(await AsistenteEndpoints.Responder(db, tiempo, zona, q, DateOnly.FromDateTime(ctx.Ahora), ct), ct: ct);
            return;
        }
        if (p.Clase == "nada") { await bot.Enviar(p.Resumen, ct: ct); return; }

        var clave = Guid.NewGuid().ToString("N")[..10];
        cache.Set($"tg:{clave}", p, TimeSpan.FromHours(2));
        var sb = new StringBuilder($"<b>{H(p.Resumen)}</b>");
        foreach (var d in p.Dudas ?? []) sb.Append($"\n<i>· {H(d)}</i>");
        await bot.Enviar(sb.ToString(), Botones(("✅ Guardar", $"si:{clave}"), ("✖️ Cancelar", $"no:{clave}")), html: true, ct: ct);
    }

    private static readonly HashSet<(long Chat, long Mensaje)> Deshechos = [];

    private async Task Boton(JsonElement cb, CancellationToken ct)
    {
        var datos = cb.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
        var desde = cb.GetProperty("from").GetProperty("id").GetInt64();
        var mensaje = cb.TryGetProperty("message", out var m) ? m : default;
        var chat = mensaje.ValueKind == JsonValueKind.Object ? mensaje.GetProperty("chat").GetProperty("id").GetInt64() : desde;
        var idMensaje = mensaje.ValueKind == JsonValueKind.Object ? mensaje.GetProperty("message_id").GetInt64() : 0;
        if (bot.Chat != chat || desde != chat) { await Contestar(cb, "Este bot es privado.", ct); return; }

        var partes = datos.Split(':', 2);
        var (accion, clave) = (partes[0], partes.ElementAtOrDefault(1) ?? "");
        switch (accion)
        {
            case "sy":
                await Editar(chat, idMensaje, "✅ Vale, anotado: fuiste tú.", null, ct);
                await Contestar(cb, null, ct);
                return;
            case "sn":
                // cierra la sesión que abrió el señuelo y bloquea la entrada una hora
                if (await db.Sesiones.FindAsync([clave], ct) is { Cerrada: null } abierta) abierta.Cerrada = DateTimeOffset.UtcNow;
                foreach (var u in await db.Users.ToListAsync(ct)) { u.LockoutEnabled = true; u.LockoutEnd = reloj.GetUtcNow().AddHours(1); }
                await db.SaveChangesAsync(ct);
                await Editar(chat, idMensaje, "🔒 Sesión cerrada y entrada bloqueada una hora. Cambia la contraseña cuando puedas.", null, ct);
                await Contestar(cb, "Bloqueado", ct);
                return;
            case "no":
                cache.Remove($"tg:{clave}");
                await Editar(chat, idMensaje, "✖️ Cancelado.", null, ct);
                await Contestar(cb, null, ct);
                return;
            case "si":
                if (!cache.TryGetValue($"tg:{clave}", out Propuesta? p) || p is null)
                {
                    await Contestar(cb, "Ha caducado: escríbemelo otra vez.", ct);
                    return;
                }
                var (ok, texto, deshacer) = await Guardar(p, ct);
                if (ok) cache.Remove($"tg:{clave}");
                await Editar(chat, idMensaje, (ok ? "✅ " : "⚠️ ") + H(texto), deshacer is null ? null : Botones(("↩️ Deshacer", deshacer)), ct);
                await Contestar(cb, ok ? "Guardado" : null, ct);
                return;
            case "dm" or "de" or "dh":
                // un segundo toque antes de que desaparezca el botón no deshace otra vez (restaría dos veces de una hucha)
                bool primera;
                lock (Deshechos) primera = Deshechos.Add((chat, idMensaje));
                var hecho = primera && await Deshacer(accion, clave, ct);
                await Editar(chat, idMensaje, hecho ? "↩️ Deshecho: no se ha guardado nada." : "Ya no se puede deshacer.", null, ct);
                await Contestar(cb, null, ct);
                return;
            default:
                await Contestar(cb, null, ct);
                return;
        }
    }

    private async Task<(bool Ok, string Texto, string? Deshacer)> Guardar(Propuesta p, CancellationToken ct)
    {
        IResult r = p.Clase switch
        {
            "movimiento" when p.Movimiento is { } m => await GastosEndpoints.Crear(m, db, avisos, log, ct),
            "agenda" when p.Evento is { } e => await AgendaEndpoints.Crear(e, db, google, con, zona, reloj, ct),
            "hucha" when p.Hucha is { } h => await Cartera.Aportar(h.Id, new Aportacion(h.Importe), db, ct),
            "horario" when p.Horario is { } hz => await Horario.Poner(db, hz, ct),
            _ => Results.BadRequest(),
        };
        if (r is ValidationProblem v) return (false, string.Join(" ", v.ProblemDetails.Errors.SelectMany(x => x.Value)), null);
        var valor = (r as IValueHttpResult)?.Value;
        return valor switch
        {
            Movimiento mv => (true, $"Apuntado: {p.Resumen}", $"dm:{mv.Id}"),
            Horario hz => (true, $"Horario guardado: {hz.Texto()}. Lo verás en el calendario y te avisaré si una cita cae dentro.", null),
            Hucha h => (true,$"{p.Resumen}. Ahora hay {Cartera.Euros(h.Ahorrado)}.", $"dh:{h.Id}:{p.Hucha!.Importe.ToString(CultureInfo.InvariantCulture)}"),
            not null when p.Clase == "agenda" && valor.GetType().GetProperty("Id")?.GetValue(valor) is int id => (true, $"En la agenda: {p.Resumen}", $"de:{id}"),
            _ => (false, "No se ha podido guardar. Prueba desde la app.", null),
        };
    }

    private async Task<bool> Deshacer(string accion, string clave, CancellationToken ct)
    {
        switch (accion)
        {
            case "dm" when int.TryParse(clave, out var id):
                if (await db.Movimientos.FindAsync([id], ct) is not { } m) return false;
                db.Movimientos.Remove(m);
                await db.SaveChangesAsync(ct);
                return true;
            case "de" when int.TryParse(clave, out var id):
                return await AgendaEndpoints.Borrar(id, db, google, ct) is NoContent;
            case "dh":
                var p = clave.Split(':');
                return p.Length == 2 && int.TryParse(p[0], out var hid) && decimal.TryParse(p[1], CultureInfo.InvariantCulture, out var importe)
                    && await Cartera.Aportar(hid, new Aportacion(-importe), db, ct) is Ok<Hucha>;
            default:
                return false;
        }
    }

    // tu día: el tiempo, lo que tienes en la agenda y cómo vas de dinero
    private async Task<string> Hoy(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(ahora, zona.Info);
        var hoy = DateOnly.FromDateTime(local.DateTime);
        var sb = new StringBuilder($"<b>{H(char.ToUpper(local.ToString("dddd d 'de' MMMM", Es)[0]) + local.ToString("dddd d 'de' MMMM", Es)[1..])}</b>\n");

        if (await tiempo.Leer(ct) is { } p) sb.Append('\n').Append(H(ServicioTiempo.Frase(p, hoy, hoy))).Append('\n');

        // la misma respuesta que «¿qué tengo hoy?», con el horario de trabajo incluido
        sb.Append("\n<b>Agenda</b>\n").Append(H(await AsistenteEndpoints.Agenda(db, zona, hoy, hoy, ct))).Append('\n');

        var q = new Pregunta(QuePregunta.Saldo, null, null, hoy, hoy, "ahora");
        sb.Append("\n<b>Dinero</b>\n").Append(H(await AsistenteEndpoints.Responder(db, tiempo, zona, q, hoy, ct)));
        return sb.ToString();
    }

    private static object Botones(params (string Texto, string Datos)[] b) =>
        new { inline_keyboard = new[] { b.Select(x => new { text = x.Texto, callback_data = x.Datos }).ToArray() } };

    private Task Editar(long chat, long mensaje, string html, object? teclado, CancellationToken ct)
    {
        var cuerpo = new Dictionary<string, object> { ["chat_id"] = chat, ["message_id"] = mensaje, ["text"] = html, ["parse_mode"] = "HTML" };
        if (teclado is not null) cuerpo["reply_markup"] = teclado;
        return bot.Llamar("editMessageText", cuerpo, ct);
    }

    private Task Contestar(JsonElement cb, string? texto, CancellationToken ct) =>
        bot.Llamar("answerCallbackQuery", texto is null ? new { callback_query_id = cb.GetProperty("id").GetString() } : new { callback_query_id = cb.GetProperty("id").GetString(), text = texto }, ct);

    // Telegram en modo HTML solo pide escapar estos tres
    private static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
