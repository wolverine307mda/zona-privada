using System.Text;
using Mda.Api.Archivos;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Seguridad;

// Un archivo falso y apetecible («contraseñas.csv») en Documentos. Nadie legítimo tiene motivo para abrirlo o descargarlo:
// si alguien con una sesión robada lo hace, salta la alarma. Verlo en una lista no cuenta; abrirlo o descargarlo, sí.
public class Senuelo
{
    public int Id { get; set; }
    public string Raiz { get; set; } = "documentos";
    public string Ruta { get; set; } = "";
    public string Plantilla { get; set; } = "";
    public bool Activo { get; set; } = true;
    public int Disparos { get; set; }
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Ultimo { get; set; }
}

public record SenueloEntrada(string Plantilla, string? Carpeta);

public static class Senuelos
{
    public const string CerrarSesion = "senuelo-cerrar";

    // contenido claramente inventado: nada de aquí es real
    private static readonly Dictionary<string, (string Nombre, string Texto)> Plantillas = new()
    {
        ["contrasenas"] = ("contraseñas.csv", "sitio,usuario,clave\nbanco-ejemplo.invalid,usuario.demo,NO-ES-REAL-1\ncorreo-ejemplo.invalid,usuario.demo,NO-ES-REAL-2\nnube-ejemplo.invalid,usuario.demo,NO-ES-REAL-3\n"),
        ["banco"] = ("claves-banco.txt", "Banco de ejemplo\nUsuario: 00000000\nClave: NO-ES-REAL\nPIN tarjeta: 0000 (de ejemplo)\n"),
        ["tarjetas"] = ("tarjetas.txt", "Tarjeta de ejemplo\nNúmero: 0000 0000 0000 0000\nCaduca: 00/00  CVV: 000\n(Datos de ejemplo: no son reales)\n"),
    };

    // Se llama al abrir o descargar un archivo; true si era un señuelo
    public static async Task<bool> Comprobar(HttpContext ctx, AppDbContext db, Bot bot, IAvisos avisos, Carpetas c, string raiz, string real, bool prueba = false)
    {
        var rel = Path.GetRelativePath(Path.GetFullPath(c.RutaRaiz(raiz)), real).Replace('\\', '/');
        var s = await db.Senuelos.FirstOrDefaultAsync(x => x.Raiz == raiz && x.Ruta == rel && x.Activo);
        if (s is null) return false;
        await Disparar(ctx, db, bot, avisos, s, prueba);
        return true;
    }

    public static async Task Disparar(HttpContext ctx, AppDbContext db, Bot bot, IAvisos avisos, Senuelo s, bool prueba)
    {
        var ua = ctx.Request.Headers.UserAgent.ToString();
        var ip = IpCliente.De(ctx);
        var sid = Sesiones.Actual(ctx.User);
        s.Disparos++;
        s.Ultimo = DateTimeOffset.UtcNow;
        var cerrar = !prueba && !(await db.Ajustes.AsNoTracking().AnyAsync(a => a.Clave == CerrarSesion && a.Valor == "no"));
        if (cerrar && sid is not null && await db.Sesiones.FindAsync(sid) is { Cerrada: null } sesion) sesion.Cerrada = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var texto = $"{(prueba ? "PRUEBA · " : "")}Alguien ha abierto el archivo señuelo «{Path.GetFileName(s.Ruta)}».\n\nEquipo: {Dispositivo.Nombre(ua)}\nIP: {ip}\n" +
                    (cerrar ? "\nSu sesión se ha cerrado." : "");
        await avisos.PorPush(prueba ? "🪤 Prueba del señuelo" : "🚨 Señuelo abierto", texto, "/seguridad");
        if (bot.Vinculado)
            await bot.Enviar($"{(prueba ? "🪤" : "🚨")} {texto}", new
            {
                inline_keyboard = new[] { new[]
                {
                    new { text = "Fui yo", callback_data = $"sy:{s.Id}" },
                    new { text = "No fui yo", callback_data = $"sn:{sid ?? "-"}" },
                } },
            });
    }

    public static void MapSenuelos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/senuelos").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(new
        {
            cerrarSesion = !await db.Ajustes.AnyAsync(a => a.Clave == CerrarSesion && a.Valor == "no", ct),
            senuelos = await db.Senuelos.AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct),
            plantillas = Plantillas.Select(p => new { id = p.Key, nombre = p.Value.Nombre }),
        })).AddEndpointFilter<PinFiltro>(); // la lista da la ruta exacta de cada trampa: detrás del PIN

        // crea el archivo falso en una carpeta de Documentos y lo vigila
        g.MapPost("/", async (SenueloEntrada e, Carpetas c, AppDbContext db, CancellationToken ct) =>
        {
            if (!Plantillas.TryGetValue(e.Plantilla, out var p)) return Results.NotFound();
            var carpeta = c.Resolver("documentos", e.Carpeta);
            if (carpeta is null || !Directory.Exists(carpeta)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["carpeta"] = ["Esa carpeta no existe en Documentos."] });
            var destino = Carpetas.Libre(carpeta, p.Nombre);
            await File.WriteAllTextAsync(destino, p.Texto, new UTF8Encoding(false), ct);
            var rel = Path.GetRelativePath(Path.GetFullPath(c.RutaRaiz("documentos")), destino).Replace('\\', '/');
            var s = new Senuelo { Ruta = rel, Plantilla = e.Plantilla };
            db.Senuelos.Add(s);
            await db.SaveChangesAsync(ct);
            return Results.Ok(s);
        });

        g.MapPut("/{id:int}", async (int id, bool activo, AppDbContext db, CancellationToken ct) =>
        {
            var s = await db.Senuelos.FindAsync([id], ct);
            if (s is null) return Results.NotFound();
            s.Activo = activo;
            await db.SaveChangesAsync(ct);
            return Results.Ok(s);
        });

        g.MapDelete("/{id:int}", async (int id, Carpetas c, AppDbContext db, CancellationToken ct) =>
        {
            var s = await db.Senuelos.FindAsync([id], ct);
            if (s is null) return Results.NotFound();
            if (c.Resolver(s.Raiz, s.Ruta) is { } real && File.Exists(real)) File.Delete(real); // lo creó la app: es falso
            db.Senuelos.Remove(s);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPut("/ajustes", async (bool cerrarSesion, AppDbContext db, CancellationToken ct) =>
        {
            var a = await db.Ajustes.FindAsync([CerrarSesion], ct);
            if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = CerrarSesion });
            a.Valor = cerrarSesion ? "si" : "no";
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // simula un disparo (sin cerrar nada) para ver qué te llega
        g.MapPost("/{id:int}/probar", async (int id, HttpContext ctx, AppDbContext db, Bot bot, IAvisos avisos) =>
        {
            var s = await db.Senuelos.FindAsync(id);
            if (s is null) return Results.NotFound();
            await Disparar(ctx, db, bot, avisos, s, prueba: true);
            return Results.NoContent();
        }).RequireRateLimiting(Limites.Sensible);
    }
}
