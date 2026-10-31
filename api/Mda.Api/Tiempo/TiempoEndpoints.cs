using Mda.Api.Data;
using Mda.Api.Seguridad;

namespace Mda.Api.Tiempo;

public record AvisosTiempo(bool Activos);

public static class TiempoEndpoints
{
    public static void MapTiempo(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/tiempo").RequireAuthorization();

        // la previsión (o, si aún no hay sitio elegido, solo eso)
        g.MapGet("/", async (ServicioTiempo tiempo, AppDbContext db, CancellationToken ct) =>
        {
            var avisos = (await db.Ajustes.FindAsync([ServicioTiempo.ClaveAvisos], ct))?.Valor != "no";
            if (await tiempo.LugarGuardado(ct) is null) return Results.Ok(new { lugar = (Lugar?)null, avisos });
            var p = await tiempo.Leer(ct);
            return p is null
                ? Results.Problem("No se pudo consultar el tiempo ahora mismo.", statusCode: 502)
                : Results.Ok(new { p.Lugar, p.Ahora, p.Horas, p.Dias, avisos });
        });

        g.MapGet("/buscar", async (string? q, ServicioTiempo tiempo, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2 || q.Length > 80 ? Results.Ok(Array.Empty<Lugar>()) : Results.Ok(await tiempo.Buscar(q, ct)));

        g.MapPut("/lugar", async (Lugar l, ServicioTiempo tiempo, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(l.Nombre) || l.Nombre.Length > 100 || l.Lat is < -90 or > 90 || l.Lon is < -180 or > 180 || (l.Region?.Length ?? 0) > 200)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["lugar"] = ["Elige un sitio de la lista."] });
            await tiempo.GuardarLugar(db, l with { Nombre = l.Nombre.Trim() }, ct);
            return Results.NoContent();
        });

        // el aviso de la víspera (lluvia, tormenta, calor, helada)
        g.MapPut("/avisos", async (AvisosTiempo a, AppDbContext db, CancellationToken ct) =>
        {
            var aj = await db.Ajustes.FindAsync([ServicioTiempo.ClaveAvisos], ct) ?? db.Ajustes.Add(new Ajuste { Clave = ServicioTiempo.ClaveAvisos }).Entity;
            aj.Valor = a.Activos ? "si" : "no";
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
