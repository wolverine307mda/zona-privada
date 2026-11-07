using System.Globalization;
using System.Text;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Asistente;
using Mda.Api.Avisos;
using Mda.Api.Conexiones;
using Mda.Api.Data;
using Mda.Api.Seguridad;
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
    Bot bot, AppDbContext db, IAvisos avisos, GoogleCalendar google, ServicioConexiones con, ServicioTiempo tiempo, Mda.Api.Domotica.ServicioCasa casa,
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
        "🌦️ <i>¿va a llover mañana?</i>\n" +
        "📝 <i>/tarea llamar al seguro</i> · /tareas · <i>/listo 12</i>\n" +
        "📄 /caducidades (DNI, ITV, seguros…)\n" +
        "🛒 <i>/compra leche, pan</i> · /compra (la lista) · <i>/comprado leche</i>\n\n" +
        "Y estos atajos: /hoy (tu día), /tiempo, /saldo, /ayuda\n" +
        "✅ /habitos (lo de hoy) y /hecho meditar (lo apunta)\n" +
        "🚨 /panico lo corta todo (sesiones, tokens, galería pública, entrada 1 h) y /desbloquear lo levanta.";

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
            case "/habitos" or "/hábitos": await bot.Enviar(await HabitosDeHoy(ct), html: true, ct: ct); return;
            case "/tareas": await bot.Enviar(await ListaTareas(ct), html: true, ct: ct); return;
            case "/tarea": await bot.Enviar(await NuevaTarea(texto.Length > 6 ? texto[6..].Trim() : "", ct), html: true, ct: ct); return;
            case "/listo": await bot.Enviar(await TareaHecha(texto.Length > 6 ? texto[6..].Trim() : "", ct), html: true, ct: ct); return;
            case "/caducidades" or "/papeles": await bot.Enviar(await Caducidades(ct), html: true, ct: ct); return;
            case "/compra": await bot.Enviar(await Compras(texto.Length > 7 ? texto[7..].Trim() : "", ct), html: true, ct: ct); return;
            case "/comprado": await bot.Enviar(await Comprado(texto.Length > 10 ? texto[10..].Trim() : "", ct), html: true, ct: ct); return;
            case "/hecho": await bot.Enviar(await Hecho(texto.Length > 6 ? texto[6..].Trim() : "", ct), html: true, ct: ct); return;
            case "/tiempo": texto = "qué tiempo hace hoy"; break;
            case "/saldo": texto = "¿cuánto tengo?"; break;
            case "/panico" or "/pánico":
                await bot.Enviar("🚨 " + Panico.Resumen(await Panico.Ejecutar(db, reloj, cache, ct)) + "\nPara volver a entrar antes, escribe /desbloquear.", ct: ct);
                return;
            case "/desbloquear":
                await Panico.Levantar(db, cache, ct);
                await bot.Enviar("🔓 Bloqueo levantado: ya puedes iniciar sesión. La galería y el estado público siguen apagados hasta que los actives en la app.", ct: ct);
                return;
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
            case "pv" or "pd" or "pc":
                {
                    var trozos = clave.Split(':');
                    var mov = int.TryParse(trozos[0], out var mid) ? await db.Movimientos.FindAsync([mid], ct) : null;
                    if (mov is null) { await Editar(chat, idMensaje, "Ya no existe este apunte.", null, ct); await Contestar(cb, null, ct); return; }
                    if (accion == "pd") { db.Movimientos.Remove(mov); await db.SaveChangesAsync(ct); await Editar(chat, idMensaje, "↩️ Deshecho: no se ha guardado nada.", null, ct); }
                    else
                    {
                        if (accion == "pc" && trozos.Length > 1 && Categorias.Valida(trozos[1], mov.Tipo)) { mov.Categoria = trozos[1]; await Reglas.Aprender(db, mov.Concepto, mov.Categoria, mov.Tipo, ct); }
                        mov.PorConfirmar = false;
                        await db.SaveChangesAsync(ct);
                        await Editar(chat, idMensaje, $"✅ {H(mov.Concepto)} {Cartera.Euros(mov.Importe)} · {H(Categorias.Todas[mov.Categoria].Nombre)}", Botones(("↩️ Deshacer", $"pd:{mov.Id}")), ct);
                    }
                    await Contestar(cb, null, ct);
                    return;
                }
            case "ca":
                {
                    // «Apagar» en el aviso de un aparato que lleva mucho encendido
                    var a = int.TryParse(clave, out var aid) ? await db.Aparatos.FindAsync([aid], ct) : null;
                    if (a is null) { await Editar(chat, idMensaje, "Ese aparato ya no existe.", null, ct); await Contestar(cb, null, ct); return; }
                    var (bien, mensajeCasa, _) = await casa.Ejecutar(db, a, "encendido", "false", ct);
                    await Editar(chat, idMensaje, bien ? $"✅ {H(a.Nombre)} apagado." : $"⚠️ No se pudo apagar {H(a.Nombre)}: {H(mensajeCasa)}", null, ct);
                    await Contestar(cb, bien ? "Apagado" : null, ct);
                    return;
                }
            case "cv":
                await Editar(chat, idMensaje, "👍 Vale, anotado.", null, ct);
                await Contestar(cb, null, ct);
                return;
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
    // ---- hábitos: ver lo de hoy y apuntar uno ----
    private async Task<string> HabitosDeHoy(CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).Date);
        var lista = (await Habitos.ServicioHabitos.Resumen(db, hoy, false, ct)).Where(h => h.TocaHoy).ToList();
        if (lista.Count == 0) return "No tienes hábitos para hoy. Crea alguno en la app (Hábitos).";
        var sb = new StringBuilder("<b>Hábitos de hoy</b>\n");
        foreach (var h in lista)
        {
            sb.Append(h.Hecho ? "✅ " : "⬜ ").Append(H(h.Nombre));
            if (h.Objetivo > 1) sb.Append($" {h.Hoy}/{h.Objetivo}");
            if (h.Actual > 1) sb.Append($" · racha {h.Actual}");
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    // «/hecho meditar»: suma uno (o lo completa si el objetivo es 1) al hábito cuyo nombre contenga lo escrito
    private async Task<string> Hecho(string nombre, CancellationToken ct)
    {
        if (nombre.Length == 0) return "Dime cuál: <i>/hecho meditar</i>. Mira /habitos.";
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).Date);
        var candidatos = (await db.Habitos.Where(h => !h.Archivado).ToListAsync(ct)).Where(h => h.Nombre.Contains(nombre, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidatos.Count == 0) return $"No encuentro ningún hábito con «{H(nombre)}». Mira /habitos.";
        if (candidatos.Count > 1) return "¿Cuál? " + string.Join(", ", candidatos.Select(h => $"<i>{H(h.Nombre)}</i>"));
        var h = candidatos[0];
        var actual = await db.RegistrosHabito.Where(r => r.HabitoId == h.Id && r.Fecha == hoy).Select(r => (int?)r.Cantidad).FirstOrDefaultAsync(ct) ?? 0;
        var nueva = h.Objetivo == 1 ? 1 : actual + 1;
        await Habitos.ServicioHabitos.Marcar(db, h.Id, hoy, nueva, ct);
        var regs = await db.RegistrosHabito.AsNoTracking().Where(r => r.HabitoId == h.Id && r.Fecha >= hoy.AddDays(-400)).ToDictionaryAsync(r => r.Fecha, r => r.Cantidad, ct);
        var racha = Habitos.Rachas.Calcular(h.Dias, h.Objetivo, regs, Habitos.ServicioHabitos.Inicio(h, regs, hoy), hoy).Actual;
        var progreso = h.Objetivo > 1 ? $" ({nueva}/{h.Objetivo}{(string.IsNullOrEmpty(h.Unidad) ? "" : " " + H(h.Unidad))})" : "";
        return $"✅ <b>{H(h.Nombre)}</b>{progreso}{(nueva >= h.Objetivo && racha > 1 ? $" · racha de {racha} días" : "")}";
    }

    // ---- tareas por hacer: verlas, añadir una y cerrarla ----
    private async Task<string> ListaTareas(CancellationToken ct)
    {
        var lista = await Tareas.ServicioTareas.Lista(db, Tareas.ServicioTareas.Hoy(reloj, zona), false, ct);
        if (lista.Count == 0) return "No tienes tareas pendientes. 🎉 Añade una con <i>/tarea comprar pan</i>.";
        var sb = new StringBuilder("<b>Tareas por hacer</b>\n");
        foreach (var t in lista.Take(15))
        {
            sb.Append(t.Vencida ? "🔴 " : t.Hoy ? "🟠 " : t.Prioridad == 2 ? "⭐ " : "⬜ ").Append(H(t.Titulo));
            if (t.Vence is not null) sb.Append(t.Vencida ? $" (venció hace {-t.Dias} d)" : t.Hoy ? " (hoy)" : $" ({t.Vence[8..]}/{t.Vence[5..7]})");
            sb.Append($" · /listo {t.Id}\n");
        }
        if (lista.Count > 15) sb.Append($"…y {lista.Count - 15} más en la app.");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> NuevaTarea(string titulo, CancellationToken ct)
    {
        if (titulo.Length == 0) return "Dime qué: <i>/tarea llamar al seguro</i>.";
        var e = new Tareas.TareaEntrada(titulo, null, null, null, null);
        if (Tareas.ServicioTareas.Validar(e, true) is { Count: > 0 } mal) return H(mal.Values.First()[0]);
        var t = new Tareas.Tarea { Titulo = titulo.Trim(), Creada = reloj.GetUtcNow() };
        db.Tareas.Add(t);
        await db.SaveChangesAsync(ct);
        return $"📝 Apuntada: <b>{H(t.Titulo)}</b> (/listo {t.Id} cuando la hagas).";
    }

    // «/listo 12» o «/listo pan»: por número o por un trozo del título
    private async Task<string> TareaHecha(string que, CancellationToken ct)
    {
        if (que.Length == 0) return "Dime cuál: <i>/listo 12</i> o <i>/listo pan</i>. Mira /tareas.";
        Tareas.Tarea? t;
        if (int.TryParse(que, out var id)) t = await db.Tareas.FirstOrDefaultAsync(x => x.Id == id && !x.Hecha, ct);
        else
        {
            var c = (await db.Tareas.Where(x => !x.Hecha).ToListAsync(ct)).Where(x => x.Titulo.Contains(que, StringComparison.OrdinalIgnoreCase)).ToList();
            if (c.Count > 1) return "¿Cuál? " + string.Join(", ", c.Take(6).Select(x => $"<i>{H(x.Titulo)}</i> (/listo {x.Id})"));
            t = c.FirstOrDefault();
        }
        if (t is null) return $"No encuentro esa tarea pendiente. Mira /tareas.";
        await Tareas.ServicioTareas.Completar(db, t.Id, true, reloj, zona, ct);
        return $"✅ Hecha: <b>{H(t.Titulo)}</b>{(t.Repite.Length > 0 ? " (la próxima ya está apuntada)" : "")}";
    }

    // ---- la compra: ver la lista (con lo que ya toca) o añadir varias cosas separadas por comas ----
    private async Task<string> Compras(string anadir, CancellationToken ct)
    {
        var hoy = Tareas.ServicioTareas.Hoy(reloj, zona);
        if (anadir.Length > 0)
        {
            foreach (var n in anadir.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(20))
                if (await Compra.ServicioCompra.Añadir(db, n, null, reloj, ct) is null) return "No pude añadirlo (nombre vacío o de más de 60 letras, o la lista está llena).";
        }
        var pendientes = await db.ArticulosCompra.AsNoTracking().Where(a => !a.Comprado).OrderBy(a => a.Id).ToListAsync(ct);
        var enLista = pendientes.Select(a => a.Clave).ToHashSet();
        var toca = (await db.ProductosHabituales.AsNoTracking().ToListAsync(ct)).Where(p => !enLista.Contains(p.Clave) && Compra.ServicioCompra.Toca(p, hoy)).Take(6).ToList();
        if (pendientes.Count == 0 && toca.Count == 0) return "La lista de la compra está vacía. Añade con <i>/compra leche, pan</i>.";
        var sb = new StringBuilder("<b>Compra</b>\n");
        foreach (var a in pendientes) sb.Append("⬜ ").Append(H(a.Nombre)).Append(a.Cantidad.Length > 0 ? $" ({H(a.Cantidad)})" : "").Append('\n');
        if (toca.Count > 0) sb.Append("\n💡 Ya toca: ").Append(string.Join(", ", toca.Select(p => H(p.Nombre)))).Append(" (añádelo con /compra)");
        sb.Append("\n\nCuando lo tengas: <i>/comprado leche</i>");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> Comprado(string que, CancellationToken ct)
    {
        if (que.Length == 0) return "Dime qué: <i>/comprado leche</i> (o varios: <i>/comprado leche, pan</i>). Mira /compra.";
        var hoy = Tareas.ServicioTareas.Hoy(reloj, zona);
        var hechos = new List<string>(); var noEstan = new List<string>();
        foreach (var n in que.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(20))
        {
            var clave = Compra.ServicioCompra.Clave(n);
            var a = await db.ArticulosCompra.FirstOrDefaultAsync(x => !x.Comprado && x.Clave == clave, ct)
                ?? (await db.ArticulosCompra.Where(x => !x.Comprado).ToListAsync(ct)).FirstOrDefault(x => x.Clave.Contains(clave));
            if (a is null) { noEstan.Add(n); continue; }
            await Compra.ServicioCompra.Marcar(db, a.Id, true, hoy, reloj, ct);
            hechos.Add(a.Nombre);
        }
        var partes = new List<string>();
        if (hechos.Count > 0) partes.Add("✅ Comprado: " + string.Join(", ", hechos.Select(H)));
        if (noEstan.Count > 0) partes.Add("No está en la lista: " + string.Join(", ", noEstan.Select(H)));
        return string.Join("\n", partes);
    }

    // ---- papeles que caducan: lo próximo a vencer ----
    private async Task<string> Caducidades(CancellationToken ct)
    {
        var hoy = Tareas.ServicioTareas.Hoy(reloj, zona);
        var lista = (await Papeles.ServicioPapeles.Lista(db, hoy, ct)).Take(10).ToList();
        if (lista.Count == 0) return "No tienes papeles apuntados. Añade el DNI, la ITV, el seguro… en la app (Papeles).";
        var sb = new StringBuilder("<b>Papeles y caducidades</b>\n");
        foreach (var p in lista)
        {
            sb.Append(p.Estado == "caducado" ? "🔴 " : p.Estado == "pronto" ? "🟠 " : "🟢 ").Append(H(p.Nombre)).Append(" · ");
            sb.Append(p.Dias < 0 ? $"caducó hace {-p.Dias} d" : p.Dias == 0 ? "caduca hoy" : $"en {p.Dias} d").Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

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
