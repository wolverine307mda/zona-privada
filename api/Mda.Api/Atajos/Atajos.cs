using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mda.Api.Agenda;
using Mda.Api.Asistente;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Mda.Api.Seguridad;
using Mda.Api.Telegram;
using Mda.Api.Tiempo;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mda.Api.Atajos;

// Un token para el iPhone (app Atajos): sirve solo para lo que se le dio (gasto, pago, lectura), no abre la app ni lleva cookie,
// se enseña una sola vez, se guarda como hash y se revoca desde Seguridad.
public class TokenAtajo
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Alcances { get; set; } = "";
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UltimoUso { get; set; }
    public string UltimaIp { get; set; } = "";
    // redes desde las que se ha usado (/24 en IPv4, /48 en IPv6: el móvil cambia de IP a cada rato, de red no tanto)
    public string Redes { get; set; } = "";
    public bool Revocado { get; set; }
}

public record TokenEntrada(string Nombre, string[] Alcances);
public record MovimientoAtajo(decimal Importe, string Concepto, string? Tipo, string? Categoria, DateOnly? Fecha);
public record FraseAtajo(string Texto);
public record PagoAtajo(string Comercio, decimal Importe, string? Tarjeta);
public record PingAtajo(string? Texto);

public static class AtajosApi
{
    public static readonly string[] Alcances = ["gasto", "pago", "lectura"];
    private static readonly CultureInfo Es = new("es-ES");

    public static string Hashear(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static string Red(string ip) =>
        IPAddress.TryParse(ip, out var a) && a.GetAddressBytes() is { } b
            ? (b.Length == 4 ? string.Join('.', b[..3]) : Convert.ToHexString(b[..6]))
            : ip;

    // exige «Authorization: Bearer mda_…» con ese alcance; deja el token en HttpContext.Items
    private static RouteHandlerBuilder Requiere(this RouteHandlerBuilder b, string alcance) => b.AddEndpointFilter(async (ctx, next) =>
    {
        var http = ctx.HttpContext;
        var cabecera = http.Request.Headers.Authorization.ToString();
        if (!cabecera.StartsWith("Bearer mda_", StringComparison.Ordinal)) return Results.Unauthorized();
        var hash = Hashear(cabecera["Bearer ".Length..].Trim());
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var t = await db.TokensAtajo.FirstOrDefaultAsync(x => x.Hash == hash && !x.Revocado);
        if (t is null || !t.Alcances.Split(',').Contains(alcance)) return Results.Unauthorized();

        var ip = IpCliente.De(http);
        var red = Red(ip);
        var redes = t.Redes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        var nueva = !redes.Contains(red);
        if (nueva) { redes.Add(red); t.Redes = string.Join(',', redes.TakeLast(8)); }
        t.UltimoUso = DateTimeOffset.UtcNow;
        t.UltimaIp = ip;
        await db.SaveChangesAsync();
        // la primera vez es normal; después, una red distinta merece un aviso
        if (nueva && redes.Count > 1)
            _ = http.RequestServices.GetRequiredService<IAvisos>().PorTelegram($"🔑 El atajo «{t.Nombre}» se ha usado desde una red nueva ({ip}). Si no has sido tú, revócalo en Seguridad › Atajos.");
        http.Items["tokenAtajo"] = t;
        return await next(ctx);
    });

    public static void MapAtajos(this IEndpointRouteBuilder app)
    {
        // ---- gestión de tokens (con sesión) ----
        var g = app.MapGroup("/api/atajos-tokens").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.TokensAtajo.AsNoTracking().OrderBy(t => t.Id)
                .Select(t => new { t.Id, t.Nombre, alcances = t.Alcances.Split(',', StringSplitOptions.None), t.Creado, t.UltimoUso, t.UltimaIp, t.Revocado }).ToListAsync(ct)));

