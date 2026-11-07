using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Inicio;

// La distribución de las tarjetas de Inicio (orden, cuáles se ven y de qué tamaño), guardada en el servidor para verla igual en todos tus dispositivos del mismo tipo:
// una para «pc» y otra para «movil». Es un JSON pequeño en Ajustes; el front lo normaliza (tarjetas que ya no existen, tarjetas nuevas…).
public record DisenoInicio(List<string>? Orden, List<string>? Ocultas, Dictionary<string, string>? Tamanos);

public static class DisenoInicioEndpoints
{
    public static readonly string[] Dispositivos = ["pc", "movil"];
    public static readonly string[] Tamanos = ["S", "M", "L"];
    public const int MaxTarjetas = 60;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Clave(string dispositivo) => $"inicio-diseno-{dispositivo}";

    // Pura: deja solo lo razonable (identificadores cortos y sin repetir, tamaños S/M/L, tope de tarjetas)
    public static DisenoInicio Limpiar(DisenoInicio? d)
    {
        bool Id(string s) => s.Length is > 0 and <= 40 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        var orden = (d?.Orden ?? []).Where(Id).Distinct().Take(MaxTarjetas).ToList();
        var ocultas = (d?.Ocultas ?? []).Where(Id).Distinct().Take(MaxTarjetas).ToList();
        var tam = (d?.Tamanos ?? []).Where(x => Id(x.Key) && Tamanos.Contains(x.Value)).Take(MaxTarjetas).ToDictionary(x => x.Key, x => x.Value);
        return new DisenoInicio(orden, ocultas, tam);
    }

    public static void MapDisenoInicio(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/inicio/diseno").RequireAuthorization();

        g.MapGet("/{dispositivo}", async (string dispositivo, AppDbContext db, CancellationToken ct) =>
        {
            if (!Dispositivos.Contains(dispositivo)) return Results.NotFound();
            var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave(dispositivo), ct))?.Valor;
            try { return Results.Ok(v is null ? new DisenoInicio([], [], []) : Limpiar(JsonSerializer.Deserialize<DisenoInicio>(v, Json))); } catch (JsonException) { return Results.Ok(new DisenoInicio([], [], [])); }
        });

        g.MapPut("/{dispositivo}", async (string dispositivo, DisenoInicio e, AppDbContext db, CancellationToken ct) =>
        {
            if (!Dispositivos.Contains(dispositivo)) return Results.NotFound();
            var d = Limpiar(e);
            var fila = await db.Ajustes.FirstOrDefaultAsync(a => a.Clave == Clave(dispositivo), ct);
            var v = JsonSerializer.Serialize(d, Json);
            if (fila is null) db.Ajustes.Add(new Ajuste { Clave = Clave(dispositivo), Valor = v }); else fila.Valor = v;
            await db.SaveChangesAsync(ct);
            return Results.Ok(d);
        });

        // «Restablecer»: vuelve al orden de siempre
        g.MapDelete("/{dispositivo}", async (string dispositivo, AppDbContext db, CancellationToken ct) =>
        {
            if (!Dispositivos.Contains(dispositivo)) return Results.NotFound();
            var fila = await db.Ajustes.FirstOrDefaultAsync(a => a.Clave == Clave(dispositivo), ct);
            if (fila is not null) { db.Ajustes.Remove(fila); await db.SaveChangesAsync(ct); }
            return Results.NoContent();
        });
    }
}
