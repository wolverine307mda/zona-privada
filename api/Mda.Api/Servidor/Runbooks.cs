using System.Collections.Concurrent;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Pc;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Mda.Api.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Servidor;

// Un paso de un runbook. Tipo: aviso (Texto) · espera (Segundos) · comprobar (Valor: agente | base | direccion | vigilancia) · despertar (Valor: aparato; vacío = el PC principal)
// · escena (Valor: nombre) · casa (Valor: llegada | salida) · orden (Valor: bloquear | suspender | apagar | reiniciar | cancelar).
// Aprobar: el runbook se para ahí hasta que lo permitas con Face ID. Continuar: si el paso falla, sigue con el siguiente.
public record PasoRunbook(string Tipo, string? Texto = null, string? Valor = null, int? Segundos = null, bool Aprobar = false, bool Continuar = false);
public record RunbookEntrada(string? Nombre, string? Descripcion, List<PasoRunbook>? Pasos, bool Guardia = false);
// cómo va cada paso de una ejecución: pendiente | en-curso | esperando | ok | error | omitido
public record EstadoPaso(string Tipo, string Resumen, bool Aprobar, string Estado, string Salida);

public static class Runbooks
{
    public static readonly string[] Tipos = ["aviso", "espera", "comprobar", "despertar", "escena", "casa", "orden"];
    public static readonly string[] Comprobaciones = ["agente", "base", "direccion", "vigilancia"];
    public static readonly string[] Ordenes = ["bloquear", "suspender", "apagar", "reiniciar", "cancelar"];
    // estas cortan lo que estés haciendo en el PC: el paso tiene que pedir permiso
    public static readonly string[] OrdenesDelicadas = ["suspender", "apagar", "reiniciar"];
    public static readonly string[] EstadosCasa = ["llegada", "salida"];
    public const int MaxPasos = 20, MaxEspera = 300;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Dictionary<string, string[]> Validar(RunbookEntrada e)
    {
        var err = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 60) err["nombre"] = ["Pon un nombre (máximo 60 caracteres)."];
        if ((e.Descripcion?.Length ?? 0) > 200) err["descripcion"] = ["La descripción no puede pasar de 200 caracteres."];
        if (e.Pasos is not { Count: > 0 }) { err["pasos"] = ["Añade al menos un paso."]; return err; }
        if (e.Pasos.Count > MaxPasos) { err["pasos"] = [$"Como mucho {MaxPasos} pasos."]; return err; }
        for (var i = 0; i < e.Pasos.Count; i++)
        {
            var p = e.Pasos[i];
            string? mal = p.Tipo switch
            {
                "aviso" => string.IsNullOrWhiteSpace(p.Texto) || p.Texto.Length > 300 ? "escribe el aviso (máximo 300 caracteres)" : null,
                "espera" => p.Segundos is null or < 1 or > MaxEspera ? $"la espera es de 1 a {MaxEspera} segundos" : null,
                "comprobar" => Comprobaciones.Contains(p.Valor ?? "") ? null : $"comprueba una de estas cosas: {string.Join(", ", Comprobaciones)}",
                "despertar" => (p.Valor?.Length ?? 0) > 60 ? "el nombre del aparato es demasiado largo" : null,
                "escena" => string.IsNullOrWhiteSpace(p.Valor) || p.Valor.Length > 60 ? "pon el nombre de la escena" : null,
                "casa" => EstadosCasa.Contains(p.Valor ?? "") ? null : "elige llegada o salida",
                "orden" => !Ordenes.Contains(p.Valor ?? "") ? $"elige una orden: {string.Join(", ", Ordenes)}"
                    : OrdenesDelicadas.Contains(p.Valor!) && !p.Aprobar ? $"«{p.Valor}» corta lo que haces en el PC: ese paso tiene que pedir permiso" : null,
                _ => "tipo de paso desconocido",
            };
            if (mal is not null) { err[$"pasos[{i}]"] = [$"Paso {i + 1}: {mal}."]; }
        }
        return err;
    }

    public static string Resumen(PasoRunbook p) => p.Tipo switch
    {
        "aviso" => $"Avisar: «{p.Texto}»",
        "espera" => $"Esperar {p.Segundos} s",
        "comprobar" => p.Valor switch { "agente" => "Comprobar el agente del PC", "base" => "Comprobar la base de datos", "direccion" => "Comprobar la dirección pública", "vigilancia" => "Comprobar las vigilancias", _ => "Comprobar" },
        "despertar" => string.IsNullOrWhiteSpace(p.Valor) ? "Despertar el PC principal" : $"Despertar «{p.Valor}»",
        "escena" => $"Aplicar la escena «{p.Valor}»",
        "casa" => p.Valor == "llegada" ? "Llegué a casa" : "Me voy de casa",
        "orden" => $"Orden al PC: {p.Valor}",
        _ => p.Tipo,
    };

    public static List<PasoRunbook> LeerPasos(string json)
    {
        try { return JsonSerializer.Deserialize<List<PasoRunbook>>(json, Json) ?? []; } catch (JsonException) { return []; }
    }

    public static string Serializar<T>(T valor) => JsonSerializer.Serialize(valor, Json);

    // runbooks de partida, para que la pantalla no esté vacía la primera vez
    public static async Task Sembrar(AppDbContext db, CancellationToken ct)
    {
        if (await db.Runbooks.AnyAsync(ct)) return;
        db.Runbooks.Add(new Runbook
        {
            Nombre = "Comprobar todo", Descripcion = "Mira la base, el agente, la dirección pública y las vigilancias, y te cuenta cómo está.", Guardia = true,
            Pasos = Serializar(new List<PasoRunbook>
            {
                new("comprobar", Valor: "base", Continuar: true), new("comprobar", Valor: "agente", Continuar: true),
                new("comprobar", Valor: "direccion", Continuar: true), new("comprobar", Valor: "vigilancia", Continuar: true),
                new("aviso", Texto: "Comprobación terminada: mira el resultado en Servidor › Runbooks."),
            }),
        });
        await db.SaveChangesAsync(ct);
    }

    public static void MapRunbooks(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/servidor").RequireAuthorization();

        object Fila(Runbook r, EjecucionRunbook? ultima) => new
        {
            r.Id, r.Nombre, r.Descripcion, r.Guardia, pasos = LeerPasos(r.Pasos).Select(p => new { p.Tipo, p.Texto, p.Valor, p.Segundos, p.Aprobar, p.Continuar, resumen = Resumen(p) }),
            ultima = ultima is null ? null : new { ultima.Id, ultima.Estado, ultima.Inicio, ultima.Fin },
        };

        g.MapGet("/runbooks", async (AppDbContext db, CancellationToken ct) =>
        {
            await Sembrar(db, ct);
            var todos = await db.Runbooks.AsNoTracking().OrderBy(r => r.Nombre).ToListAsync(ct);
            var ultimas = (await db.EjecucionesRunbook.AsNoTracking().Where(x => x.RunbookId != null).OrderByDescending(x => x.Inicio).Take(300).ToListAsync(ct))
                .GroupBy(x => x.RunbookId!.Value).ToDictionary(x => x.Key, x => x.First());
            return Results.Ok(todos.Select(r => Fila(r, ultimas.GetValueOrDefault(r.Id))));
        });

        g.MapPost("/runbooks", async (RunbookEntrada e, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            if (Validar(e) is { Count: > 0 } err) return Results.ValidationProblem(err);
            var nombre = e.Nombre!.Trim();
            if (await db.Runbooks.AnyAsync(r => r.Nombre.ToLower() == nombre.ToLower(), ct)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Ya hay un runbook con ese nombre."] });
            var r = new Runbook { Nombre = nombre, Descripcion = e.Descripcion?.Trim() ?? "", Guardia = e.Guardia, Pasos = Serializar(e.Pasos), Creado = reloj.GetUtcNow() };
            db.Runbooks.Add(r);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/servidor/runbooks/{r.Id}", Fila(r, null));
        });

        g.MapPut("/runbooks/{id:int}", async (int id, RunbookEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (Validar(e) is { Count: > 0 } err) return Results.ValidationProblem(err);
            if (await db.Runbooks.FindAsync([id], ct) is not { } r) return Results.NotFound();
            var nombre = e.Nombre!.Trim();
            if (await db.Runbooks.AnyAsync(x => x.Nombre.ToLower() == nombre.ToLower() && x.Id != id, ct)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Ya hay un runbook con ese nombre."] });
            (r.Nombre, r.Descripcion, r.Guardia, r.Pasos) = (nombre, e.Descripcion?.Trim() ?? "", e.Guardia, Serializar(e.Pasos));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("/runbooks/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Runbooks.FindAsync([id], ct) is not { } r) return Results.NotFound();
            db.Runbooks.Remove(r);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("/runbooks/{id:int}/ejecutar", async (int id, AppDbContext db, RunbookRunner runner, CancellationToken ct) =>
        {
            if (await db.Runbooks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct) is not { } r) return Results.NotFound();
            return await runner.Lanzar(r, "app") is { } ejecucion ? Results.Accepted($"/api/servidor/ejecuciones/{ejecucion}", new { ejecucion })
                : Results.Conflict(new { mensaje = "Ese runbook ya se está ejecutando." });
        });

        object Ejec(EjecucionRunbook x) => new { x.Id, x.RunbookId, x.Nombre, x.Origen, x.Inicio, x.Fin, x.Estado, pasos = JsonSerializer.Deserialize<List<EstadoPaso>>(x.Pasos, Json) ?? [] };

        g.MapGet("/ejecuciones", async (int? max, AppDbContext db, CancellationToken ct) =>
            Results.Ok((await db.EjecucionesRunbook.AsNoTracking().OrderByDescending(x => x.Inicio).Take(Math.Clamp(max ?? 20, 1, 100)).ToListAsync(ct)).Select(Ejec)));

        g.MapGet("/ejecuciones/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
            await db.EjecucionesRunbook.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) is { } x ? Results.Ok(Ejec(x)) : Results.NotFound());

        // dejar pasar el paso que espera: Face ID (o contraseña) justo antes
        g.MapPost("/ejecuciones/{id:int}/aprobar", async (int id, RunbookRunner runner, AppDbContext db, HttpContext ctx, TimeProvider reloj) =>
        {
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            return runner.Resolver(id, true) ? Results.NoContent() : Results.Conflict(new { mensaje = "Esa ejecución no está esperando permiso." });
        });

        g.MapPost("/ejecuciones/{id:int}/cancelar", (int id, RunbookRunner runner) =>
            runner.Cancelar(id) ? Results.NoContent() : Results.Conflict(new { mensaje = "Esa ejecución ya terminó." }));
    }
}

// Ejecuta los runbooks en segundo plano (uno por ejecución) y deja cada paso apuntado en la base según avanza, para verlo en vivo.
public sealed class RunbookRunner(IServiceScopeFactory scopes, TimeProvider reloj, ILogger<RunbookRunner> log)
{
    public static readonly TimeSpan EsperaPermiso = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _permisos = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _cancelaciones = new();
    private readonly ConcurrentDictionary<int, int> _enCurso = new();   // runbook → ejecución

    // null = ya hay una ejecución de ese runbook en curso
    public async Task<int?> Lanzar(Runbook rb, string origen)
    {
        if (!_enCurso.TryAdd(rb.Id, 0)) return null;
        try
        {
            var pasos = Runbooks.LeerPasos(rb.Pasos);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = new EjecucionRunbook
            {
                RunbookId = rb.Id, Nombre = rb.Nombre, Origen = origen, Inicio = reloj.GetUtcNow(),
                Pasos = Runbooks.Serializar(pasos.Select(p => new EstadoPaso(p.Tipo, Runbooks.Resumen(p), p.Aprobar, "pendiente", "")).ToList()),
            };
            db.EjecucionesRunbook.Add(e);
            await db.SaveChangesAsync();
            await ServidorLog.Anotar(db, reloj, "runbook", $"Runbook «{rb.Nombre}» lanzado", $"desde {origen}");
            _enCurso[rb.Id] = e.Id;
            var cts = _cancelaciones[e.Id] = new CancellationTokenSource();
            _ = Task.Run(() => Ejecutar(e.Id, rb.Id, rb.Nombre, pasos, cts.Token));
            return e.Id;
        }
        catch { _enCurso.TryRemove(rb.Id, out _); throw; }
    }

    // dar (o negar) el permiso a un paso que espera
    public bool Resolver(int ejecucion, bool permitir) => _permisos.TryRemove(ejecucion, out var t) && t.TrySetResult(permitir);

    public bool Cancelar(int ejecucion)
    {
        if (!_cancelaciones.TryGetValue(ejecucion, out var cts)) return false;
        cts.Cancel();
        Resolver(ejecucion, false);
        return true;
    }

    private async Task Ejecutar(int id, int runbookId, string nombre, List<PasoRunbook> pasos, CancellationToken ct)
    {
        var estados = pasos.Select(p => new EstadoPaso(p.Tipo, Runbooks.Resumen(p), p.Aprobar, "pendiente", "")).ToList();
        var final = "ok";
        string? falloEn = null;
        try
        {
            for (var i = 0; i < pasos.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = pasos[i];
                if (p.Aprobar)
                {
                    estados[i] = estados[i] with { Estado = "esperando", Salida = "Esperando tu permiso (Face ID)." };
                    await Guardar(id, "esperando", estados);
                    var tcs = _permisos[id] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    await AvisarPermiso(id, nombre, Runbooks.Resumen(p));
                    using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    limite.CancelAfter(EsperaPermiso);
                    using var _ = limite.Token.Register(() => tcs.TrySetResult(false));
                    var permitido = await tcs.Task;
                    _permisos.TryRemove(id, out var __);
                    if (!permitido) { estados[i] = estados[i] with { Estado = "omitido", Salida = ct.IsCancellationRequested ? "Cancelado." : "No se dio el permiso a tiempo." }; falloEn = Runbooks.Resumen(p); final = ct.IsCancellationRequested ? "cancelado" : "error"; break; }
                }
                estados[i] = estados[i] with { Estado = "en-curso", Salida = "" };
                await Guardar(id, "en-curso", estados);
                (bool ok, string salida) r;
                try { r = await Paso(p, nombre, ct); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Fallo en un paso del runbook {Nombre}", nombre); r = (false, "Falló: " + e.Message); }
                estados[i] = estados[i] with { Estado = r.ok ? "ok" : "error", Salida = r.salida };
                await Guardar(id, "en-curso", estados);
                if (!r.ok && !p.Continuar) { falloEn = Runbooks.Resumen(p); final = "error"; break; }
                if (!r.ok) final = "error";
            }
        }
        catch (OperationCanceledException) { final = "cancelado"; }
        catch (Exception e) { log.LogError(e, "Fallo al ejecutar el runbook {Nombre}", nombre); final = "error"; falloEn ??= "un error interno"; }
        finally
        {
            for (var i = 0; i < estados.Count; i++) if (estados[i].Estado is "pendiente" or "esperando") estados[i] = estados[i] with { Estado = "omitido" };
            await Guardar(id, final, estados, terminado: true);
            _cancelaciones.TryRemove(id, out var cts); cts?.Dispose();
            _permisos.TryRemove(id, out _);
            _enCurso.TryRemove(runbookId, out _);
            await Cerrar(id, nombre, final, estados, falloEn);
        }
    }

    private async Task Guardar(int id, string estado, List<EstadoPaso> pasos, bool terminado = false)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.EjecucionesRunbook.FindAsync(id) is not { } e) return;
        e.Estado = estado; e.Pasos = Runbooks.Serializar(pasos);
        if (terminado) e.Fin = reloj.GetUtcNow();
        await db.SaveChangesAsync();
    }

    private async Task AvisarPermiso(int id, string nombre, string paso)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var bot = sp.GetRequiredService<Bot>();
        var url = sp.GetRequiredService<IConfiguration>()["App:UrlPublica"] is { Length: > 0 } u ? u.TrimEnd('/') + "/servidor" : null;
        var botones = new List<object[]>();
        if (url is not null && url.StartsWith("https://", StringComparison.Ordinal)) botones.Add([new { text = "✅ Abrir y permitir con Face ID", url }]);
        botones.Add([new { text = "🛑 Cancelar el runbook", callback_data = $"rb:no:{id}" }]);
        if (bot.Vinculado) await bot.Enviar($"⏸️ <b>«{System.Net.WebUtility.HtmlEncode(nombre)}»</b> espera tu permiso:\n{System.Net.WebUtility.HtmlEncode(paso)}", new { inline_keyboard = botones }, html: true);
        await sp.GetRequiredService<IAvisos>().PorPush($"«{nombre}» espera tu permiso", paso, "/servidor");
    }

    private async Task Cerrar(int id, string nombre, string estado, List<EstadoPaso> pasos, string? falloEn)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var texto = estado switch
            {
                "ok" => $"✅ Runbook «{nombre}» terminado ({pasos.Count} pasos).",
                "cancelado" => $"🛑 Runbook «{nombre}» cancelado.",
                _ => $"⚠️ Runbook «{nombre}» terminó con fallos{(falloEn is not null ? $" (se detuvo en: {falloEn})" : "")}.\n" + string.Join("\n", pasos.Where(p => p.Estado == "error").Select(p => $"• {p.Resumen}: {p.Salida}")),
            };
            await ServidorLog.Anotar(db, reloj, "runbook", $"Runbook «{nombre}» {(estado == "ok" ? "terminado" : estado == "cancelado" ? "cancelado" : "con fallos")}", falloEn ?? "", estado == "ok" ? "info" : "aviso");
            await sp.GetRequiredService<IAvisos>().PorTelegram(texto);
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "No se pudo dar por cerrado el runbook {Nombre}", nombre); }
    }

    // ejecuta un paso en su propio ámbito (un DbContext por paso) y devuelve si fue bien y qué decir
    private async Task<(bool Ok, string Salida)> Paso(PasoRunbook p, string nombre, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var agente = sp.GetRequiredService<EstadoAgente>();
        var avisos = sp.GetRequiredService<IAvisos>();
        var desde = $"Runbook «{nombre}»";
        switch (p.Tipo)
        {
            case "aviso":
                var ok = await avisos.PorTelegram($"📋 {nombre}: {p.Texto}", ct);
                ok |= await avisos.PorPush(nombre, p.Texto!, "/servidor", ct);
                return (ok, ok ? "Aviso enviado." : "No hay ningún canal de aviso disponible.");
            case "espera":
                await Task.Delay(TimeSpan.FromSeconds(p.Segundos ?? 1), reloj, ct);
                return (true, $"Esperé {p.Segundos} s.");
            case "comprobar":
                return await Comprobar(p.Valor!, db, agente, sp, ct);
            case "despertar":
                {
                    var d = string.IsNullOrWhiteSpace(p.Valor) ? await PcPrincipal.Leer(db, ct)
                        : (await db.DispositivosRed.AsNoTracking().ToListAsync(ct)).FirstOrDefault(x => string.Equals(x.Nombre, p.Valor.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (d is null) return (false, string.IsNullOrWhiteSpace(p.Valor) ? "No hay un PC principal elegido (Red)." : $"No hay ningún aparato llamado «{p.Valor}» en Red.");
                    return await PcPrincipal.Despertar(d, db, agente, reloj, desde, ct);
                }
            case "escena":
                {
                    var e = (await db.Escenas.AsNoTracking().ToListAsync(ct)).FirstOrDefault(x => string.Equals(x.Nombre, p.Valor!.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (e is null) return (false, $"No hay ninguna escena llamada «{p.Valor}».");
                    if (e.ApagarMinutos is not null) return (false, "Esa escena apaga el PC: aplícala desde la app, con Face ID.");
                    return await Escenas.Aplicar(e, db, agente, avisos, reloj, desde, ct);
                }
            case "casa":
                return await Casa.Aplicar(p.Valor!, db, agente, avisos, reloj, desde, ct);
            case "orden":
                {
                    if (!agente.Conectado(OrdenesPc.AgenteConectado)) return (false, "El agente del PC no está en marcha.");
                    var o = await OrdenesPc.Encolar(db, reloj, p.Valor!, "", desde, ct);
                    return (true, $"Orden «{p.Valor}» enviada al agente (n.º {o.Id}).");
                }
            default: return (false, "Tipo de paso desconocido.");
        }
    }

    private static async Task<(bool, string)> Comprobar(string que, AppDbContext db, EstadoAgente agente, IServiceProvider sp, CancellationToken ct)
    {
        switch (que)
        {
            case "base": return await db.Database.CanConnectAsync(ct) ? (true, "La base de datos contesta.") : (false, "La base de datos no contesta.");
            case "agente": return agente.Conectado(OrdenesPc.AgenteConectado) ? (true, "El agente del PC está conectado.") : (false, "El agente del PC no está conectado.");
            case "direccion":
                {
                    var u = sp.GetRequiredService<EstadoDireccion>().Ultimo;
                    if (u is null) return (false, "Aún no se ha comprobado la dirección pública.");
                    return u.Responde ? (true, $"La dirección pública responde{(u.DiasCertificado is { } d ? $"; el certificado caduca en {d} días" : "")}.") : (false, $"La dirección pública no responde: {u.Error ?? "sin respuesta"}.");
                }
            case "vigilancia":
                {
                    var mal = sp.GetRequiredService<Mda.Api.Avisos.RegistroVigilancia>().Pasos().Where(x => !x.UltimoOk).ToList();
                    return mal.Count == 0 ? (true, "Todas las vigilancias van bien.") : (false, "Fallan: " + string.Join(", ", mal.Select(x => x.Nombre)));
                }
            default: return (false, "Comprobación desconocida.");
        }
    }
}
