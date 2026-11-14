using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Servidor;

public record GuardiaEntrada(bool Activa);

// Modo guardia: mientras está activo, cuando una vigilancia da la alarma (la API da errores, la dirección pública deja de responder…) el bot no solo te lo cuenta:
// te propone qué hacer con botones. «Ejecutar runbook» (los marcados como de guardia), «Silenciar 1 h», «Ignorar» y «Pánico». Un toque y hecho.
// Apagada, las alarmas siguen saliendo como siempre (solo texto): la guardia solo añade los botones.
public sealed class GuardiaServicio(IServiceScopeFactory scopes, Bot bot, IMemoryCache cache, TimeProvider reloj, ILogger<GuardiaServicio> log)
{
    public const string Clave = "guardia";
    public static readonly TimeSpan Silencio = TimeSpan.FromHours(1);
    public const int MaxRunbooks = 3;

    public static async Task<bool> Activa(AppDbContext db, CancellationToken ct = default) =>
        (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor == "si";

    public static async Task Poner(AppDbContext db, TimeProvider reloj, bool activa, string desde, CancellationToken ct = default)
    {
        var a = await db.Ajustes.FindAsync([Clave], ct);
        var antes = a?.Valor == "si";
        if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = Clave });
        a.Valor = activa ? "si" : "no";
        await db.SaveChangesAsync(ct);
        if (antes != activa) await ServidorLog.Anotar(db, reloj, "guardia", activa ? "Modo guardia activado" : "Modo guardia desactivado", desde, ct: ct);
    }

    public void Silenciar(string clave) => cache.Set($"guardia-silencio:{clave}", true, Silencio);
    public bool Silenciada(string clave) => cache.TryGetValue($"guardia-silencio:{clave}", out _);

    // los botones de un aviso de guardia (pura: se prueba sin Telegram)
    public static object Teclado(string clave, IEnumerable<(int Id, string Nombre)> runbooks)
    {
        var filas = new List<object[]>();
        foreach (var r in runbooks.Take(MaxRunbooks)) filas.Add([new { text = $"▶️ {r.Nombre}", callback_data = $"rb:run:{r.Id}" }]);
        filas.Add([new { text = "🔕 Silenciar 1 h", callback_data = $"gd:sil:{clave}" }, new { text = "👍 Ignorar", callback_data = "gd:ok" }]);
        filas.Add([new { text = "🚨 Pánico", callback_data = "gd:panico" }]);
        return new { inline_keyboard = filas };
    }

    // una alarma: se apunta siempre; con la guardia activa (y sin silencio) llega con botones. true si se mandó el mensaje.
    public async Task<bool> Proponer(string clave, string titulo, string detalle, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ServidorLog.Anotar(db, reloj, "alerta", titulo, detalle, "grave", ct);
            if (!await Activa(db, ct) || Silenciada(clave) || !bot.Vinculado) return false;
            var runbooks = (await db.Runbooks.AsNoTracking().Where(r => r.Guardia).OrderBy(r => r.Nombre).Take(MaxRunbooks).ToListAsync(ct)).Select(r => (r.Id, r.Nombre));
            static string h(string t) => System.Net.WebUtility.HtmlEncode(t);
            await ServidorLog.Anotar(db, reloj, "guardia", $"La guardia propone acciones: {titulo}", "", "aviso", ct);
            return await bot.Enviar($"🛡️ <b>Guardia</b> · {h(titulo)}\n{h(detalle)}\n\n¿Qué hago?", Teclado(clave, runbooks), html: true, ct: ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "La guardia no pudo proponer acciones para {Clave}", clave);
            return false;
        }
    }
}

public static class GuardiaEndpoints
{
    public static void MapGuardia(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/servidor/guardia").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, Bot bot, CancellationToken ct) =>
            Results.Ok(new { activa = await GuardiaServicio.Activa(db, ct), telegram = bot.Vinculado, runbooks = await db.Runbooks.CountAsync(r => r.Guardia, ct) }));

        g.MapPut("/", async (GuardiaEntrada e, AppDbContext db, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            await GuardiaServicio.Poner(db, reloj, e.Activa, "App · " + IpCliente.De(ctx), ct);
            return Results.NoContent();
        });

        // un aviso de prueba, para ver los botones sin esperar a que algo falle
        g.MapPost("/probar", async (GuardiaServicio guardia, Bot bot, AppDbContext db, CancellationToken ct) =>
        {
            if (!bot.Vinculado) return Results.Conflict(new { mensaje = "Telegram no está vinculado (Seguridad › Avisos)." });
            if (!await GuardiaServicio.Activa(db, ct)) return Results.Conflict(new { mensaje = "Activa primero el modo guardia." });
            return await guardia.Proponer("prueba", "Prueba de la guardia", "Esto es un simulacro: nada ha fallado.", ct)
                ? Results.Ok(new { mensaje = "Te he mandado el aviso de prueba a Telegram." }) : Results.Conflict(new { mensaje = "No se pudo mandar (¿está silenciada «prueba»? espera una hora o pulsa de nuevo)." });
        });
    }
}
