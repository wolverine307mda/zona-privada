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
// Atajos manda el importe como lo escribe el teléfono: 3,26 o "3,26" (texto con coma). Se acepta número, texto con coma o con punto.
public class ImporteFlexible : System.Text.Json.Serialization.JsonConverter<decimal>
{
    public override decimal Read(ref System.Text.Json.Utf8JsonReader r, Type tipo, System.Text.Json.JsonSerializerOptions o)
    {
        if (r.TokenType == System.Text.Json.JsonTokenType.Number) return r.GetDecimal();
        if (TryParsear(r.GetString(), out var v)) return v;
        throw new System.Text.Json.JsonException("Importe no válido");
    }

    // «3,26», «11,75 €» (con el espacio de no separación que pone Atajos), «EUR 3.26», «1.234,56 €», «1,234.56», «-4,50»:
    // se quita todo menos cifras, signo y separadores, y el último separador es el decimal
    public static bool TryParsear(string? texto, out decimal valor)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(texto ?? "", "[^0-9.,-]", "");
        var coma = t.LastIndexOf(','); var punto = t.LastIndexOf('.');
        if (coma >= 0 && punto >= 0) t = coma > punto ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        else if (coma >= 0) t = t.Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor);
    }
    public override void Write(System.Text.Json.Utf8JsonWriter w, decimal v, System.Text.Json.JsonSerializerOptions o) => w.WriteNumberValue(v);
}

public record MovimientoAtajo([property: System.Text.Json.Serialization.JsonConverter(typeof(ImporteFlexible))] decimal Importe, string Concepto, string? Tipo, string? Categoria, DateOnly? Fecha);
public record FraseAtajo(string Texto);
public record PagoAtajo(string Comercio, [property: System.Text.Json.Serialization.JsonConverter(typeof(ImporteFlexible))] decimal Importe, string? Tarjeta, double? Lat = null, double? Lon = null);
public record PingAtajo(string? Texto);
public record CasaAtajo(string Estado);
public record NotaAtajo(string? Texto, string? Titulo);
public record DespertarAtajo(string? Nombre);

public static class AtajosApi
{
    public static readonly string[] Alcances = ["gasto", "pago", "lectura", "casa", "nota", "pc", "panico"];
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

