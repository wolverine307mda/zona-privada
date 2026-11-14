using Mda.Api.Data;
using Mda.Api.Pc;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Servidor;

// Lo del servidor que se maneja desde Telegram: comandos (/guardia, /enciende, /runbooks, /ejecuta) y los botones de las alertas de la guardia,
// las peticiones de entrada y los runbooks. La Conversacion solo reparte; aquí está lo que hace cada cosa.
public static class TelegramServidor
{
    public static readonly string[] Comandos = ["/guardia", "/enciende", "/despertar", "/runbooks", "/ejecuta"];

    private static string H(string s) => System.Net.WebUtility.HtmlEncode(s);

    // lo que se contesta a un comando (HTML) y, si toca, sus botones
    public static async Task<(string Html, object? Teclado)> Comando(string comando, string argumentos, AppDbContext db, GuardiaServicio guardia, RunbookRunner runner,
        EstadoAgente agente, TimeProvider reloj, CancellationToken ct)
    {
        switch (comando)
        {
            case "/guardia":
                {
                    var arg = argumentos.Trim().ToLowerInvariant();
                    if (arg is "on" or "off" or "si" or "no") await GuardiaServicio.Poner(db, reloj, arg is "on" or "si", "Telegram", ct);
                    var activa = await GuardiaServicio.Activa(db, ct);
                    return ($"🛡️ Modo guardia: <b>{(activa ? "ACTIVADO" : "apagado")}</b>.\n" + (activa
                        ? "Si una vigilancia da la alarma te propondré qué hacer con botones."
                        : "Las alarmas siguen llegando como siempre; con la guardia, además, llevan botones para actuar."),
                        new { inline_keyboard = new[] { new[] { new { text = activa ? "Apagar la guardia" : "Activar la guardia", callback_data = activa ? "gd:off" : "gd:on" } } } });
                }
            case "/enciende" or "/despertar":
                {
                    var d = await PcPrincipal.Leer(db, ct);
                    if (d is null) return ("Aún no has elegido tu PC principal: hazlo en la app, en Red › «Es mi PC principal».", null);
                    var (_, mensaje) = await PcPrincipal.Despertar(d, db, agente, reloj, "Telegram", ct);
                    return ($"💻 {H(mensaje)}", null);
                }
            case "/runbooks":
                {
                    await Runbooks.Sembrar(db, ct);
                    var lista = await db.Runbooks.AsNoTracking().OrderBy(r => r.Nombre).Take(20).ToListAsync(ct);
                    if (lista.Count == 0) return ("No hay ningún runbook: créalos en la app, en Servidor › Runbooks.", null);
                    return ("📋 <b>Runbooks</b>: pulsa uno para ejecutarlo.\n" + string.Join("\n", lista.Select(r => $"• <b>{H(r.Nombre)}</b>{(r.Descripcion.Length > 0 ? " — " + H(r.Descripcion) : "")}")),
                        new { inline_keyboard = lista.Select(r => new object[] { new { text = $"▶️ {r.Nombre}", callback_data = $"rb:run:{r.Id}" } }).ToArray() });
                }
            case "/ejecuta":
                {
                    var q = argumentos.Trim();
                    if (q.Length == 0) return ("Dime cuál: <i>/ejecuta comprobar todo</i>. Con /runbooks ves la lista.", null);
                    var todos = await db.Runbooks.AsNoTracking().ToListAsync(ct);
                    var r = todos.FirstOrDefault(x => string.Equals(x.Nombre, q, StringComparison.OrdinalIgnoreCase)) ?? todos.FirstOrDefault(x => x.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase));
                    if (r is null) return ($"No encuentro ningún runbook como «{H(q)}». Con /runbooks ves la lista.", null);
                    return (await runner.Lanzar(r, "telegram") is null ? $"«{H(r.Nombre)}» ya se está ejecutando." : $"▶️ Lanzado «{H(r.Nombre)}». Te aviso cuando termine.", null);
                }
        }
        return ("", null);
    }

    // lo que hace un botón: (texto con el que se sustituye el mensaje, aviso corto) o null si no es de aquí
    public static async Task<(string Html, string? Aviso)?> Boton(string accion, string clave, AppDbContext db, GuardiaServicio guardia, RunbookRunner runner,
        IMemoryCache cache, TimeProvider reloj, CancellationToken ct)
    {
        switch (accion)
        {
            case "lg":
                return await Aprobaciones.Denegar(db, reloj, clave, "desde Telegram", ct)
                    ? ("🚫 Petición de entrada denegada. Si no fuiste tú, cambia la contraseña y mira Seguridad › Sesiones.", "Denegada")
                    : ("Esa petición ya no estaba pendiente.", null);
            case "gd":
                {
                    var partes = clave.Split(':', 2);
                    switch (partes[0])
                    {
                        case "sil":
                            guardia.Silenciar(partes.ElementAtOrDefault(1) ?? "");
                            return ("🔕 Silenciado una hora: no volveré a proponerte acciones para esto hasta entonces.", "Silenciado");
                        case "ok": return ("👍 Ignorado.", null);
                        case "on" or "off":
                            await GuardiaServicio.Poner(db, reloj, partes[0] == "on", "Telegram", ct);
                            return (partes[0] == "on" ? "🛡️ Modo guardia activado." : "🛡️ Modo guardia apagado.", null);
                        case "panico":
                            await ServidorLog.Anotar(db, reloj, "guardia", "Pánico pulsado desde la guardia", "Telegram", "grave", ct);
                            return ("🚨 " + H(Panico.Resumen(await Panico.Ejecutar(db, reloj, cache, ct))) + "\nPara volver a entrar antes, escribe /desbloquear.", "Pánico");
                    }
                    return null;
                }
            case "rb":
                {
                    var partes = clave.Split(':', 2);
                    if (partes[0] == "run" && int.TryParse(partes.ElementAtOrDefault(1), out var id))
                    {
                        if (await db.Runbooks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct) is not { } r) return ("Ese runbook ya no existe.", null);
                        return await runner.Lanzar(r, "telegram") is null ? ($"«{H(r.Nombre)}» ya se está ejecutando.", null) : ($"▶️ Lanzado «{H(r.Nombre)}». Te aviso cuando termine.", "Lanzado");
                    }
                    if (partes[0] == "no" && int.TryParse(partes.ElementAtOrDefault(1), out var ej))
                        return runner.Cancelar(ej) ? ("🛑 Runbook cancelado.", "Cancelado") : ("Ese runbook ya había terminado.", null);
                    return null;
                }
        }
        return null;
    }
}
