using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// Una escena es un conjunto de ajustes del PC que se aplican de golpe: «Cine» baja el brillo y pone el plan de cine, «Trabajo» activa la concentración,
// «Dormir» silencia y programa el apagado. Cada campo vacío significa «no tocar eso».
public class Escena
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Icono { get; set; } = "✨";
    public string Energia { get; set; } = "";        // un plan de OrdenesPc.Perfiles, o vacío
    public int? Brillo { get; set; }
    public int? Volumen { get; set; }
    public bool? Concentracion { get; set; }          // true activa, false desactiva, null no toca
    public bool? Despierto { get; set; }              // true mantiene el PC despierto, false deja que se suspenda, null no toca
    public int? ApagarMinutos { get; set; }           // programa el apagado (pide Face ID al aplicarla)
}

public record EscenaEntrada(string? Nombre, string? Icono, string? Energia, int? Brillo, int? Volumen, bool? Concentracion, bool? Despierto, int? ApagarMinutos);
public record EscenaAtajo(string? Nombre);

public static class Escenas
{
    public const int Maximo = 12;

    public static List<Escena> Predeterminadas() =>
    [
        new() { Nombre = "Cine", Icono = "🎬", Energia = "cine", Brillo = 40, Volumen = 50 },
        new() { Nombre = "Trabajo", Icono = "💼", Energia = "equilibrado", Brillo = 80, Concentracion = true },
        new() { Nombre = "Dormir", Icono = "🌙", Brillo = 10, Volumen = 0, Despierto = false, ApagarMinutos = 60 },
    ];

    // lo que hace la escena, en palabras (para la tarjeta y el aviso)
    public static string Resumen(Escena e)
    {
        var l = new List<string>();
        if (e.Energia.Length > 0) l.Add($"plan {e.Energia}");
        if (e.Brillo is { } b) l.Add($"brillo {b} %");
        if (e.Volumen is { } v) l.Add($"volumen {v} %");
        if (e.Concentracion is { } c) l.Add(c ? "concentración" : "sin concentración");
        if (e.Despierto is { } d) l.Add(d ? "PC despierto" : "puede suspenderse");
        if (e.ApagarMinutos is { } m) l.Add($"apagar en {m} min");
        return l.Count > 0 ? string.Join(" · ", l) : "no cambia nada";
    }

