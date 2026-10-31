using Mda.Api.Agenda;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Asistente;

public record FraseEntrada(string Texto);

// El asistente: le escribes (o le dices) una frase y te devuelve qué ha entendido, listo para guardar.
// Las preguntas («¿cuánto llevo en restaurantes?») las contesta él mismo con tus apuntes.
public static class AsistenteEndpoints
{
    public static void MapAsistente(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/asistente").RequireAuthorization();

        g.MapPost("/entender", async (FraseEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, Tiempo.ServicioTiempo tiempo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(e.Texto) || e.Texto.Length > 300)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["texto"] = ["Escribe una frase (máximo 300 caracteres)."] });
            var ctx = await ContextoDe(db, reloj, zona, ct);
            var p = Interprete.Entender(e.Texto, ctx);
            if (p.Pregunta is { } q) p = p with { Respuesta = await Responder(db, tiempo, q, DateOnly.FromDateTime(ctx.Ahora), ct) };
            return Results.Ok(p);
        });
    }

    public static async Task<Contexto> ContextoDe(AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct)
    {
        var ahora = TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).DateTime;
        // la categoría que más pones a cada concepto, mirando los últimos apuntes
        var ultimos = await db.Movimientos.AsNoTracking().OrderByDescending(m => m.Id).Take(1000)
            .Select(m => new { m.Concepto, m.Categoria }).ToListAsync(ct);
        var aprendido = ultimos.Where(m => !string.IsNullOrWhiteSpace(m.Concepto))
            .GroupBy(m => Interprete.Aplanar(m.Concepto.Trim()))
            .Where(x => x.Key.Length >= 3)
            .ToDictionary(x => x.Key, x => x.GroupBy(m => m.Categoria).OrderByDescending(c => c.Count()).First().Key);
        var huchas = await db.Huchas.AsNoTracking().OrderBy(h => h.Id).Select(h => new HuchaConocida(h.Id, h.Nombre, h.Emoji)).ToListAsync(ct);
        return new Contexto(ahora, zona.Info, aprendido, huchas);
    }

    internal static async Task<string> Responder(AppDbContext db, Tiempo.ServicioTiempo tiempo, Pregunta q, DateOnly hoy, CancellationToken ct)
    {
        if (q.Que == QuePregunta.Tiempo)
        {
            if (await tiempo.LugarGuardado(ct) is null) return "Aún no sé dónde vives: elige tu ciudad en la tarjeta del tiempo de Inicio.";
            return await tiempo.Leer(ct) is { } p ? Tiempo.ServicioTiempo.Frase(p, q.Desde, hoy) : "Ahora mismo no puedo consultar el tiempo. Prueba en un rato.";
        }

        if (q.Que == QuePregunta.Saldo)
        {
            if (await Cartera.Base(db, ct) is null) return "Aún no me has dicho cuánto tienes: ponlo en Cartera › Ajustar saldo.";
            var desde = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(-3);
            var movs = await db.Movimientos.AsNoTracking().Where(m => m.Fecha >= desde && m.Fecha <= hoy).ToListAsync(ct);
            var rec = await db.Recurrentes.AsNoTracking().ToListAsync(ct);
            var saldo = await Cartera.SaldoActual(db, hoy, ct);
            var enHuchas = await db.Huchas.SumAsync(h => (decimal?)h.Ahorrado, ct) ?? 0;
            var c = Cartera.ConsejosDe(hoy, movs, rec, saldo, enHuchas);
            var t = saldo < 0 ? $"Estás en {Cartera.Euros(saldo)}. " : $"Tienes {Cartera.Euros(saldo)}. ";
            if (enHuchas > 0) t += $"De eso, {Cartera.Euros(enHuchas)} están en huchas. ";
            t += c.PorDia > 0
                ? $"Hasta fin de mes puedes gastar unos {Cartera.Euros(c.PorDia)} al día."
                : "Este mes ya no queda margen: mejor no gastar más de la cuenta.";
            return t;
        }

        var tipo = q.Que == QuePregunta.Ingresado ? TipoMovimiento.Ingreso : TipoMovimiento.Gasto;
        var lista = await db.Movimientos.AsNoTracking().Where(m => m.Tipo == tipo && m.Fecha >= q.Desde && m.Fecha <= q.Hasta).ToListAsync(ct);
        string? de = null;
        if (q.Concepto is { } con)
        {
            lista = lista.Where(m => Interprete.Aplanar(m.Concepto).Contains(con)).ToList();
            de = lista.FirstOrDefault()?.Concepto ?? char.ToUpper(con[0]) + con[1..];
        }
        else if (q.Categoria is { } cat)
        {
            lista = lista.Where(m => m.Categoria == cat).ToList();
            de = Categorias.Todas[cat].Nombre;
        }
        var total = lista.Sum(m => m.Importe);
        var cuando = char.ToUpper(q.Periodo[0]) + q.Periodo[1..];
        var verbo = tipo == TipoMovimiento.Gasto ? "gastado" : "ingresado";
        if (lista.Count == 0)
            return de is null ? $"{cuando} no has {verbo} nada." : $"{cuando} no has {verbo} nada en {de}.";
        var apuntes = lista.Count == 1 ? "1 apunte" : $"{lista.Count} apuntes";
        var r = de is null
            ? $"{cuando} llevas {Cartera.Euros(total)} {verbo}s ({apuntes})."
            : $"{cuando} llevas {Cartera.Euros(total)} en {de} ({apuntes}).";
        if (de is null && tipo == TipoMovimiento.Gasto && lista.GroupBy(m => m.Categoria).OrderByDescending(x => x.Sum(m => m.Importe)).First() is { } top && lista.Select(m => m.Categoria).Distinct().Count() > 1)
            r += $" Lo que más: {(Categorias.Todas.TryGetValue(top.Key, out var ct2) ? ct2.Nombre : top.Key)}, {Cartera.Euros(top.Sum(m => m.Importe))}.";
        var esteMes = q.Desde == new DateOnly(hoy.Year, hoy.Month, 1);
        if (esteMes && q.Concepto is null && q.Categoria is { } c2 && await db.Presupuestos.FindAsync([c2], ct) is { Limite: > 0 } pres)
            r += $" Es el {Math.Round(total / pres.Limite * 100)} % de tu presupuesto ({Cartera.Euros(pres.Limite)}).";
        return r;
    }
}
