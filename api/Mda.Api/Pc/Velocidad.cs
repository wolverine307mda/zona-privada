using Mda.Api.Agenda;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// Una medida del test de velocidad de Internet que hace el agente desde el PC (megabits por segundo y latencia).
public class MedidaVelocidad
{
    public int Id { get; set; }
    public DateTimeOffset Hora { get; set; }
    public double BajadaMbps { get; set; }
    public double SubidaMbps { get; set; }
    public int? PingMs { get; set; }
}

public record VelocidadEntrada(double BajadaMbps, double SubidaMbps, int? PingMs);

public static class VelocidadEndpoints
{
    public const int Guardadas = 400;

    public static void MapVelocidad(this IEndpointRouteBuilder app)
    {
        // el historial: las últimas medidas (unos 30 días si se mide cada noche) y un resumen
        app.MapGet("/api/red/velocidad", async (int? dias, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            var desde = reloj.GetUtcNow().AddDays(-Math.Clamp(dias ?? 30, 1, 365));
            var l = await db.MedidasVelocidad.AsNoTracking().Where(m => m.Hora >= desde).OrderBy(m => m.Hora).ToListAsync(ct);
            return Results.Ok(new
            {
                actual = l.LastOrDefault(),
                historial = l,
                resumen = new
                {
                    medidas = l.Count,
                    bajadaMedia = l.Count > 0 ? Math.Round(l.Average(m => m.BajadaMbps), 1) : (double?)null,
                    subidaMedia = l.Count > 0 ? Math.Round(l.Average(m => m.SubidaMbps), 1) : (double?)null,
                    bajadaMinima = l.Count > 0 ? l.Min(m => m.BajadaMbps) : (double?)null,
                },
            });
        }).RequireAuthorization();

        // lo manda el agente (token compartido) al terminar la prueba
        app.MapPost("/api/sistema/agente/velocidad", async (HttpRequest req, VelocidadEntrada e, AppDbContext db, IConfiguration config, TimeProvider reloj, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            if (e.BajadaMbps is < 0 or > 100_000 || e.SubidaMbps is < 0 or > 100_000 || e.PingMs is < 0 or > 60_000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["bajadaMbps"] = ["Velocidades que no cuadran."] });
            db.MedidasVelocidad.Add(new MedidaVelocidad { Hora = reloj.GetUtcNow(), BajadaMbps = Math.Round(e.BajadaMbps, 1), SubidaMbps = Math.Round(e.SubidaMbps, 1), PingMs = e.PingMs });
            await db.SaveChangesAsync(ct);
            var sobran = await db.MedidasVelocidad.OrderByDescending(m => m.Id).Skip(Guardadas).ToListAsync(ct);
            if (sobran.Count > 0) { db.MedidasVelocidad.RemoveRange(sobran); await db.SaveChangesAsync(ct); }
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting(Limites.Agente);
    }
}

// Cada noche (entre las 3 y las 5, hora de casa) pide una medida si hace más de 20 horas que no hay ninguna y el agente está en marcha:
// así sale una curva de cómo va tu conexión a lo largo del mes sin que tengas que acordarte.
public class VelocidadNocturna(IServiceScopeFactory scopes, EstadoAgente agente, Zona zona, TimeProvider reloj, ILogger<VelocidadNocturna> log) : BackgroundService
{
    public static bool EsHoraDeMedir(DateTimeOffset ahora, TimeZoneInfo zona, DateTimeOffset? ultima) =>
        TimeZoneInfo.ConvertTime(ahora, zona).Hour is >= 3 and < 5 && (ultima is null || ahora - ultima >= TimeSpan.FromHours(20));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(30), reloj);
        do
        {
            try { await Revisar(ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Fallo pidiendo el test de velocidad"); }
        } while (await t.WaitForNextTickAsync(ct));
    }

    public async Task Revisar(CancellationToken ct)
    {
        if (!agente.Conectado(OrdenesPc.AgenteConectado)) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ultima = (await db.MedidasVelocidad.AsNoTracking().OrderByDescending(m => m.Id).FirstOrDefaultAsync(ct))?.Hora;
        if (!EsHoraDeMedir(reloj.GetUtcNow(), zona.Info, ultima)) return;
        // si la última orden de velocidad aún está pendiente no se repite
        if (await db.OrdenesPc.AnyAsync(o => o.Tipo == "velocidad" && o.Creada > reloj.GetUtcNow().AddHours(-2), ct)) return;
        await OrdenesPc.Encolar(db, reloj, "velocidad", "", "prueba nocturna", ct);
        log.LogInformation("Test de velocidad nocturno pedido al agente");
    }
}
