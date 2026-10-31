using Mda.Api.Auth;
using Mda.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace Mda.Api.Seguridad;

public record BloqueoEntrada(int? Segundos);
public record DesbloqueoClave(string Password, string? Codigo);

// «Pedir Face ID al volver a la app»: al salir de la app (o tras un rato sin oírse de ella) la sesión se BLOQUEA en el servidor
// y no contesta a nada, ni a los datos ni al tiempo real, hasta que se desbloquea con el passkey (o la contraseña). Así, quien
// coja el móvil con la app abierta no ve nada, y una cookie robada de ese móvil tampoco abre la sesión.
public static class BloqueoSesion
{
    // lo único que sigue contestando con la sesión bloqueada
    private static bool Permitida(PathString ruta) => ruta.StartsWithSegments("/api/sesion") || ruta.StartsWithSegments("/api/salud");

    public static IApplicationBuilder UseBloqueoSesion(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (ctx.Items[Sesiones.ClaveBloqueada] is true && (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/hubs")) && !Permitida(ctx.Request.Path))
        {
            ctx.Response.StatusCode = StatusCodes.Status423Locked;
            await ctx.Response.WriteAsJsonAsync(new { bloqueada = true });
            return;
        }
        await next();
    });

    private static async Task<SesionAbierta?> Actual(HttpContext ctx, AppDbContext db) =>
        Sesiones.Actual(ctx.User) is { } sid ? await db.Sesiones.FindAsync(sid) : null;

    public static void MapBloqueo(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sesion").RequireAuthorization();

        // activar o cambiar el bloqueo de ESTA sesión (este dispositivo). Quitarlo o aflojarlo pide la contraseña otra vez.
        g.MapPut("/bloqueo", async (BloqueoEntrada e, HttpContext ctx, AppDbContext db, TimeProvider reloj) =>
        {
            if (e.Segundos is { } s && (s < 5 || s > 3600)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["segundos"] = ["Entre 5 segundos y 1 hora."] });
            var sesion = await Actual(ctx, db);
            if (sesion is null) return Results.Unauthorized();
            if (sesion.Bloqueada) return Results.StatusCode(StatusCodes.Status423Locked);
            var afloja = sesion.BloquearTrasSegundos is { } actual && (e.Segundos is null || e.Segundos > actual);
            if (afloja && !await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            sesion.BloquearTrasSegundos = e.Segundos;
            sesion.SegundoPlano = null;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // la app avisa de que sale a segundo plano (sendBeacon al ocultarse)
        g.MapPost("/segundo-plano", async (HttpContext ctx, AppDbContext db, TimeProvider reloj) =>
        {
            var sesion = await Actual(ctx, db);
            if (sesion is { BloquearTrasSegundos: not null, Bloqueada: false })
            {
                sesion.SegundoPlano = reloj.GetUtcNow();
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        // desbloquear con el passkey: se pide un reto para esta cuenta y se comprueba la firma
        g.MapPost("/desbloqueo/opciones", async (ClaimsPrincipalAccessor yo, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            return u is null ? Results.Unauthorized() : Results.Content(await signIn.MakePasskeyRequestOptionsAsync(u), "application/json");
        }).RequireRateLimiting(Limites.Reauth);

        g.MapPost("/desbloqueo", async (CredencialEntrada c, HttpContext ctx, ClaimsPrincipalAccessor yo, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, AppDbContext db) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            var r = await signIn.PerformPasskeyAssertionAsync(c.Credencial.GetRawText());
            // tiene que ser un passkey de ESTA cuenta (no vale el de otra)
            if (!r.Succeeded || r.User is null || r.User.Id != u.Id) return Results.Problem("Ese passkey no es de esta cuenta.", statusCode: StatusCodes.Status401Unauthorized);
            await users.AddOrUpdatePasskeyAsync(r.User, r.Passkey);
            return await Desbloquear(ctx, db);
        }).RequireRateLimiting(Limites.Reauth);

        // respaldo: contraseña (y código de dos pasos)
        g.MapPost("/desbloqueo-clave", async (DesbloqueoClave e, HttpContext ctx, ClaimsPrincipalAccessor yo, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, AppDbContext db) =>
        {
            var u = await users.GetUserAsync(yo.Usuario);
            if (u is null) return Results.Unauthorized();
            if (await Reautenticar.Verificar(users, signIn, u, e.Password, e.Codigo) is { } error) return error;
            return await Desbloquear(ctx, db);
        }).RequireRateLimiting(Limites.Reauth);
    }

    private static async Task<IResult> Desbloquear(HttpContext ctx, AppDbContext db)
    {
        var sesion = await Actual(ctx, db);
        if (sesion is null) return Results.Unauthorized();
        sesion.Bloqueada = false;
        sesion.SegundoPlano = null;
        sesion.UltimaVez = DateTimeOffset.UtcNow; // la red de seguridad por silencio cuenta desde ahora
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