    // si el cuerpo ni siquiera se pudo leer (JSON mal formado), el framework responde 400 vacío antes de llegar al endpoint: se le pone mensaje
    public static IApplicationBuilder UseMensajeAtajos(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        await next();
        if (ctx.Request.Path.StartsWithSegments("/api/atajos") && ctx.Response.StatusCode >= 400 && !ctx.Response.HasStarted && ctx.Response.ContentLength is null && ctx.Response.ContentType is null)
        {
            ctx.Response.ContentType = "application/json; charset=utf-8";
            var mensaje = ctx.Response.StatusCode == 400 ? "No he entendido los datos del atajo: revisa el cuerpo JSON (importe, concepto, tipo…)." : "No se ha podido completar.";
            await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { mensaje }));
        }
    });

    // un error sin cuerpo (401, 400, 502…) o con un ProblemDetails se convierte en {mensaje}; lo que ya trae `mensaje` o datos se deja igual
    internal static object? ConMensaje(object? r)
    {
        if (r is not IStatusCodeHttpResult { StatusCode: >= 400 and var codigo } || r is IValueHttpResult { Value: not null and not Microsoft.AspNetCore.Mvc.ProblemDetails }) return r;
        var texto = r is IValueHttpResult { Value: Microsoft.AspNetCore.Http.HttpValidationProblemDetails v } && v.Errors.Values.SelectMany(x => x).FirstOrDefault() is { Length: > 0 } primero ? primero
            : r is IValueHttpResult { Value: Microsoft.AspNetCore.Mvc.ProblemDetails { Detail: { Length: > 0 } detalle } } ? detalle
            : codigo switch
            {
                401 => "Token no válido o sin permiso para este atajo.",
                400 => "Faltan datos o no son válidos.",
                404 => "No existe lo que pides.",
                429 => "Demasiadas peticiones seguidas: espera un momento.",
                >= 500 => "El servidor no ha podido hacerlo.",
                _ => $"No se ha podido completar (error {codigo}).",
            };
        return Results.Json(new { mensaje = texto }, statusCode: codigo);
    }

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
            // revocar = borrar: sin el token guardado la huella ya no existe y deja de valer; no se queda un «deshabilitado» en la lista
            db.TokensAtajo.Remove(t);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- lo que usa el iPhone (con token) ----
        var a = app.MapGroup("/api/atajos").AllowAnonymous().RequireRateLimiting(Limites.Atajos);
        // todos los atajos contestan con un `mensaje` legible, también cuando algo falla: el atajo del iPhone lo enseña tal cual en la notificación
        a.AddEndpointFilter(async (ctx, next) => ConMensaje(await next(ctx)));

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
            var saldo = await Cartera.SaldoActual(db, hoy, ct);
            var agenda = await AsistenteEndpoints.Agenda(db, zona, hoy, hoy, ct);
            return Results.Ok(new { saldo, saldoTexto = Cartera.Euros(saldo), agenda, mensaje = $"Libre: {Cartera.Euros(saldo)}. {agenda}" });
        }).Requiere("lectura");

        // todo lo de hoy en una respuesta (gasto, presupuestos, agenda, hábitos, mejor salida) y ya escrito en `lineas`: para un widget de Scriptable
        a.MapGet("/hoy", async (AppDbContext db, Luz.ServicioLuz luz, Tiempo.ServicioTiempo tiempo, TimeProvider reloj, Zona zona, CancellationToken ct) =>
            Results.Ok(await Hoy.Componer(db, luz, tiempo, zona, reloj, ct))).Requiere("lectura");

        // el iPhone avisa (hora, lugar…) y el bot te pregunta: respondes desde la notificación de Telegram y el asistente lo guarda
        a.MapPost("/ping", async (PingAtajo e, Bot bot, CancellationToken ct) =>
        {
            if (!bot.Vinculado) return Results.Conflict(new { mensaje = "Telegram no está vinculado." });
            var texto = string.IsNullOrWhiteSpace(e.Texto) ? "¿Qué has gastado?" : e.Texto.Trim()[..Math.Min(e.Texto.Trim().Length, 120)];
            var ok = await bot.Enviar($"💬 {System.Net.WebUtility.HtmlEncode(texto)}", new { force_reply = true, input_field_placeholder = "12,50 en el Mercadona" }, html: true, ct: ct);
            return ok ? Results.Ok(new { mensaje = "Aviso enviado a Telegram." }) : Results.Json(new { mensaje = "Telegram no ha respondido." }, statusCode: 502);
        }).Requiere("gasto");

        // «Llegué a casa» / «Me voy»: una automatización por lugar del iPhone manda {"estado":"llegada"} o {"estado":"salida"}
        a.MapPost("/casa", async (CasaAtajo e, AppDbContext db, Mda.Api.Sistema.EstadoAgente agente, IAvisos avisos, Mda.Api.Contexto.MotorContexto contexto, Mda.Api.Domotica.ServicioCasa casa, Bot bot, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            var estado = (e.Estado ?? "").ToLowerInvariant();
            // además de lo de siempre, se apunta dónde estás y se aplican tus escenas por contexto (aunque el agente del PC no esté en marcha)
            if (estado is "llegada" or "salida") await contexto.Evento(db, estado == "llegada" ? "llego" : "salgo", ct);
            var (ok, mensaje) = await Mda.Api.Pc.Casa.Aplicar(estado, db, agente, avisos, reloj, "Atajo · " + IpCliente.De(ctx), ct);
            // al salir, además, la comprobación de la casa: puertas abiertas y aparatos que se quedan encendidos
            if (estado == "salida") { var salida = await Mda.Api.Contexto.SalidaCasa.Ejecutar(db, casa, bot, reloj.GetUtcNow(), ct); mensaje = ok ? $"{mensaje} {salida}" : salida; return Results.Ok(new { mensaje }); }
            return ok ? Results.Ok(new { mensaje }) : Results.Conflict(new { mensaje });
        }).Requiere("casa");

        // «Me voy a dormir» / «Despierto»: la Concentración «Dormir» del iPhone manda {"estado":"duermo"} y la contesta con lo de mañana
        a.MapPost("/dormir", async (CasaAtajo e, AppDbContext db, Mda.Api.Contexto.MotorContexto contexto, Mda.Api.Tiempo.ServicioTiempo tiempo, Zona zona, TimeProvider reloj, CancellationToken ct) =>
        {
            var estado = (e.Estado ?? "").ToLowerInvariant();
            if (estado is not ("duermo" or "despierto")) return Results.BadRequest(new { mensaje = "Manda estado «duermo» o «despierto»." });
            return Results.Ok(new { mensaje = await Mda.Api.Contexto.Dormir.Ejecutar(db, contexto, tiempo, zona, reloj, estado == "duermo", ct) });
        }).Requiere("casa");

        // «Ropa tendida» / «Ropa recogida»: un atajo del iPhone manda {"estado":"tendida"} o {"estado":"recogida"}
        a.MapPost("/ropa", async (CasaAtajo e, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            var estado = (e.Estado ?? "").ToLowerInvariant();
            if (estado is "tendida" or "tender") { await Mda.Api.Tiempo.Ropa.Tender(db, reloj.GetUtcNow(), ct); return Results.Ok(new { mensaje = "Ropa tendida. Te aviso si va a llover." }); }
            if (estado is "recogida" or "recoger") { await Mda.Api.Tiempo.Ropa.Recoger(db, ct); return Results.Ok(new { mensaje = "Ropa recogida." }); }
            return Results.BadRequest(new { mensaje = "Manda estado «tendida» o «recogida»." });
        }).Requiere("casa");

        // Una escena del PC por su nombre («Cine», «Trabajo»…); las que apagan el PC se aplican solo desde la app, con Face ID
        a.MapPost("/escena", async (Mda.Api.Pc.EscenaAtajo e, AppDbContext db, Mda.Api.Sistema.EstadoAgente agente, IAvisos avisos, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            var nombre = (e.Nombre ?? "").Trim();
            var escena = (await db.Escenas.AsNoTracking().ToListAsync(ct)).FirstOrDefault(x => string.Equals(x.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
            if (escena is null) return Results.NotFound(new { mensaje = "No hay ninguna escena con ese nombre." });
            if (escena.ApagarMinutos is not null) return Results.Conflict(new { mensaje = "Esa escena apaga el PC: aplícala desde la app, con Face ID." });
            var (ok, mensaje) = await Mda.Api.Pc.Escenas.Aplicar(escena, db, agente, avisos, reloj, "Atajo · " + IpCliente.De(ctx), ct);
            return ok ? Results.Ok(new { mensaje }) : Results.Conflict(new { mensaje });
        }).Requiere("casa");

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
            if (Lugares.Valida(e.Lat, e.Lon)) { m.Lat = Lugares.Redondear(e.Lat!.Value); m.Lon = Lugares.Redondear(e.Lon!.Value); }
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

        // «Nota rápida»: dictas o escribes y queda en Notas con la etiqueta «atajo»
        a.MapPost("/nota", async (NotaAtajo e, AppDbContext db, CancellationToken ct) =>
        {
            var texto = (e.Texto ?? "").Trim();
            if (texto.Length == 0 || texto.Length > 5000 || (e.Titulo?.Length ?? 0) > 200) return Results.BadRequest(new { mensaje = "La nota está vacía o es demasiado larga." });
            var n = new Mda.Api.Notas.Nota { Titulo = e.Titulo?.Trim() ?? "", Contenido = texto, Etiquetas = "atajo" };
            db.Notas.Add(n);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = n.Id, mensaje = "Nota guardada." });
        }).Requiere("nota");

        // «Encender el PC»: despierta por Wake-on-LAN un aparato de tu red (por su nombre) a través del agente
        a.MapPost("/despertar", async (DespertarAtajo e, AppDbContext db, Mda.Api.Sistema.EstadoAgente agente, IAvisos avisos, TimeProvider reloj, HttpContext ctx, CancellationToken ct) =>
        {
            var nombre = (e.Nombre ?? "").Trim();
            var d = (await db.DispositivosRed.AsNoTracking().ToListAsync(ct)).FirstOrDefault(x => string.Equals(x.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
            if (d is null) return Results.NotFound(new { mensaje = "No hay ningún aparato con ese nombre en Red." });
            if (d.Mac.Length == 0) return Results.Conflict(new { mensaje = "Ese aparato no tiene dirección MAC." });
            if (!agente.Conectado(Mda.Api.Pc.OrdenesPc.AgenteConectado)) return Results.Conflict(new { mensaje = "El agente del PC no está en marcha." });
            var desde = "Atajo · " + IpCliente.De(ctx);
            await Mda.Api.Pc.OrdenesPc.Encolar(db, reloj, "wol", d.Mac, desde, ct);
            _ = avisos.PorTelegram($"💻 Orden al PC: despertar «{d.Nombre}»\nDesde {desde}. Si no has sido tú, revoca el atajo en Seguridad.");
            return Results.Ok(new { mensaje = $"Despertando «{d.Nombre}»." });
        }).Requiere("pc");

        // «Estado»: el semáforo de Inicio en una frase y por áreas, para que Siri lo lea o un widget lo pinte
        a.MapGet("/estado", async (AppDbContext db, Mda.Api.Avisos.Vigilante vigilante, Mda.Api.Sistema.EstadoAgente agente, EstadoFunnel funnel, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var s = await SemaforoVida.Calcular(db, vigilante, agente, funnel, reloj, zona, ct);
            var mal = s.Areas.Where(x => x.Estado != SemaforoVida.Verde).ToList();
            var mensaje = mal.Count == 0 ? "Todo en orden." : string.Join(" ", mal.Select(x => $"{x.Nombre}: {x.Motivo}"));
            return Results.Ok(new { estado = s.Estado, mensaje, areas = s.Areas });
        }).Requiere("lectura");

        // «Pánico»: lo mismo que el botón de la app. Cierra sesiones y revoca los tokens (también este); se levanta con /desbloquear en Telegram
        a.MapPost("/panico", async (AppDbContext db, TimeProvider reloj, IMemoryCache cache, IAvisos avisos, HttpContext ctx, CancellationToken ct) =>
        {
            var r = await Panico.Ejecutar(db, reloj, cache, ct);
            await avisos.PorTelegram($"🚨 Botón de pánico pulsado desde un atajo del iPhone ({IpCliente.De(ctx)}).\n{Panico.Resumen(r)}\nPara volver a entrar antes, escribe /desbloquear.", ct);
            return Results.Ok(new { mensaje = Panico.Resumen(r) });
        }).Requiere("panico");
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
