using System.Globalization;
using Mda.Api.Agenda;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Papeles;

// Un papel o trámite que caduca: DNI, ITV, seguro del coche, garantía de la tele, carné… Avisa antes de que venza.
// `AvisadoNivel` recuerda hasta dónde se ha avisado (1 = ya queda poco, 2 = queda una semana, 3 = ya caducó) para no repetir.
public class Papel
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Categoria { get; set; } = "Otro";
    public DateOnly Vence { get; set; }
    public int AvisarDias { get; set; } = 30;
    public string Notas { get; set; } = "";
    public int AvisadoNivel { get; set; }
}

public record PapelEntrada(string? Nombre, string? Categoria, string? Vence, int? AvisarDias, string? Notas);
public record PapelDto(int Id, string Nombre, string Categoria, string Vence, int AvisarDias, string Notas, int Dias, string Estado);

public static class ServicioPapeles
{
    public const int Maximo = 100;
    public static readonly string[] Categorias = ["Identidad", "Coche", "Seguro", "Garantía", "Salud", "Casa", "Otro"];

    // 0 = tranquilo · 1 = entra en su plazo de aviso · 2 = queda una semana o menos · 3 = caducado
    public static int Nivel(int dias, int avisarDias) => dias < 0 ? 3 : dias <= 7 ? 2 : dias <= Math.Max(avisarDias, 8) ? 1 : 0;

    public static string Estado(int dias, int avisarDias) => Nivel(dias, avisarDias) switch { 3 => "caducado", 0 => "bien", _ => "pronto" };

    public static PapelDto Dto(Papel p, DateOnly hoy)
    {
        var dias = p.Vence.DayNumber - hoy.DayNumber;
        return new PapelDto(p.Id, p.Nombre, p.Categoria, p.Vence.ToString("yyyy-MM-dd"), p.AvisarDias, p.Notas, dias, Estado(dias, p.AvisarDias));
    }

    public static Dictionary<string, string[]> Validar(PapelEntrada e, bool nuevo)
    {
        var mal = new Dictionary<string, string[]>();
        if (nuevo || e.Nombre is not null) { if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 80) mal["nombre"] = ["Ponle un nombre (máximo 80 letras)."]; }
        if (e.Categoria is not null && !Categorias.Contains(e.Categoria)) mal["categoria"] = ["Categoría no válida."];
        if (nuevo || e.Vence is not null) { if (!DateOnly.TryParseExact(e.Vence, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) mal["vence"] = ["Pon la fecha de caducidad."]; }
        if (e.AvisarDias is { } d && d is < 1 or > 365) mal["avisarDias"] = ["Avisar entre 1 y 365 días antes."];
        if (e.Notas is { Length: > 500 }) mal["notas"] = ["Las notas llegan a 500 letras."];
        return mal;
    }

    public static async Task<List<PapelDto>> Lista(AppDbContext db, DateOnly hoy, CancellationToken ct)
    {
        var l = await db.Papeles.AsNoTracking().ToListAsync(ct);
        return l.OrderBy(p => p.Vence).ThenBy(p => p.Id).Select(p => Dto(p, hoy)).ToList();
    }
}

public static class PapelesEndpoints
{
    public static void MapPapeles(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/papeles").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
            Results.Ok(new { categorias = ServicioPapeles.Categorias, papeles = await ServicioPapeles.Lista(db, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).Date), ct) }));

        g.MapPost("/", async (PapelEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (ServicioPapeles.Validar(e, true) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.Papeles.CountAsync(ct) >= ServicioPapeles.Maximo)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Como mucho {ServicioPapeles.Maximo} papeles: borra alguno."] });
            var p = new Papel
            {
                Nombre = e.Nombre!.Trim(), Categoria = e.Categoria ?? "Otro", Vence = DateOnly.ParseExact(e.Vence!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                AvisarDias = e.AvisarDias ?? 30, Notas = (e.Notas ?? "").Trim(),
            };
            db.Papeles.Add(p);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/papeles/{p.Id}", ServicioPapeles.Dto(p, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).Date)));
        });

        // Cambiar la fecha (renovarlo) o el plazo de aviso vuelve a armar los avisos
        g.MapPut("/{id:int}", async (int id, PapelEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (ServicioPapeles.Validar(e, false) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var p = await db.Papeles.FindAsync([id], ct);
            if (p is null) return Results.NotFound();
            if (e.Nombre is not null) p.Nombre = e.Nombre.Trim();
            if (e.Categoria is not null) p.Categoria = e.Categoria;
            if (e.Notas is not null) p.Notas = e.Notas.Trim();
            if (e.Vence is not null) { p.Vence = DateOnly.ParseExact(e.Vence, "yyyy-MM-dd", CultureInfo.InvariantCulture); p.AvisadoNivel = 0; }
            if (e.AvisarDias is { } d) { p.AvisarDias = d; p.AvisadoNivel = 0; }
            await db.SaveChangesAsync(ct);
            return Results.Ok(ServicioPapeles.Dto(p, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).Date)));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var p = await db.Papeles.FindAsync([id], ct);
            if (p is null) return Results.NotFound();
            db.Papeles.Remove(p);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
