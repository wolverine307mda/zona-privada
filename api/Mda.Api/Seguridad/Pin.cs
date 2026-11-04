using System.Text.RegularExpressions;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

public record PinEntrada(string? Pin);
public record PinNuevo(string? Nuevo);

// PIN de cuatro cifras para Documentos (y la galería de «tu carpeta de imágenes» y la lista de señuelos): una capa más sobre la sesión.
// Solo se guarda su huella (PBKDF2 de Identity). Se pide al entrar en Documentos y vale 10 minutos desde el último uso. Cinco fallos
// bloquean esa sesión 15 minutos y avisan por Telegram. Sin PIN configurado, no se pide nada.
public static class Pin
{
    public const string ClaveHash = "pin-hash";
    public static readonly TimeSpan Vida = TimeSpan.FromMinutes(10), Bloqueo = TimeSpan.FromMinutes(15);
    public const int MaxFallos = 5;
    private static readonly Regex Cuatro = new(@"^\d{4}$", RegexOptions.Compiled);

    public static bool Valido(string? pin) => pin is not null && Cuatro.IsMatch(pin);

    public static async Task<bool> Requerido(AppDbContext db) =>
        await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveHash) is { Valor.Length: > 0 };

    // al primer arranque, si el .env trae un PIN inicial (Documentos__PinInicial) y aún no hay ninguno, se guarda su huella
    public static async Task Sembrar(AppDbContext db, IConfiguration config, IPasswordHasher<IdentityUser> hasher, ILogger log, CancellationToken ct)
    {
        if (config["Documentos:PinInicial"] is not { } inicial || !Valido(inicial.Trim())) return;
        if (await Requerido(db)) return;
        await Guardar(db, hasher, inicial.Trim(), ct);
        log.LogInformation("PIN de Documentos creado a partir de Documentos:PinInicial (ya se puede quitar del .env y cambiar desde Seguridad)");
    }

    public static async Task Guardar(AppDbContext db, IPasswordHasher<IdentityUser> hasher, string pin, CancellationToken ct = default)
    {
        var a = await db.Ajustes.FindAsync([ClaveHash], ct);
        if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = ClaveHash });
        a.Valor = hasher.HashPassword(new IdentityUser(), pin);
        await db.SaveChangesAsync(ct);
    }

    // ¿esta sesión ha puesto el PIN hace poco? Cada uso renueva los 10 minutos (sin escribir en la base en cada petición)
    public static async Task<bool> Vigente(HttpContext ctx, AppDbContext db, TimeProvider reloj)
    {
        if (!await Requerido(db)) return true;
        var sid = Sesiones.Actual(ctx.User);
        if (sid is null || await db.Sesiones.FindAsync(sid) is not { } s) return false;
        var ahora = reloj.GetUtcNow();
        if (s.PinHasta is not { } h || h <= ahora) return false;
        if (h - ahora < Vida - TimeSpan.FromMinutes(1)) { s.PinHasta = ahora + Vida; await db.SaveChangesAsync(); }
        return true;
    }

    // 403 con «pin» para que la app enseñe el teclado y repita la petición
    public static IResult Pedir() => Results.Json(new { pin = true, detalle = "Escribe el PIN para entrar aquí." }, statusCode: StatusCodes.Status403Forbidden);

    public static void MapPin(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/seguridad/pin").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, HttpContext ctx, TimeProvider reloj) =>
        {
            var sid = Sesiones.Actual(ctx.User);
            var s = sid is null ? null : await db.Sesiones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid);
            var ahora = reloj.GetUtcNow();
            return Results.Ok(new
            {
                requerido = await Requerido(db),
                vigente = await Vigente(ctx, db, reloj),
                bloqueadoHasta = s?.PinBloqueadoHasta is { } b && b > ahora ? b : (DateTimeOffset?)null,
            });
        });

        g.MapPost("/", async (PinEntrada e, AppDbContext db, IPasswordHasher<IdentityUser> hasher, IAvisos avisos, HttpContext ctx, TimeProvider reloj) =>
        {
            var sid = Sesiones.Actual(ctx.User);
            if (sid is null || await db.Sesiones.FindAsync(sid) is not { } s) return Results.Unauthorized();
            var ahora = reloj.GetUtcNow();
            if (s.PinBloqueadoHasta is { } b && b > ahora)
                return Results.Json(new { detalle = $"Demasiados intentos: espera {(int)Math.Ceiling((b - ahora).TotalMinutes)} minutos.", bloqueadoHasta = b }, statusCode: StatusCodes.Status429TooManyRequests);
            if (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveHash) is not { Valor.Length: > 0 } hash) return Results.NoContent(); // sin PIN no hay nada que pedir

            var bien = Valido(e.Pin) && hasher.VerifyHashedPassword(new IdentityUser(), hash.Valor, e.Pin!) != PasswordVerificationResult.Failed;
            if (bien)
            {
                s.PinFallos = 0; s.PinBloqueadoHasta = null; s.PinHasta = ahora + Vida;
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
            s.PinFallos++;
            var bloquear = s.PinFallos >= MaxFallos;
            if (bloquear) { s.PinBloqueadoHasta = ahora + Bloqueo; s.PinFallos = 0; }
            await db.SaveChangesAsync();
            if (bloquear) await avisos.PorTelegram($"🔒 PIN de Documentos: {MaxFallos} fallos seguidos desde {IpCliente.De(ctx)}. Esa sesión queda bloqueada {(int)Bloqueo.TotalMinutes} minutos.");
            return Results.Json(new { detalle = bloquear ? $"Demasiados intentos: espera {(int)Bloqueo.TotalMinutes} minutos." : "PIN incorrecto.", restantes = Math.Max(0, MaxFallos - s.PinFallos), bloqueadoHasta = s.PinBloqueadoHasta },
                statusCode: bloquear ? StatusCodes.Status429TooManyRequests : StatusCodes.Status401Unauthorized);
        }).RequireRateLimiting(Limites.Reauth);

        // salir de Documentos: el PIN se vuelve a pedir la próxima vez
        g.MapDelete("/", async (AppDbContext db, HttpContext ctx) =>
        {
            if (Sesiones.Actual(ctx.User) is { } sid && await db.Sesiones.FindAsync(sid) is { } s) { s.PinHasta = null; await db.SaveChangesAsync(); }
            return Results.NoContent();
        });

        // cambiar el PIN pide confirmar con Face ID o contraseña (una cookie robada no puede cambiarlo)
        g.MapPut("/", async (PinNuevo e, AppDbContext db, IPasswordHasher<IdentityUser> hasher, HttpContext ctx, TimeProvider reloj) =>
        {
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            if (!Valido(e.Nuevo)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nuevo"] = ["Son cuatro cifras."] });
            await Guardar(db, hasher, e.Nuevo!);
            return Results.NoContent();
        });
    }
}

// Se pone en un grupo o una ruta: sin el PIN puesto en esta sesión, 403 «pin». Se salta «raíces» porque Inicio solo cuenta carpetas.
public class PinFiltro : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
    {
        var ctx = c.HttpContext;
        if (ctx.Request.Path.Value?.EndsWith("/raices", StringComparison.OrdinalIgnoreCase) == true) return await next(c);
        if (!await Pin.Vigente(ctx, ctx.RequestServices.GetRequiredService<AppDbContext>(), ctx.RequestServices.GetRequiredService<TimeProvider>()))
            return Pin.Pedir();
        return await next(c);
    }
}