    public static Dictionary<string, string[]> Validar(EscenaEntrada e)
    {
        var errores = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 40) errores["nombre"] = ["Ponle un nombre (hasta 40 letras)."];
        if ((e.Icono ?? "").Length > 16) errores["icono"] = ["El icono es un emoji."];
        if (!string.IsNullOrEmpty(e.Energia) && !OrdenesPc.Perfiles.Contains(e.Energia)) errores["energia"] = [$"El plan es uno de: {string.Join(", ", OrdenesPc.Perfiles)}."];
        if (e.Brillo is < 0 or > 100) errores["brillo"] = ["De 0 a 100."];
        if (e.Volumen is < 0 or > 100) errores["volumen"] = ["De 0 a 100."];
        if (e.ApagarMinutos is < 1 or > 720) errores["apagarMinutos"] = ["De 1 a 720 minutos."];
        return errores;
    }

    // encola las órdenes de cada ajuste (el agente las recoge en unos segundos)
    public static async Task<(bool Ok, string Mensaje)> Aplicar(Escena e, AppDbContext db, EstadoAgente agente, IAvisos avisos, TimeProvider reloj, string desde, CancellationToken ct = default)
    {
        if (!agente.Conectado(OrdenesPc.AgenteConectado)) return (false, "El agente de Windows no está en marcha.");
        if (e.Energia.Length > 0) await OrdenesPc.Encolar(db, reloj, "energia", e.Energia, desde, ct);
        if (e.Brillo is { } b) await OrdenesPc.Encolar(db, reloj, "brillo", b.ToString(), desde, ct);
        if (e.Volumen is { } v) await OrdenesPc.Encolar(db, reloj, "volumen", v.ToString(), desde, ct);
        if (e.Despierto is { } d) await OrdenesPc.Encolar(db, reloj, "despierto", d ? "on" : "off", desde, ct);
        if (e.Concentracion is { } c) await Casa.Concentracion(c, db, agente, avisos, reloj, desde, ct);
        if (e.ApagarMinutos is { } m) await OrdenesPc.Encolar(db, reloj, "apagar", m.ToString(), desde, ct);
        var mensaje = $"Escena «{e.Nombre}»: {Resumen(e)}.";
        _ = avisos.PorTelegram($"{e.Icono} {mensaje}", ct);
        return (true, mensaje);
    }

    public static void MapEscenas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/escenas").RequireAuthorization();
        string Desde(HttpContext ctx) => Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()) + " · " + IpCliente.De(ctx);
        object Salida(Escena e) => new { e.Id, e.Nombre, e.Icono, e.Energia, e.Brillo, e.Volumen, e.Concentracion, e.Despierto, e.ApagarMinutos, resumen = Resumen(e), apagaElPc = e.ApagarMinutos is not null };
        static void Copiar(EscenaEntrada e, Escena x)
        {
            x.Nombre = e.Nombre!.Trim(); x.Icono = string.IsNullOrWhiteSpace(e.Icono) ? "✨" : e.Icono.Trim(); x.Energia = e.Energia ?? "";
            (x.Brillo, x.Volumen, x.Concentracion, x.Despierto, x.ApagarMinutos) = (e.Brillo, e.Volumen, e.Concentracion, e.Despierto, e.ApagarMinutos);
        }

        // la primera vez se crean tres de ejemplo (Cine, Trabajo y Dormir) que se pueden cambiar o quitar
        g.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Escenas.AnyAsync(ct) && !await db.Ajustes.AnyAsync(a => a.Clave == "escenas-sembradas", ct))
            {
                db.Escenas.AddRange(Predeterminadas());
                db.Ajustes.Add(new Ajuste { Clave = "escenas-sembradas", Valor = "si" }); // quitarlas todas no las vuelve a crear
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok((await db.Escenas.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(Salida));
        });

        g.MapPost("/", async (EscenaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (Validar(e) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.Escenas.CountAsync(ct) >= Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Máximo {Maximo} escenas."] });
            if ((await db.Escenas.AsNoTracking().Select(x => x.Nombre).ToListAsync(ct)).Any(n => string.Equals(n, e.Nombre!.Trim(), StringComparison.OrdinalIgnoreCase)))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Ya hay una escena con ese nombre."] });
            var x = new Escena(); Copiar(e, x);
            db.Escenas.Add(x);
            await db.SaveChangesAsync(ct);
            return Results.Ok(Salida(x));
        });

        g.MapPut("/{id:int}", async (int id, EscenaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Escenas.FindAsync([id], ct) is not { } x) return Results.NotFound();
            if (Validar(e) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if ((await db.Escenas.AsNoTracking().Where(o => o.Id != id).Select(o => o.Nombre).ToListAsync(ct)).Any(n => string.Equals(n, e.Nombre!.Trim(), StringComparison.OrdinalIgnoreCase)))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Ya hay una escena con ese nombre."] });
            Copiar(e, x);
            await db.SaveChangesAsync(ct);
            return Results.Ok(Salida(x));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Escenas.FindAsync([id], ct) is not { } x) return Results.NotFound();
            db.Escenas.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // una escena que programa el apagado pide Face ID, como apagar el PC a mano
        g.MapPost("/{id:int}/aplicar", async (int id, AppDbContext db, EstadoAgente agente, IAvisos avisos, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            if (await db.Escenas.FindAsync([id], ct) is not { } x) return Results.NotFound();
            if (x.ApagarMinutos is not null && !await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            var (ok, mensaje) = await Aplicar(x, db, agente, avisos, reloj, Desde(ctx), ct);
            return ok ? Results.Ok(new { mensaje }) : Results.Conflict(new { detalle = mensaje });
        }).RequireRateLimiting(Limites.Sensible);
    }
}
