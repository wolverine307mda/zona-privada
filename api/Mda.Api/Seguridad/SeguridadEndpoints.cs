using System.Security.Claims;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

public record Latido(bool App);
public record ConfianzaEquipo(bool Confianza);
public record AjustesSeguridad(bool AvisarInicios, bool? ResumenSemanal = null, bool? SoloNuevos = null);
public record PreferenciaAviso(string[]? Canales);

// Seguridad: sesiones abiertas (con quién está en línea ahora), historial de accesos, avisos y verificación en dos pasos
public static class SeguridadEndpoints
{
    private static string? Lugar(Geo? g) => g is null ? null : g.Ciudad.Length > 0 ? $"{g.Ciudad}, {g.Pais}" : g.Pais;

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

        g.MapGet("/accesos", async (AppDbContext db, GeoIp geo) =>
            Results.Ok((await db.Accesos.AsNoTracking().OrderByDescending(a => a.Id).Take(40).ToListAsync())
                .Select(a =>
                {
                    var sitio = geo.Buscar(a.Ip);   // también las coordenadas: el radar del globo dibuja de dónde llega cada intento
                    return new { a.Id, a.Fecha, a.Ip, a.Exito, a.Motivo, a.Revisado, Dispositivo = Dispositivo.Nombre(a.Agente), Tipo = Dispositivo.Tipo(a.Agente), Lugar = Lugar(sitio), Lat = sitio?.Lat, Lon = sitio?.Lon };
                })));

