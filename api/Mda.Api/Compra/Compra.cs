using System.Globalization;
using System.Text;
using Mda.Api.Agenda;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Compra;

// La lista de la compra. Al marcar algo como comprado la app aprende cada cuánto lo compras y, cuando toca, te lo sugiere («ya toca leche»).
public class ArticuloCompra
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Cantidad { get; set; } = "";
    public string Clave { get; set; } = "";               // el nombre sin mayúsculas ni tildes: para reconocer «Leche» y «leche»
    public bool Comprado { get; set; }
    public DateTimeOffset Creado { get; set; }
    public DateTimeOffset? CompradoEn { get; set; }
}

// Lo que sabe la app de un producto: cuántas veces lo has comprado y cada cuántos días
public class ProductoHabitual
{
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Veces { get; set; }
    public DateOnly Ultima { get; set; }
    public double IntervaloDias { get; set; }             // media móvil de los días entre compras (0 = aún no hay dos compras en días distintos)
}

public record ArticuloEntrada(string? Nombre, string? Cantidad);
public record MarcaCompra(bool? Comprado);
public record ArticuloDto(int Id, string Nombre, string Cantidad, bool Comprado);
public record Sugerencia(string Nombre, int Dias, int CadaDias);

public static class ServicioCompra
{
    public const int MaximoPendientes = 200, MinimoVeces = 3, MaxHabituales = 600;

    public static string Clave(string nombre)
    {
        var d = nombre.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Aprende de una compra: suma una vez y ajusta el intervalo (media móvil, para que pese más lo reciente). Dos compras el mismo día cuentan como una.
    public static void Aprender(ProductoHabitual p, DateOnly hoy)
    {
        if (p.Veces > 0 && hoy > p.Ultima)
        {
            var delta = hoy.DayNumber - p.Ultima.DayNumber;
            p.IntervaloDias = p.IntervaloDias <= 0 ? delta : 0.6 * p.IntervaloDias + 0.4 * delta;
        }
        if (p.Veces == 0 || hoy > p.Ultima) { p.Veces++; p.Ultima = hoy; }
    }

    // ¿Ya toca? Cuando ha pasado casi el tiempo que sueles tardar en volver a comprarlo
    public static bool Toca(ProductoHabitual p, DateOnly hoy) =>
        p.Veces >= MinimoVeces && p.IntervaloDias >= 1 && hoy.DayNumber - p.Ultima.DayNumber >= Math.Round(p.IntervaloDias * 0.9);

    public static async Task<object> Estado(AppDbContext db, DateOnly hoy, CancellationToken ct)
    {
        var pendientes = await db.ArticulosCompra.AsNoTracking().Where(a => !a.Comprado).OrderBy(a => a.Id).ToListAsync(ct);
        var comprados = await db.ArticulosCompra.AsNoTracking().Where(a => a.Comprado).OrderByDescending(a => a.CompradoEn).Take(15).ToListAsync(ct);
        var enLista = pendientes.Select(a => a.Clave).ToHashSet();
        var habituales = await db.ProductosHabituales.AsNoTracking().OrderByDescending(p => p.Veces).ThenBy(p => p.Nombre).ToListAsync(ct);
        var sugerencias = habituales.Where(p => !enLista.Contains(p.Clave) && Toca(p, hoy))
            .OrderByDescending(p => (hoy.DayNumber - p.Ultima.DayNumber) / p.IntervaloDias).Take(8)
            .Select(p => new Sugerencia(p.Nombre, hoy.DayNumber - p.Ultima.DayNumber, (int)Math.Round(p.IntervaloDias))).ToList();
        var fuera = sugerencias.Select(s => Clave(s.Nombre)).ToHashSet();
        return new
        {
            pendientes = pendientes.Select(Dto),
            comprados = comprados.Select(Dto),
            sugerencias,
            habituales = habituales.Where(p => !enLista.Contains(p.Clave) && !fuera.Contains(p.Clave)).Take(12).Select(p => p.Nombre),
        };
    }

    public static ArticuloDto Dto(ArticuloCompra a) => new(a.Id, a.Nombre, a.Cantidad, a.Comprado);

    // Añade a la lista (si ya está pendiente no se repite). Devuelve null si el nombre no vale o la lista está llena.
    public static async Task<ArticuloCompra?> Añadir(AppDbContext db, string? nombre, string? cantidad, TimeProvider reloj, CancellationToken ct)
    {
        nombre = (nombre ?? "").Trim();
        if (nombre.Length is 0 or > 60) return null;
        var clave = Clave(nombre);
        var ya = await db.ArticulosCompra.FirstOrDefaultAsync(a => !a.Comprado && a.Clave == clave, ct);
        if (ya is not null) return ya;
        if (await db.ArticulosCompra.CountAsync(a => !a.Comprado, ct) >= MaximoPendientes) return null;
        var a = new ArticuloCompra { Nombre = nombre, Clave = clave, Cantidad = (cantidad ?? "").Trim(), Creado = reloj.GetUtcNow() };
        if (a.Cantidad.Length > 20) a.Cantidad = a.Cantidad[..20];
        db.ArticulosCompra.Add(a);
        await db.SaveChangesAsync(ct);
        return a;
    }

    // Marca (o desmarca) y aprende. Desmarcar no desaprende: solo deja el artículo otra vez en la lista.
    public static async Task<ArticuloCompra?> Marcar(AppDbContext db, int id, bool comprado, DateOnly hoy, TimeProvider reloj, CancellationToken ct)
    {
        var a = await db.ArticulosCompra.FindAsync([id], ct);
        if (a is null) return null;
        if (a.Comprado == comprado) return a;
        a.Comprado = comprado;
        a.CompradoEn = comprado ? reloj.GetUtcNow() : null;
        if (comprado)
        {
            var p = await db.ProductosHabituales.FindAsync([a.Clave], ct);
            if (p is null)
            {
                if (await db.ProductosHabituales.CountAsync(ct) >= MaxHabituales) db.ProductosHabituales.Remove(await db.ProductosHabituales.OrderBy(x => x.Veces).ThenBy(x => x.Ultima).FirstAsync(ct));
                db.ProductosHabituales.Add(p = new ProductoHabitual { Clave = a.Clave, Nombre = a.Nombre });
            }
            Aprender(p, hoy);
        }
        // se guardan solo los 15 últimos comprados
        var viejos = await db.ArticulosCompra.Where(x => x.Comprado && x.Id != id).OrderByDescending(x => x.CompradoEn).Skip(14).ToListAsync(ct);
        db.ArticulosCompra.RemoveRange(viejos);
        await db.SaveChangesAsync(ct);
        return a;
    }
}

public static class CompraEndpoints
{
    public static void MapCompra(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/compra").RequireAuthorization();
        DateOnly Hoy(TimeProvider reloj, Zona zona) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).Date);