        // el token completo solo sale en esta respuesta
        g.MapPost("/", async (TokenEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            var alcances = e.Alcances.Distinct().Where(Alcances.Contains).ToArray();
            if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Length > 40 || alcances.Length == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Pon un nombre (máx. 40) y al menos un permiso."] });
            var token = "mda_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var t = new TokenAtajo { Nombre = e.Nombre.Trim(), Hash = Hashear(token), Alcances = string.Join(',', alcances) };
            db.TokensAtajo.Add(t);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { t.Id, t.Nombre, token });
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var t = await db.TokensAtajo.FindAsync([id], ct);
            if (t is null) return Results.NotFound();
            t.Revocado = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- lo que usa el iPhone (con token) ----
        var a = app.MapGroup("/api/atajos").AllowAnonymous().RequireRateLimiting(Limites.Atajos);

        a.MapGet("/categorias", () => Categorias.Todas.Where(c => c.Value.Tipo == TipoMovimiento.Gasto).Select(c => new { id = c.Key, nombre = c.Value.Nombre })).Requiere("gasto");

        // «Gasto rápido»: importe, concepto, tipo, categoría y fecha del formulario de Atajos
        a.MapPost("/movimiento", async (MovimientoAtajo e, AppDbContext db, IAvisos avisos, ILogger<Movimiento> log, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var tipo = string.Equals(e.Tipo, "ingreso", StringComparison.OrdinalIgnoreCase) ? TipoMovimiento.Ingreso : TipoMovimiento.Gasto;
            var cat = e.Categoria is { Length: > 0 } c ? c : tipo == TipoMovimiento.Gasto ? "otros" : "otros-ingresos";
            var entrada = new MovimientoEntrada(tipo, e.Fecha ?? Cartera.Hoy(reloj, zona), e.Importe, cat, e.Concepto ?? "", "Atajo de iOS", null, null, null, null);
            var res = await GastosEndpoints.Crear(entrada, db, avisos, log, ct);
            if (res is not Created<Movimiento> { Value: { } m }) return res;
            var hoy = Cartera.Hoy(reloj, zona);
            return Results.Ok(new { id = m.Id, mensaje = $"Apuntado: {Cartera.Euros(m.Importe)} · {m.Concepto} · libre {Cartera.Euros(await Cartera.SaldoActual(db, hoy, ct))}" });
        }).Requiere("gasto");

        // «Gasto por voz»: una frase como al asistente; guarda si lo entiende sin dudas
        a.MapPost("/frase", async (FraseAtajo e, HttpContext http, AppDbContext db, IAvisos avisos, ILogger<Movimiento> log, ServicioTiempo tiempo, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(e.Texto) || e.Texto.Length > 300) return Results.BadRequest();
            var ctx = await AsistenteEndpoints.ContextoDe(db, reloj, zona, ct);
            var p = Interprete.Entender(e.Texto, ctx);
            if (p.Clase == "movimiento" && p.Movimiento is { } m && (p.Dudas is null || p.Dudas.Count == 0))
            {
                var res = await GastosEndpoints.Crear(m, db, avisos, log, ct);
                return res is Created<Movimiento> ? Results.Ok(new { guardado = true, mensaje = $"Apuntado: {p.Resumen}" }) : res;
            }
            if (p.Clase == "pregunta" && p.Pregunta is { } q)
            {
                // las preguntas dan información: solo con el permiso de lectura
                var token = (TokenAtajo)http.Items["tokenAtajo"]!;
                if (!token.Alcances.Split(',').Contains("lectura")) return Results.Ok(new { guardado = false, mensaje = "Este atajo no tiene permiso de lectura." });
                return Results.Ok(new { guardado = false, mensaje = await AsistenteEndpoints.Responder(db, tiempo, zona, q, Cartera.Hoy(reloj, zona), ct) });
            }
            return Results.Ok(new { guardado = false, mensaje = p.Dudas is { Count: > 0 } ? $"No estoy seguro: {string.Join(' ', p.Dudas)}" : "No lo he entendido: dilo con importe y concepto." });
        }).Requiere("gasto");

