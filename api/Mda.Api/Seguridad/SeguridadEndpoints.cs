using System.Security.Claims;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

public record Latido(bool App);
public record AjustesSeguridad(bool AvisarInicios, bool? ResumenSemanal = null);

// Seguridad: sesiones abiertas (con quién está en línea ahora), historial de accesos, avisos y verificación en dos pasos
public static class SeguridadEndpoints
{
    public static void MapSeguridad(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/seguridad").RequireAuthorization();

        g.MapGet("/sesiones", async (ClaimsPrincipal user, AppDbContext db, TimeProvider reloj) =>
        {
            var ahora = reloj.GetUtcNow();
            // la cookie caduca tras 8 horas sin uso: las más viejas ya no valen aunque no se cerraran
            var limite = ahora.AddHours(-8);
            var actual = Sesiones.Actual(user);
            var lista = await db.Sesiones.AsNoTracking().Where(s => s.Cerrada == null && s.UltimaVez > limite).ToListAsync();
            return Results.Ok(lista.OrderByDescending(s => s.Id == actual).ThenByDescending(s => s.UltimaVez).Select(s => new
            {
                s.Id, s.Creada, s.UltimaVez, s.Ip, s.App,
                Dispositivo = Dispositivo.Sistema(s.Agente), Navegador = Dispositivo.Navegador(s.Agente), Tipo = Dispositivo.Tipo(s.Agente),
                EnLinea = ahora - s.UltimaVez < Sesiones.EnLinea, Actual = s.Id == actual,
            }));
        });

        // la pestaña abierta avisa cada minuto de que sigue ahí (para el «en línea»)
        g.MapPost("/latido", async (Latido l, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (Sesiones.Actual(user) is { } sid && await db.Sesiones.FindAsync(sid) is { } s)
            {
                s.UltimaVez = DateTimeOffset.UtcNow;
                s.App = l.App;
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        g.MapDelete("/sesiones/{id}", async (string id, AppDbContext db) =>
        {
            var s = await db.Sesiones.FindAsync(id);
            if (s is null) return Results.NotFound();
            s.Cerrada = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // cerrar todas menos esta
        g.MapDelete("/sesiones", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var actual = Sesiones.Actual(user);
            var otras = await db.Sesiones.Where(s => s.Cerrada == null && s.Id != actual).ToListAsync();
            otras.ForEach(s => s.Cerrada = DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            return Results.Ok(new { cerradas = otras.Count });
        });

        g.MapGet("/accesos", async (AppDbContext db) =>
            Results.Ok((await db.Accesos.AsNoTracking().OrderByDescending(a => a.Id).Take(40).ToListAsync())
                .Select(a => new { a.Fecha, a.Ip, a.Exito, a.Motivo, Dispositivo = Dispositivo.Nombre(a.Agente), Tipo = Dispositivo.Tipo(a.Agente) })));

        g.MapGet("/estado", async (ClaimsPrincipal user, UserManager<IdentityUser> users, AppDbContext db, IAvisos avisos, Vigilante vigilante, TimeProvider reloj) =>
        {
            var u = await users.GetUserAsync(user);
            var desde = reloj.GetUtcNow().AddDays(-30);
            return Results.Ok(new
            {
                dosPasos = u is not null && await users.GetTwoFactorEnabledAsync(u),
                canales = avisos.Disponibles(),
                avisarInicios = await db.Ajustes.AnyAsync(a => a.Clave == Sesiones.AvisarInicios && a.Valor == "si"),
                fallosMes = await db.Accesos.CountAsync(a => !a.Exito && a.Fecha > desde),
                resumenSemanal = !await db.Ajustes.AnyAsync(a => a.Clave == Vigilante.ResumenSemanal && a.Valor == "no"),
                dispositivosPush = await db.Push.CountAsync(),
                copia = vigilante.UltimaCopia(),
            });
        });

        g.MapPut("/ajustes", async (AjustesSeguridad a, AppDbContext db) =>
        {
            var aj = await db.Ajustes.FindAsync(Sesiones.AvisarInicios) ?? db.Ajustes.Add(new Ajuste { Clave = Sesiones.AvisarInicios }).Entity;
            aj.Valor = a.AvisarInicios ? "si" : "no";
            if (a.ResumenSemanal is { } rs)
            {
                var r = await db.Ajustes.FindAsync(Vigilante.ResumenSemanal) ?? db.Ajustes.Add(new Ajuste { Clave = Vigilante.ResumenSemanal }).Entity;
                r.Valor = rs ? "si" : "no";
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // manda un aviso de prueba por el canal que se pida
        g.MapPost("/avisos/prueba/{canal}", async (string canal, IAvisos avisos) =>
        {
            const string texto = "👋 Prueba de avisos de tu zona privada: si te llega esto, los recordatorios funcionan.";
            var ok = canal switch
            {
                "correo" => await avisos.PorCorreo("Prueba de avisos", texto),
                "whatsapp" => await avisos.PorWhatsApp(texto),
                "push" => await avisos.PorPush("👋 Prueba de avisos", "Si ves esto, las notificaciones de la app funcionan en tus dispositivos.", "/seguridad"),
                _ => (bool?)null,
            };
            return ok is null ? Results.NotFound() : ok.Value ? Results.NoContent()
                : Results.Problem(canal == "push" ? "No hay ningún dispositivo con las notificaciones activadas." : "No se pudo enviar: revisa la configuración en el .env.", statusCode: 502);
        });
    }
}