        g.MapGet("/", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) => Results.Ok(await ServicioCompra.Estado(db, Hoy(reloj, zona), ct)));

        // Uno o varios a la vez: «leche, pan, huevos»
        g.MapPost("/", async (ArticuloEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var nombres = (e.Nombre ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(30).ToList();
            if (nombres.Count == 0 || nombres.Any(n => n.Length > 60))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Escribe qué hay que comprar (cada cosa hasta 60 letras; separa con comas)."] });
            foreach (var n in nombres)
                if (await ServicioCompra.Añadir(db, n, nombres.Count == 1 ? e.Cantidad : null, reloj, ct) is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"La lista admite {ServicioCompra.MaximoPendientes} cosas: marca alguna como comprada."] });
            return Results.Ok(await ServicioCompra.Estado(db, Hoy(reloj, zona), ct));
        });

        g.MapPut("/{id:int}", async (int id, MarcaCompra m, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
            await ServicioCompra.Marcar(db, id, m.Comprado ?? true, Hoy(reloj, zona), reloj, ct) is null ? Results.NotFound() : Results.Ok(await ServicioCompra.Estado(db, Hoy(reloj, zona), ct)));

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var a = await db.ArticulosCompra.FindAsync([id], ct);
            if (a is null) return Results.NotFound();
            db.ArticulosCompra.Remove(a);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Olvida lo que sabe de un producto (si te lo sugiere sin querer)
        g.MapDelete("/habituales/{nombre}", async (string nombre, AppDbContext db, CancellationToken ct) =>
        {
            var p = await db.ProductosHabituales.FindAsync([ServicioCompra.Clave(nombre)], ct);
            if (p is null) return Results.NotFound();
            db.ProductosHabituales.Remove(p);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