        // saldo y agenda de hoy (para un widget de Scriptable o para que Siri lo lea)
        a.MapGet("/resumen", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Cartera.Hoy(reloj, zona);
            return Results.Ok(new { saldo = await Cartera.SaldoActual(db, hoy, ct), saldoTexto = Cartera.Euros(await Cartera.SaldoActual(db, hoy, ct)), agenda = await AsistenteEndpoints.Agenda(db, zona, hoy, hoy, ct) });
        }).Requiere("lectura");

        // el iPhone avisa (hora, lugar…) y el bot te pregunta: respondes desde la notificación de Telegram y el asistente lo guarda
        a.MapPost("/ping", async (PingAtajo e, Bot bot, CancellationToken ct) =>
        {
            if (!bot.Vinculado) return Results.Conflict(new { mensaje = "Telegram no está vinculado." });
            var texto = string.IsNullOrWhiteSpace(e.Texto) ? "¿Qué has gastado?" : e.Texto.Trim()[..Math.Min(e.Texto.Trim().Length, 120)];
            var ok = await bot.Enviar($"💬 {System.Net.WebUtility.HtmlEncode(texto)}", new { force_reply = true, input_field_placeholder = "12,50 en el Mercadona" }, html: true, ct: ct);
            return ok ? Results.NoContent() : Results.StatusCode(502);
        }).Requiere("gasto");

        // Apple Pay: la automatización «Transacción» manda comercio e importe al pagar; entra «por confirmar»
        a.MapPost("/pago", async (PagoAtajo e, AppDbContext db, IAvisos avisos, Bot bot, IMemoryCache cache, ILogger<Movimiento> log, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var comercio = Limpiar(e.Comercio);
            var importe = Math.Round(Math.Abs(e.Importe), 2, MidpointRounding.AwayFromZero);
            if (comercio.Length == 0 || importe <= 0 || importe > 1_000_000) return Results.BadRequest();
            // el mismo pago dos veces en dos minutos (la automatización a veces dispara dos) se cuenta una vez
            var clave = $"pago:{Interprete.Aplanar(comercio)}:{importe}";
            if (cache.TryGetValue(clave, out _)) return Results.Ok(new { duplicado = true, mensaje = "Ya estaba apuntado." });
            cache.Set(clave, true, TimeSpan.FromMinutes(2));

            var tipo = e.Importe < 0 ? TipoMovimiento.Ingreso : TipoMovimiento.Gasto; // una devolución llega con importe negativo
            var ctx = await AsistenteEndpoints.ContextoDe(db, reloj, zona, ct);
            var cat = tipo == TipoMovimiento.Ingreso ? "otros-ingresos" : Categoria(comercio, ctx.Aprendido);
            var hoy = Cartera.Hoy(reloj, zona);
            var res = await GastosEndpoints.Crear(new MovimientoEntrada(tipo, hoy, importe, cat, comercio, e.Tarjeta is { Length: > 0 } t ? $"Apple Pay · {t}" : "Apple Pay", null, null, null, null), db, avisos, log, ct);
            if (res is not Created<Movimiento> { Value: { } m }) return res;
            m.PorConfirmar = true;
            await db.SaveChangesAsync(ct);

            var nombre = Categorias.Todas[m.Categoria].Nombre;
            var presupuesto = await TextoPresupuesto(db, m, hoy, ct);
            var mensaje = $"{m.Concepto} {Cartera.Euros(m.Importe)} · {nombre}{presupuesto}";
            if (bot.Vinculado)
            {
                var otras = new[] { "supermercado", "restaurantes", "gasolina", "ocio", "transporte", "otros" }.Where(c => c != m.Categoria).Take(3);
                await bot.Enviar($"💳 {System.Net.WebUtility.HtmlEncode(mensaje)}", new
                {
                    inline_keyboard = new[]
                    {
                        new[] { new { text = "✔ Vale", callback_data = $"pv:{m.Id}" }, new { text = "↩ Deshacer", callback_data = $"pd:{m.Id}" } },
                        otras.Select(c => new { text = Categorias.Todas[c].Nombre, callback_data = $"pc:{m.Id}:{c}" }).ToArray(),
                    },
                }, html: true, ct: ct);
            }
            return Results.Ok(new { id = m.Id, mensaje });
        }).Requiere("pago");
    }

    // «MERCADONA 0123 MADRID» → «Mercadona Madrid»
    public static string Limpiar(string? s)
    {
        var t = Regex.Replace((s ?? "").Trim(), @"\b[\d*#]+\b", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length > 120) t = t[..120];
        return t.Length > 0 && t == t.ToUpperInvariant() ? Es.TextInfo.ToTitleCase(t.ToLowerInvariant()) : t;
    }

    // la categoría que ya usas con ese comercio (el nombre exacto o uno que aparezca dentro); si no, «otros»
    public static string Categoria(string comercio, IReadOnlyDictionary<string, string> aprendido)
    {
        var plano = Interprete.Aplanar(comercio);
        if (aprendido.TryGetValue(plano, out var exacta) && Categorias.Todas.ContainsKey(exacta)) return exacta;
        var dentro = aprendido.Where(k => k.Key.Length >= 4 && plano.Contains(k.Key) && Categorias.Todas.ContainsKey(k.Value)).OrderByDescending(k => k.Key.Length).FirstOrDefault();
        return dentro.Key is null ? "otros" : dentro.Value;
    }

    internal static async Task<string> TextoPresupuesto(AppDbContext db, Movimiento m, DateOnly hoy, CancellationToken ct)
    {
        if (m.Tipo != TipoMovimiento.Gasto || await db.Presupuestos.FindAsync([m.Categoria], ct) is not { Limite: > 0 } p) return "";
        var desde = new DateOnly(hoy.Year, hoy.Month, 1);
        var gastado = await db.Movimientos.Where(x => x.Tipo == TipoMovimiento.Gasto && x.Categoria == m.Categoria && x.Fecha >= desde && x.Fecha < desde.AddMonths(1))
            .SumAsync(x => (decimal?)x.Importe, ct) ?? 0;
        return $" · {Categorias.Todas[m.Categoria].Nombre.ToLowerInvariant()} al {Math.Round(gastado / p.Limite * 100, MidpointRounding.AwayFromZero)} % del presupuesto";
    }
}
