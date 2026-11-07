using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Domotica;

public record ReglaEntrada(string? Nombre, bool? Activa, int SensorId, string? SensorClave, string? Operador, double Umbral, int Minutos, int DestinoId,
    string? Accion, double? Objetivo, string? Modo, bool Revertir, double Histeresis, int EsperaMinutos, bool SoloAvisar);

public static class ReglasEndpoints
{
    public const int Maximo = 20;
    private static readonly string[] Modos = ["auto", "frio", "calor", "eco"];

    // Valida la regla contra los aparatos que existen; devuelve los errores por campo
    public static Dictionary<string, string[]> Validar(ReglaEntrada e, IReadOnlyDictionary<int, Aparato> aparatos)
    {
        var mal = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 40) mal["nombre"] = ["Ponle un nombre (hasta 40 letras)."];
        if (!aparatos.TryGetValue(e.SensorId, out var sensor)) mal["sensorId"] = ["Elige un sensor."];
        else if (Catalogo.Buscar(sensor.Tipo, e.SensorClave ?? "") is not { Tipo: "numero" } && !EvaluadorReglas.EsEstado(sensor.Tipo, e.SensorClave ?? "")) mal["sensorClave"] = ["Elige un dato numérico (temperatura, humedad…) o un estado (abierta, encendido)."];
        // en una regla de estado el operador y el umbral los pone el sistema (el estado se convierte en 1/0)
        var estado = sensor is not null && EvaluadorReglas.EsEstado(sensor.Tipo, e.SensorClave ?? "");
        if (!estado && e.Operador is not (">" or "<")) mal["operador"] = ["«>» (más de) o «<» (menos de)."];
        if (e.Minutos is < 0 or > 1440) mal["minutos"] = ["De 0 a 1440 minutos."];
        if (e.Histeresis is < 0 or > 20) mal["histeresis"] = ["De 0 a 20."];
        if (e.EsperaMinutos is < 1 or > 1440) mal["esperaMinutos"] = ["De 1 a 1440 minutos."];
        if (!e.SoloAvisar)
        {
            if (!aparatos.TryGetValue(e.DestinoId, out var destino)) mal["destinoId"] = ["Elige qué aparato manejar."];
            else if (Catalogo.Buscar(destino.Tipo, "encendido") is null) mal["destinoId"] = ["Ese aparato no se puede encender ni apagar."];
            if (e.Accion is not ("encender" or "apagar")) mal["accion"] = ["«encender» o «apagar»."];
            if (e.Objetivo is < 16 or > 30) mal["objetivo"] = ["De 16 a 30 grados."];
            if (e.Modo is not null && e.Modo != "" && !Modos.Contains(e.Modo)) mal["modo"] = [$"Uno de: {string.Join(", ", Modos)}."];
        }
        return mal;
    }

    private static void Copiar(ReglaEntrada e, ReglaCasa r, bool estado)
    {
        r.Nombre = e.Nombre!.Trim(); r.Activa = e.Activa ?? true; r.SensorId = e.SensorId; r.SensorClave = e.SensorClave!;
        r.Operador = estado ? ">" : e.Operador!; r.Umbral = estado ? 0.5 : e.Umbral; r.Minutos = e.Minutos; r.DestinoId = e.SoloAvisar ? e.SensorId : e.DestinoId; r.Accion = e.Accion ?? "encender";
        r.Objetivo = e.Objetivo; r.Modo = string.IsNullOrEmpty(e.Modo) ? null : e.Modo; r.Revertir = e.Revertir;
        r.Histeresis = e.Histeresis; r.EsperaMinutos = e.EsperaMinutos; r.SoloAvisar = e.SoloAvisar;
        // si se cambia la regla, empieza de cero
        r.Disparada = false; r.Desde = null;
    }

    public static void MapReglasCasa(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/domotica/reglas").RequireAuthorization();

        // reglas de partida: se eligen y solo falta decir qué sensor y qué aparato
        g.MapGet("/plantillas", () => new[]
        {
            new { id = "aire-calor", nombre = "Aire si hace calor", descripcion = "Enciende el aire a 24 ° cuando pasa de 26 ° durante 5 min y lo apaga al bajar de 24 °.",
                  valores = new { operador = ">", umbral = 26.0, minutos = 5, accion = "encender", objetivo = (double?)24, modo = "frio", revertir = true, histeresis = 2.0, esperaMinutos = 15, soloAvisar = false, sensorClave = (string?)null } },
            new { id = "calor-frio", nombre = "Calefacción si hace frío", descripcion = "Enciende el aparato a 21 ° cuando baja de 18 ° durante 10 min y lo apaga al subir de 20 °.",
                  valores = new { operador = "<", umbral = 18.0, minutos = 10, accion = "encender", objetivo = (double?)21, modo = "calor", revertir = true, histeresis = 2.0, esperaMinutos = 15, soloAvisar = false, sensorClave = (string?)null } },
            new { id = "puerta-abierta", nombre = "Puerta abierta demasiado tiempo", descripcion = "Te avisa por Telegram si una puerta lleva abierta más de 10 min (elige el sensor de la puerta, dato «abierto»).",
                  valores = new { operador = ">", umbral = 0.5, minutos = 10, accion = "encender", objetivo = (double?)null, modo = (string?)null, revertir = false, histeresis = 0.0, esperaMinutos = 30, soloAvisar = true, sensorClave = "abierto" } },
            new { id = "enchufe-encendido", nombre = "Enchufe o plancha encendido demasiado tiempo", descripcion = "Te avisa (con botón para apagarlo) si un enchufe lleva encendido más de 60 min.",
                  valores = new { operador = ">", umbral = 0.5, minutos = 60, accion = "encender", objetivo = (double?)null, modo = (string?)null, revertir = false, histeresis = 0.0, esperaMinutos = 60, soloAvisar = true, sensorClave = "encendido" } },
            new { id = "luz-encendida", nombre = "Luz encendida mucho rato", descripcion = "Te avisa (con botón para apagarla) si una luz lleva encendida más de 3 horas.",
                  valores = new { operador = ">", umbral = 0.5, minutos = 180, accion = "encender", objetivo = (double?)null, modo = (string?)null, revertir = false, histeresis = 0.0, esperaMinutos = 60, soloAvisar = true, sensorClave = "encendido" } },
            new { id = "avisar-calor", nombre = "Avisarme si hace mucho calor", descripcion = "Te escribe por Telegram si pasa de 30 ° durante 10 min, sin tocar nada.",
                  valores = new { operador = ">", umbral = 30.0, minutos = 10, accion = "encender", objetivo = (double?)null, modo = (string?)null, revertir = false, histeresis = 1.0, esperaMinutos = 60, soloAvisar = true, sensorClave = (string?)null } },
        });

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var aparatos = await db.Aparatos.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
            string N(int id) => aparatos.TryGetValue(id, out var a) ? a.Nombre : "(quitado)";
            return Results.Ok((await db.Reglas.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct)).Select(r => new
            {
                r.Id, r.Nombre, r.Activa, r.SensorId, r.SensorClave, r.Operador, r.Umbral, r.Minutos, r.DestinoId, r.Accion, r.Objetivo, r.Modo, r.Revertir, r.Histeresis,
                r.EsperaMinutos, r.SoloAvisar, r.Disparada, r.Ultima, resumen = EvaluadorReglas.Resumen(r, N(r.SensorId), N(r.DestinoId), aparatos.TryGetValue(r.SensorId, out var sa) && EvaluadorReglas.EsEstado(sa.Tipo, r.SensorClave)),
                estado = aparatos.TryGetValue(r.SensorId, out var sb) && EvaluadorReglas.EsEstado(sb.Tipo, r.SensorClave),
            }));
        });

        g.MapPost("/", async (ReglaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            var aparatos = await db.Aparatos.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
            if (Validar(e, aparatos) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.Reglas.CountAsync(ct) >= Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Máximo {Maximo} reglas."] });
            var r = new ReglaCasa(); Copiar(e, r, aparatos[e.SensorId] is { } s0 && EvaluadorReglas.EsEstado(s0.Tipo, e.SensorClave!));
            db.Reglas.Add(r);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id });
        });

        g.MapPut("/{id:int}", async (int id, ReglaEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Reglas.FindAsync([id], ct) is not { } r) return Results.NotFound();
            var aparatos = await db.Aparatos.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
            if (Validar(e, aparatos) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            Copiar(e, r, aparatos[e.SensorId] is { } s1 && EvaluadorReglas.EsEstado(s1.Tipo, e.SensorClave!));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id });
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Reglas.FindAsync([id], ct) is not { } r) return Results.NotFound();
            db.Reglas.Remove(r);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // lo último que han hecho las reglas
        g.MapGet("/registro", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.RegistrosCasa.AsNoTracking().OrderByDescending(x => x.Fecha).Take(30).Select(x => new { x.Id, x.ReglaId, x.Fecha, x.Texto, x.Ok }).ToListAsync(ct)));
    }
}