        // marcar un fallo como revisado (o quitarle la marca)
        g.MapPut("/accesos/{id:int}/revisado", async (int id, bool revisado, AppDbContext db) =>
        {
            var a = await db.Accesos.FindAsync(id);
            if (a is null || a.Exito) return Results.NotFound();
            a.Revisado = revisado;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        // todos los fallos pendientes de golpe
        g.MapPost("/accesos/revisar", async (AppDbContext db) =>
        {
            var pendientes = await db.Accesos.Where(a => !a.Exito && !a.Revisado).ToListAsync();
            pendientes.ForEach(a => a.Revisado = true);
            await db.SaveChangesAsync();
            return Results.Ok(new { revisados = pendientes.Count });
        });

        // los equipos desde los que has entrado: avisa solo de los que no estén aquí
        g.MapGet("/equipos", async (AppDbContext db, TimeProvider reloj) =>
        {
            var ahora = reloj.GetUtcNow();
            var enLinea = (await db.Sesiones.AsNoTracking().Where(s => s.Cerrada == null && s.UltimaVez > ahora - Sesiones.EnLinea).ToListAsync()).Select(s => (s.Ip, Nombre: Dispositivo.Nombre(s.Agente))).ToHashSet();
            return Results.Ok((await db.Equipos.AsNoTracking().OrderByDescending(e => e.Ultima).ToListAsync()).Select(e => new
            {
                e.Id, e.Nombre, e.Pais, e.Ciudad, e.UltimaIp, e.Primera, e.Ultima, e.Entradas, e.Confianza, e.Sospechoso,
                estado = EquiposConfianza.Estado(e.Ultima, enLinea.Contains((e.UltimaIp, e.Nombre)), ahora),
                visto = EquiposConfianza.Visto(e.Ultima, e.Pais, e.Ciudad, ahora),
            }));
        });
        g.MapPut("/equipos/{id:int}/confianza", async (int id, ConfianzaEquipo c, AppDbContext db) =>
        {
            var e = await db.Equipos.FindAsync(id);
            if (e is null) return Results.NotFound();
            e.Confianza = c.Confianza;
            if (c.Confianza) { e.Sospechoso = false; e.SospechosoDesde = null; }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        // olvidar un equipo: la próxima vez que entre será «nuevo» y avisará
        g.MapDelete("/equipos/{id:int}", async (int id, AppDbContext db) =>
        {
            var e = await db.Equipos.FindAsync(id);
            if (e is null) return Results.NotFound();
            db.Equipos.Remove(e);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapGet("/estado", async (ClaimsPrincipal user, UserManager<IdentityUser> users, AppDbContext db, IAvisos avisos, Vigilante vigilante, TimeProvider reloj) =>
        {
            var u = await users.GetUserAsync(user);
            var desde = reloj.GetUtcNow().AddDays(-30);
            return Results.Ok(new
            {
                dosPasos = u is not null && await users.GetTwoFactorEnabledAsync(u),
                canales = avisos.Disponibles(),
                avisarInicios = await db.Ajustes.AnyAsync(a => a.Clave == Sesiones.AvisarInicios && a.Valor == "si"),
                soloNuevos = !await db.Ajustes.AnyAsync(a => a.Clave == Sesiones.AvisarSoloNuevos && a.Valor == "no"),
                fallosMes = await db.Accesos.CountAsync(a => !a.Exito && !a.Revisado && a.Fecha > desde),
                resumenSemanal = !await db.Ajustes.AnyAsync(a => a.Clave == Vigilante.ResumenSemanal && a.Valor == "no"),
                dispositivosPush = await db.Push.CountAsync(),
                copia = vigilante.UltimaCopia(),
            });
        });

        g.MapPut("/ajustes", async (AjustesSeguridad a, AppDbContext db) =>
        {
            var aj = await db.Ajustes.FindAsync(Sesiones.AvisarInicios) ?? db.Ajustes.Add(new Ajuste { Clave = Sesiones.AvisarInicios }).Entity;
            aj.Valor = a.AvisarInicios ? "si" : "no";
            if (a.SoloNuevos is { } sn)
            {
                var n = await db.Ajustes.FindAsync(Sesiones.AvisarSoloNuevos) ?? db.Ajustes.Add(new Ajuste { Clave = Sesiones.AvisarSoloNuevos }).Entity;
                n.Valor = sn ? "si" : "no";
            }
            if (a.ResumenSemanal is { } rs)
            {
                var r = await db.Ajustes.FindAsync(Vigilante.ResumenSemanal) ?? db.Ajustes.Add(new Ajuste { Clave = Vigilante.ResumenSemanal }).Entity;
                r.Valor = rs ? "si" : "no";
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // qué avisos llegan por qué canal: cada clase de aviso con sus canales activos y a qué pantalla lleva al pulsarla
        g.MapGet("/avisos/preferencias", async (AppDbContext db, IAvisos avisos) =>
        {
            var guardado = await db.Ajustes.AsNoTracking().Where(a => a.Clave.StartsWith("avisos:")).ToDictionaryAsync(a => a.Clave, a => a.Valor);
            var d = avisos.Disponibles();
            return Results.Ok(new
            {
                canales = new[]
                {
                    new { id = TiposAviso.Push, nombre = "Notificaciones", disponible = d.Push },
                    new { id = TiposAviso.Telegram, nombre = "Telegram", disponible = d.Telegram },
                    new { id = TiposAviso.Correo, nombre = "Correo", disponible = d.Correo },
                    new { id = TiposAviso.WhatsApp, nombre = "WhatsApp", disponible = d.WhatsApp },
                },
                tipos = TiposAviso.Todos.Select(t => new
                {
                    t.Id, t.Nombre, t.Detalle, t.Ruta,
                    canales = TiposAviso.Interpretar(guardado.GetValueOrDefault(TiposAviso.Clave(t.Id)), t),
                }),
            });
        });

        g.MapPut("/avisos/preferencias/{tipo}", async (string tipo, PreferenciaAviso p, AppDbContext db) =>
        {
            if (TiposAviso.Buscar(tipo) is null) return Results.NotFound();
            var clave = TiposAviso.Clave(tipo);
            var fila = await db.Ajustes.FindAsync(clave) ?? db.Ajustes.Add(new Ajuste { Clave = clave }).Entity;
            fila.Valor = TiposAviso.Guardar(p.Canales ?? []);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // manda un aviso de prueba por el canal que se pida
        g.MapPost("/avisos/prueba/{canal}", async (string canal, IAvisos avisos) =>
        {
            const string texto = "👋 Prueba de avisos de tu zona privada: si te llega esto, los recordatorios funcionan.";
            var ok = canal switch
            {
                "correo" => await avisos.PorCorreo("👋 Prueba de avisos", texto, "/seguridad"),
                "whatsapp" => await avisos.PorWhatsApp(texto),
                "telegram" => await avisos.PorTelegram(texto),
                "push" => await avisos.PorPush("👋 Prueba de avisos", "Si ves esto, las notificaciones de la app funcionan en tus dispositivos.", "/seguridad"),
                _ => (bool?)null,
            };
            return ok is null ? Results.NotFound() : ok.Value ? Results.NoContent()
                : Results.Problem(canal == "push" ? "No hay ningún dispositivo con las notificaciones activadas." : "No se pudo enviar: revisa la configuración en el .env.", statusCode: 502);
        }).RequireRateLimiting(Limites.Sensible);
    }
}
