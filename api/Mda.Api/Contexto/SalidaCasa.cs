using System.Globalization;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Domotica;
using Mda.Api.Seguridad;
using Mda.Api.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Contexto;

// Lo que se comprueba al salir de casa: qué puertas o ventanas se quedan abiertas y qué aparatos de tu lista (la plancha, la estufa, las luces…)
// se quedan encendidos. Solo se apaga solo lo que tú marcas, y solo si lo pides. Se guarda un historial de las últimas salidas.
public record AjustesSalida(bool Apagar = false, List<int>? Vigilar = null);
public record RevisionSalida(List<ElementoSalida> Abiertos, List<ElementoSalida> Encendidos, List<ElementoSalida> Apagados, List<string> Fallos);
public record ElementoSalida(int Id, string Nombre, int? HaceMinutos);
public record SalidaGuardada(DateTimeOffset Fecha, List<string> Abiertos, List<string> Encendidos, List<string> Apagados);

public static class SalidaCasa
{
    public const string Clave = "salida-ajustes", ClaveHistorial = "salida-historial";
    public const int MaxHistorial = 10, MaxVigilados = 30;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AjustesSalida Normalizar(AjustesSalida? a) => new(a?.Apagar ?? false, (a?.Vigilar ?? []).Distinct().Take(MaxVigilados).ToList());

    // Pura: mira los datos que ya hay (con la antigüedad) y dice qué está abierto y qué está encendido de lo que vigilas. Un dato con más de una hora no cuenta (sensor caído).
    public static (List<ElementoSalida> Abiertos, List<ElementoSalida> Encendidos) Revisar(IReadOnlyList<Aparato> aparatos, IReadOnlyList<Entidad> entidades, AjustesSalida ajustes, DateTimeOffset ahora)
    {
        var vigilar = ajustes.Vigilar ?? [];
        bool? Verdadero(int id, string clave)
        {
            var e = entidades.FirstOrDefault(x => x.AparatoId == id && x.Clave == clave);
            if (e?.Valor is null || e.Actualizado is null || ahora - e.Actualizado > TimeSpan.FromHours(1)) return null;
            return bool.TryParse(e.Valor, out var b) ? b : null;
        }
        int? Hace(int id, string clave) => entidades.FirstOrDefault(x => x.AparatoId == id && x.Clave == clave)?.Actualizado is { } t ? (int)Math.Max(0, (ahora - t).TotalMinutes) : null;
        var abiertos = aparatos.Where(a => a.Tipo == "puerta" && Verdadero(a.Id, "abierto") == true).Select(a => new ElementoSalida(a.Id, a.Nombre, Hace(a.Id, "abierto"))).ToList();
        var encendidos = aparatos.Where(a => vigilar.Contains(a.Id) && Catalogo.Buscar(a.Tipo, "encendido") is not null && Verdadero(a.Id, "encendido") == true)
            .Select(a => new ElementoSalida(a.Id, a.Nombre, Hace(a.Id, "encendido"))).ToList();
        return (abiertos, encendidos);
    }

    // «Hasta luego. Ojo: ventana del salón abierta; plancha encendida» o «Todo cerrado»
    public static string Frase(RevisionSalida r)
    {
        var partes = new List<string>();
        if (r.Abiertos.Count > 0) partes.Add(string.Join(", ", r.Abiertos.Select(a => $"{a.Nombre} abierta")));
        if (r.Encendidos.Count > 0) partes.Add(string.Join(", ", r.Encendidos.Select(a => $"{a.Nombre} encendido")));
        var texto = partes.Count == 0 ? "Hasta luego. Todo cerrado" : $"Hasta luego. Ojo: {string.Join("; ", partes)}";
        if (r.Apagados.Count > 0) texto += $". He apagado: {string.Join(", ", r.Apagados.Select(a => a.Nombre))}";
        if (r.Fallos.Count > 0) texto += $". No pude apagar: {string.Join(", ", r.Fallos)}";
        return texto + ".";
    }

    public static async Task<AjustesSalida> Leer(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor;
        try { return Normalizar(v is null ? null : JsonSerializer.Deserialize<AjustesSalida>(v, Json)); } catch (JsonException) { return Normalizar(null); }
    }

