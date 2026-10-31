using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Galeria;

public record AjustesPublicos(bool Estado, bool Cpu);

// «Estado en vivo» del portfolio: la web pública puede ver si el equipo está encendido y, si lo activas, su carga.
// Todo viene APAGADO por defecto y se decide en la app. Solo salen números redondeados: nunca el nombre del equipo,
// los discos, la IP, rutas ni procesos. Un único valor en memoria sirve a todos los visitantes (cero consultas por visita).
public static class EstadoPublico
{
    public const string ClaveEstado = "publico-estado";
    public const string ClaveCpu = "publico-cpu";
    private const int MaxConexiones = 20;               // visitantes con la conexión en directo abierta a la vez
    private static readonly TimeSpan MaxDuracion = TimeSpan.FromMinutes(10);
    private static int _conexiones;

    public static async Task<AjustesPublicos> Leer(AppDbContext db, CancellationToken ct)
    {
        var a = await db.Ajustes.AsNoTracking().Where(x => x.Clave == ClaveEstado || x.Clave == ClaveCpu).ToListAsync(ct);
        bool Si(string c) => a.Any(x => x.Clave == c && x.Valor == "si");
        return new AjustesPublicos(Si(ClaveEstado), Si(ClaveCpu));
    }

    // lo que ve la web: sin el interruptor general no hay nada; sin el de la carga, solo «en línea»
    // ¿es de día donde vive el equipo? (solo un sí/no: sirve para que el gemelo 3D tenga luz de día o de noche)
    internal static async Task<bool?> EsDeDia(Mda.Api.Tiempo.ServicioTiempo tiempo, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, TimeProvider reloj, CancellationToken ct)
    {
        var lugar = await cache.GetOrCreateAsync("estado:lugar", async e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5); return await tiempo.LugarGuardado(ct); });
        return lugar is null ? null : Mda.Api.Luz.Astro.AlturaSol(reloj.GetUtcNow(), lugar.Lat, lugar.Lon) > -0.833;
    }

    internal static object Foto(AjustesPublicos a, EstadoSistema estado, TimeProvider reloj, bool? dia = null)
    {
        var h = estado.Historial();
        var u = h.LastOrDefault();
        if (!a.Cpu || u is null) return new { enLinea = true, hora = reloj.GetUtcNow() };
        return new
        {
            enLinea = true,
            hora = reloj.GetUtcNow(),
            cpu = (int)Math.Round(u.Cpu),
            ram = u.MemoriaTotal > 0 ? (int)Math.Round(100.0 * u.MemoriaUsada / u.MemoriaTotal) : 0,
            encendidoMinutos = (long)(u.EncendidoSegundos / 60),
            serie = h.TakeLast(30).Select(m => (int)Math.Round(m.Cpu)).ToArray(),
            dia,
        };
    }

    public static void MapEstadoPublico(this IEndpointRouteBuilder app)
    {
        // ajustes (con sesión): qué puede ver el público
        var g = app.MapGroup("/api/publico-ajustes").RequireAuthorization();
        g.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(await Leer(db, ct)));
        g.MapPut("/", async (AjustesPublicos e, AppDbContext db, CancellationToken ct) =>
        {
            foreach (var (clave, valor) in new[] { (ClaveEstado, e.Estado), (ClaveCpu, e.Estado && e.Cpu) })
            {
                var a = await db.Ajustes.FindAsync([clave], ct);
                if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = clave });
                a.Valor = valor ? "si" : "no";
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var p = app.MapGroup("/api/publico/estado").AllowAnonymous().RequireCors(GaleriaEndpoints.WebPublica).RequireRateLimiting(Limites.Publico);

        // una lectura suelta (si la conexión en directo no funciona, la web pregunta cada pocos segundos)
        p.MapGet("/", async (AppDbContext db, EstadoSistema estado, TimeProvider reloj, Mda.Api.Tiempo.ServicioTiempo tiempo, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, CancellationToken ct) =>
        {
            var a = await Leer(db, ct);
            return a.Estado ? Results.Ok(Foto(a, estado, reloj, a.Cpu ? await EsDeDia(tiempo, cache, reloj, ct) : null)) : Results.NotFound();
        });

        // en directo (Server-Sent Events): una lectura cada 3 s; tope de visitantes y de duración
        p.MapGet("/vivo", async (HttpContext ctx, AppDbContext db, EstadoSistema estado, TimeProvider reloj, Mda.Api.Tiempo.ServicioTiempo tiempo, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, CancellationToken ct) =>
        {
            if (!(await Leer(db, ct)).Estado) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            if (Interlocked.Increment(ref _conexiones) > MaxConexiones)
            {
                Interlocked.Decrement(ref _conexiones);
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            try
            {
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-store";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";
                var fin = reloj.GetUtcNow() + MaxDuracion;
                using var t = new PeriodicTimer(TimeSpan.FromSeconds(3));
                do
                {
                    // los ajustes se releen en cada vuelta: apagar el interruptor corta el directo
                    using var scope = ctx.RequestServices.CreateScope();
                    var a = await Leer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct);
                    if (!a.Estado) return;
                    await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(Foto(a, estado, reloj, a.Cpu ? await EsDeDia(tiempo, cache, reloj, ct) : null), JsonSerializerOptions.Web)}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                } while (reloj.GetUtcNow() < fin && await t.WaitForNextTickAsync(ct));
            }
            catch (OperationCanceledException) { /* el visitante se fue */ }
            finally { Interlocked.Decrement(ref _conexiones); }
        });
    }
}
