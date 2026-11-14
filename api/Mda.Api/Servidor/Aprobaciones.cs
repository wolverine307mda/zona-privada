using System.Security.Cryptography;
using System.Text;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Telegram;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Servidor;

public record SecretoEntrada(string? Secreto);
public record CodigoEntrada(string? Codigo);

// Entrar desde un ordenador nuevo sin escribir la contraseña: el ordenador pide la aprobación y enseña un código de dos cifras; a ti te llega un aviso
// (Telegram y notificación) y, en el iPhone, abres la app, tecleas ese código y confirmas con Face ID. Teclear el código impide aprobar a ciegas una petición
// que no has hecho tú. Es un permiso de un solo uso, caduca a los 3 minutos y se bloquea tras 3 códigos mal puestos.
public static class Aprobaciones
{
    public static readonly TimeSpan Validez = TimeSpan.FromMinutes(3);
    public const int MaxPendientes = 3, MaxIntentos = 3;

    public static string NuevoCodigo() => RandomNumberGenerator.GetInt32(10, 100).ToString();
    public static string Hash(string secreto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secreto))).ToLowerInvariant();

    public static bool SecretoValido(AprobacionLogin a, string? secreto) =>
        !string.IsNullOrEmpty(secreto) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a.SecretoHash), Encoding.ASCII.GetBytes(Hash(secreto)));

    // «pendiente» que ya pasó de hora es «caducada»
    public static string EstadoActual(AprobacionLogin a, DateTimeOffset ahora) => a.Estado == "pendiente" && a.Caduca <= ahora ? "caducada" : a.Estado;

    // da la aprobación si el código coincide; con el tercer fallo la petición se deniega. Cambia `a`.
    public static (bool Ok, string Error) Aprobar(AprobacionLogin a, string? codigo, DateTimeOffset ahora)
    {
        if (EstadoActual(a, ahora) != "pendiente") return (false, "Esa petición ya no está pendiente.");
        a.Intentos++;
        var puesto = (codigo ?? "").Trim();
        if (puesto.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a.Codigo), Encoding.ASCII.GetBytes(puesto)))
        {
            if (a.Intentos >= MaxIntentos) { a.Estado = "denegada"; return (false, "Código incorrecto tres veces: la petición se ha denegado."); }
            return (false, "Ese no es el código que enseña el otro ordenador.");
        }
        a.Estado = "aprobada";
        return (true, "");
    }

    public static async Task<bool> Denegar(AppDbContext db, TimeProvider reloj, string id, string quien, CancellationToken ct)
    {
        if (await db.Aprobaciones.FindAsync([id], ct) is not { } a || EstadoActual(a, reloj.GetUtcNow()) is not ("pendiente" or "aprobada")) return false;
        a.Estado = "denegada";
        await db.SaveChangesAsync(ct);
        await ServidorLog.Anotar(db, reloj, "aprobacion", "Entrada denegada", $"{quien} · {a.Ip}{(a.Lugar.Length > 0 ? " · " + a.Lugar : "")}", "aviso", ct);
        return true;
    }

    public static void MapAprobaciones(this IEndpointRouteBuilder app)
    {
        // ---- el ordenador que pide entrar (sin sesión) ----
        var e = app.MapGroup("/api/auth/aprobacion").AllowAnonymous();

        e.MapPost("/", async (AppDbContext db, Bot bot, IAvisos avisos, IConfiguration config, GeoIp geo, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            var ahora = reloj.GetUtcNow();
            if (!bot.Vinculado && !avisos.Disponibles().Push) return Results.Conflict(new { mensaje = "No hay forma de avisarte: vincula Telegram o activa las notificaciones en el iPhone (Seguridad › Avisos)." });
            // lo viejo se barre; y no más de tres pendientes a la vez (nadie puede llenarte el móvil de avisos)
            db.Aprobaciones.RemoveRange(await db.Aprobaciones.Where(a => a.Caduca < ahora.AddDays(-1)).ToListAsync(ct));
            if (await db.Aprobaciones.CountAsync(a => a.Estado == "pendiente" && a.Caduca > ahora, ct) >= MaxPendientes)
                return Results.Json(new { mensaje = "Ya hay peticiones pendientes: espera a que caduquen o deniégalas." }, statusCode: StatusCodes.Status429TooManyRequests);

            var secreto = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            var ip = IpCliente.De(ctx);
            var sitio = geo.Buscar(ip);
            var a = new AprobacionLogin
            {
                Id = Guid.NewGuid().ToString("N"), Codigo = NuevoCodigo(), SecretoHash = Hash(secreto), Creada = ahora, Caduca = ahora.Add(Validez), Ip = ip,
                Agente = ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 400)] : "desconocido",
                Lugar = sitio is null ? "" : sitio.Ciudad.Length > 0 ? $"{sitio.Ciudad}, {sitio.Pais}" : sitio.Pais,
            };
            db.Aprobaciones.Add(a);
            await db.SaveChangesAsync(ct);

            var quien = $"{Dispositivo.Nombre(a.Agente)}{(a.Lugar.Length > 0 ? " · " + a.Lugar : "")} · {a.Ip}";
            await ServidorLog.Anotar(db, reloj, "aprobacion", "Petición de entrada", quien, "aviso", ct);
            var url = config["App:UrlPublica"] is { Length: > 0 } u ? u.TrimEnd('/') + "/aprobar" : null;
            var botones = new List<object[]>();
            if (url is not null && url.StartsWith("https://", StringComparison.Ordinal)) botones.Add([new { text = "✅ Abrir y aprobar con Face ID", url }]);
            botones.Add([new { text = "🚫 No he sido yo", callback_data = $"lg:{a.Id}" }]);
            _ = bot.Vinculado ? bot.Enviar($"🔐 <b>Alguien quiere entrar</b>\n{System.Net.WebUtility.HtmlEncode(quien)}\n\nSi eres tú: abre la app en el iPhone, escribe el código que ves en ese ordenador y confirma con Face ID. Caduca en 3 minutos.", new { inline_keyboard = botones }, html: true, ct: ct) : Task.FromResult(false);
            _ = avisos.PorPush("Alguien quiere entrar", $"{quien}. Toca para aprobar con Face ID.", "/aprobar", ct);
            return Results.Ok(new { id = a.Id, codigo = a.Codigo, secreto, caduca = a.Caduca });
        }).RequireRateLimiting(Limites.Acceso);

        e.MapPost("/{id}/estado", async (string id, SecretoEntrada s, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
            await db.Aprobaciones.FindAsync([id], ct) is { } a && SecretoValido(a, s.Secreto) ? Results.Ok(new { estado = EstadoActual(a, reloj.GetUtcNow()) }) : Results.NotFound()).RequireRateLimiting(Limites.Sondeo);

        // recoger la sesión cuando está aprobada: una sola vez
        e.MapPost("/{id}/entrar", async (string id, SecretoEntrada s, AppDbContext db, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, GeoIp geo,
            IHubContext<Mda.Api.Sistema.SistemaHub> hub, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            if (await db.Aprobaciones.FindAsync([id], ct) is not { } a || !SecretoValido(a, s.Secreto)) return Results.NotFound();
            if (EstadoActual(a, reloj.GetUtcNow()) != "aprobada") return Results.Conflict(new { mensaje = "Esa petición no está aprobada." });
            var usuario = await users.Users.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (usuario is null) return Results.NotFound();
            if (await users.IsLockedOutAsync(usuario)) return Results.Problem("La entrada está bloqueada ahora mismo.", statusCode: StatusCodes.Status423Locked);
            a.Estado = "usada";
            var acceso = new Acceso { Ip = IpCliente.De(ctx), Agente = a.Agente, Exito = true, Motivo = "ok (aprobado desde el iPhone)" };
            db.Accesos.Add(acceso);
            await db.SaveChangesAsync(ct);
            await signIn.SignInAsync(usuario, isPersistent: false);
            await ServidorLog.Anotar(db, reloj, "aprobacion", "Entrada aprobada y usada", $"{Dispositivo.Nombre(a.Agente)} · {a.Ip}{(a.Lugar.Length > 0 ? " · " + a.Lugar : "")}", ct: ct);
            await AccesoVivo.Emitir(hub, geo, acceso);
            return Results.NoContent();
        }).RequireRateLimiting(Limites.Sondeo);

        // ---- el iPhone que aprueba (con sesión y Face ID) ----
        var g = app.MapGroup("/api/auth/aprobacion").RequireAuthorization();

        g.MapGet("/pendientes", async (AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            var ahora = reloj.GetUtcNow();
            var lista = await db.Aprobaciones.AsNoTracking().Where(a => a.Estado == "pendiente" && a.Caduca > ahora).OrderByDescending(a => a.Creada).ToListAsync(ct);
            return Results.Ok(lista.Select(a => new { a.Id, equipo = Dispositivo.Nombre(a.Agente), a.Ip, a.Lugar, a.Creada, a.Caduca }));
        });

        g.MapPost("/{id}/aprobar", async (string id, CodigoEntrada c, AppDbContext db, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            // lo delicado pide Face ID (o contraseña) justo antes: una cookie robada no basta para dejar entrar a otro
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            if (await db.Aprobaciones.FindAsync([id], ct) is not { } a) return Results.NotFound();
            var (ok, error) = Aprobar(a, c.Codigo, reloj.GetUtcNow());
            await db.SaveChangesAsync(ct);
            if (ok) await ServidorLog.Anotar(db, reloj, "aprobacion", "Entrada aprobada con Face ID", $"{Dispositivo.Nombre(a.Agente)} · {a.Ip}", ct: ct);
            else if (a.Estado == "denegada") await ServidorLog.Anotar(db, reloj, "aprobacion", "Petición denegada por códigos incorrectos", a.Ip, "aviso", ct);
            return ok ? Results.NoContent() : Results.Json(new { mensaje = error }, statusCode: StatusCodes.Status422UnprocessableEntity);
        });

        g.MapPost("/{id}/denegar", async (string id, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
            await Denegar(db, reloj, id, "desde la app", ct) ? Results.NoContent() : Results.NotFound());
    }
}