    public static async Task<List<SalidaGuardada>> Historial(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveHistorial, ct))?.Valor;
        try { return v is null ? [] : JsonSerializer.Deserialize<List<SalidaGuardada>>(v, Json) ?? []; } catch (JsonException) { return []; }
    }

    private static async Task Guardar(AppDbContext db, string clave, string valor, CancellationToken ct)
    {
        var f = await db.Ajustes.FirstOrDefaultAsync(a => a.Clave == clave, ct);
        if (f is null) db.Ajustes.Add(new Ajuste { Clave = clave, Valor = valor }); else f.Valor = valor;
        await db.SaveChangesAsync(ct);
    }

    // Se ejecuta al salir: revisa, apaga lo marcado si lo pediste, lo apunta en el historial y avisa por Telegram con un botón «Apagar X» por cada aparato que sigue encendido
    public static async Task<string> Ejecutar(AppDbContext db, ServicioCasa casa, Bot bot, DateTimeOffset ahora, CancellationToken ct)
    {
        var ajustes = await Leer(db, ct);
        var aparatos = await db.Aparatos.AsNoTracking().ToListAsync(ct);
        var entidades = await db.Entidades.AsNoTracking().ToListAsync(ct);
        var (abiertos, encendidos) = Revisar(aparatos, entidades, ajustes, ahora);

        var apagados = new List<ElementoSalida>(); var fallos = new List<string>();
        if (ajustes.Apagar)
            foreach (var e in encendidos.ToList())
            {
                var a = aparatos.First(x => x.Id == e.Id);
                var (ok, _, _) = await casa.Ejecutar(db, a, "encendido", "false", ct);
                if (ok) { apagados.Add(e); encendidos.Remove(e); } else fallos.Add(e.Nombre);
            }
        var r = new RevisionSalida(abiertos, encendidos, apagados, fallos);
        var frase = Frase(r);

        var historial = await Historial(db, ct);
        historial.Insert(0, new SalidaGuardada(ahora, abiertos.Select(x => x.Nombre).ToList(), encendidos.Select(x => x.Nombre).ToList(), apagados.Select(x => x.Nombre).ToList()));
        await Guardar(db, ClaveHistorial, JsonSerializer.Serialize(historial.Take(MaxHistorial).ToList(), Json), ct);

        // solo se molesta si hay algo que contar; con todo cerrado basta la respuesta del atajo
        if ((abiertos.Count > 0 || encendidos.Count > 0 || apagados.Count > 0 || fallos.Count > 0) && bot.Vinculado)
        {
            var botones = encendidos.Take(6).Select(x => new { text = $"Apagar {x.Nombre}", callback_data = $"ca:{x.Id}" }).Select(b => new[] { b }).ToArray();
            _ = bot.Enviar($"🚪 {frase}", botones.Length > 0 ? new { inline_keyboard = botones } : null, ct: ct);
        }
        return frase;
    }

    public static void MapSalidaCasa(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/domotica/salida").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var aparatos = await db.Aparatos.AsNoTracking().OrderBy(a => a.Nombre).ToListAsync(ct);
            return Results.Ok(new
            {
                ajustes = await Leer(db, ct),
                // los que se pueden vigilar: cualquier aparato que se enciende y apaga (la nevera no se ofrece si la llamas así)
                candidatos = aparatos.Where(a => Catalogo.Buscar(a.Tipo, "encendido") is not null).Select(a => new { a.Id, a.Nombre, a.Tipo }),
                puertas = aparatos.Where(a => a.Tipo == "puerta").Select(a => new { a.Id, a.Nombre }),
                historial = await Historial(db, ct),
            });
        });

        g.MapPut("/", async (AjustesSalida e, AppDbContext db, CancellationToken ct) =>
        {
            var a = Normalizar(e);
            // solo ids de aparatos que existen y se pueden apagar
            var validos = await db.Aparatos.AsNoTracking().Select(x => new { x.Id, x.Tipo }).ToListAsync(ct);
            a = a with { Vigilar = a.Vigilar!.Where(id => validos.Any(v => v.Id == id && Catalogo.Buscar(v.Tipo, "encendido") is not null)).ToList() };
            await Guardar(db, Clave, JsonSerializer.Serialize(a, Json), ct);
            return Results.Ok(a);
        });

        // probar la comprobación sin salir de casa y sin apagar nada ni mandar mensajes
        g.MapGet("/probar", async (AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            var ajustes = await Leer(db, ct);
            var (abiertos, encendidos) = Revisar(await db.Aparatos.AsNoTracking().ToListAsync(ct), await db.Entidades.AsNoTracking().ToListAsync(ct), ajustes, reloj.GetUtcNow());
            return Results.Ok(new { frase = Frase(new RevisionSalida(abiertos, encendidos, [], [])), abiertos, encendidos });
        });
    }
}
