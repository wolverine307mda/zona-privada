using System.Text.Json;
using System.Text.RegularExpressions;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Pc;

// Abrir y cerrar aplicaciones del PC desde la app. Abrir: solo las de un catálogo que eliges tú (el agente lee el menú Inicio y tú marcas cuáles valen);
// la orden lleva el identificador, nunca una ruta, y el agente comprueba que sigue en el menú Inicio. Cerrar: solo aplicaciones con ventana que salen en la última lista,
// por número Y hora de inicio, con cierre amable primero; «Forzar» pide la contraseña otra vez; los protegidos nunca se cierran.
public class AppPc
{
    public int Id { get; set; }
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string AppId { get; set; } = "";
    public bool Delicada { get; set; }
}

public record AppInstalada(string Nombre, string AppId);
public record AppsDisponibles(List<AppInstalada>? Apps);
public record AppElegida(string? AppId, string? Nombre, bool? Delicada);
public record ProcesoVentana(int Pid, long InicioUnix, string Nombre, string Titulo, double MemoriaMb, bool Protegido);
public record ProcesosEntrada(List<ProcesoVentana>? Procesos);
public record CierreEntrada(int Pid, long Inicio, bool? Forzar);

public static partial class AppsPcEndpoints
{
    public const int Maximo = 40;
    public const string ClaveApps = "pc-apps-disponibles", ClaveProcesos = "pc-procesos";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^[\p{L}\p{N}._!{}\-\\ ()]+$")]
    private static partial Regex CaracteresAppId();

    public static bool AppIdValido(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 260 && CaracteresAppId().IsMatch(id) && !id.Contains("..") && !id.StartsWith('\\') && !id.Contains(':');

    // una clave corta y única para el botón («Visual Studio Code» → «visual-studio-code»)
    public static string Clave(string nombre, IEnumerable<string> usadas)
    {
        var baseClave = Regex.Replace(Regex.Replace(nombre.Normalize(System.Text.NormalizationForm.FormD), @"\p{Mn}", "").ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (baseClave.Length == 0) baseClave = "app";
        baseClave = baseClave[..Math.Min(baseClave.Length, 32)].TrimEnd('-');
        var tomadas = usadas.ToHashSet();
        var c = baseClave; var i = 2;
        while (tomadas.Contains(c)) c = $"{baseClave}-{i++}";
        return c;
    }

    // Protegidos: los mismos que en el agente (que es quien manda; aquí se rechaza antes para avisar claro)
    private static readonly string[] Protegidos = ["explorer", "winlogon", "wininit", "csrss", "services", "lsass", "svchost", "dwm", "system", "smss", "fontdrvhost", "sihost", "taskhostw",
        "ctfmon", "searchhost", "startmenuexperiencehost", "shellexperiencehost", "runtimebroker", "audiodg", "spoolsv", "mda.api", "mda.agente", "mda.lanzador", "zonaprivada", "postgres", "docker", "dockerd", "com.docker.backend"];

    public static bool EsProtegido(string nombre)
    {
        var n = nombre.Trim().ToLowerInvariant();
        if (n.EndsWith(".exe")) n = n[..^4];
        return Protegidos.Contains(n) || n.StartsWith("tailscale") || n.StartsWith("postgres") || n.StartsWith("mda.");
    }

    private static async Task<T?> Leer<T>(AppDbContext db, string clave, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == clave, ct))?.Valor;
        try { return v is null ? default : JsonSerializer.Deserialize<T>(v, Json); } catch (JsonException) { return default; }
    }

    private static async Task Guardar(AppDbContext db, string clave, object valor, CancellationToken ct)
    {
        var f = await db.Ajustes.FirstOrDefaultAsync(a => a.Clave == clave, ct);
        var v = JsonSerializer.Serialize(valor, Json);
        if (f is null) db.Ajustes.Add(new Ajuste { Clave = clave, Valor = v }); else f.Valor = v;
        await db.SaveChangesAsync(ct);
    }

    public static void MapAppsPc(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pc/apps").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(new
        {
            catalogo = await db.AppsPc.AsNoTracking().OrderBy(a => a.Nombre).Select(a => new { a.Clave, a.Nombre, a.AppId, a.Delicada }).ToListAsync(ct),
            disponibles = (await Leer<AppsDisponibles>(db, ClaveApps, ct))?.Apps ?? [],
        }));

        // pide al agente que vuelva a leer el menú Inicio
        g.MapPost("/actualizar", async (AppDbContext db, EstadoAgente agente, TimeProvider reloj, CancellationToken ct) =>
        {
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha." });
            await OrdenesPc.Encolar(db, reloj, "apps", "", "catálogo de aplicaciones", ct);
            return Results.Accepted();
        });

        // el catálogo entero: solo se aceptan aplicaciones que el agente ha listado
        g.MapPut("/", async (List<AppElegida> elegidas, AppDbContext db, CancellationToken ct) =>
        {
            if (elegidas.Count > Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["apps"] = [$"Como mucho {Maximo} aplicaciones."] });
            var disponibles = ((await Leer<AppsDisponibles>(db, ClaveApps, ct))?.Apps ?? []).ToDictionary(a => a.AppId, StringComparer.OrdinalIgnoreCase);
            var nuevas = new List<AppPc>();
            foreach (var e in elegidas.DistinctBy(x => x.AppId))
            {
                if (!AppIdValido(e.AppId) || !disponibles.TryGetValue(e.AppId!, out var real))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["apps"] = ["Una de las aplicaciones no está en la lista que ha mandado el PC: actualízala."] });
                var nombre = string.IsNullOrWhiteSpace(e.Nombre) ? real.Nombre : e.Nombre.Trim()[..Math.Min(e.Nombre.Trim().Length, 80)];
                nuevas.Add(new AppPc { Nombre = nombre, AppId = real.AppId, Clave = Clave(nombre, nuevas.Select(n => n.Clave)), Delicada = e.Delicada ?? false });
            }
            db.AppsPc.RemoveRange(await db.AppsPc.ToListAsync(ct));
            db.AppsPc.AddRange(nuevas);
            await db.SaveChangesAsync(ct);
            return Results.Ok(nuevas.Select(a => new { a.Clave, a.Nombre, a.AppId, a.Delicada }));
        });

        // abrir: solo por la clave del catálogo
        g.MapPost("/{clave}/abrir", async (string clave, AppDbContext db, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            var a = await db.AppsPc.AsNoTracking().FirstOrDefaultAsync(x => x.Clave == clave, ct);
            if (a is null) return Results.NotFound();
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha." });
            if (a.Delicada && !await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            var o = await OrdenesPc.Encolar(db, reloj, "abrir-app", a.AppId, Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()) + " · " + IpCliente.De(ctx), ct);
            _ = avisos.PorTelegram($"💻 Orden al PC: abrir «{a.Nombre}»\nDesde {o.Desde}.");
            return Results.Ok(new { o.Id });
        }).RequireRateLimiting(Limites.Sensible);

        // ---- cerrar ----
        var p = app.MapGroup("/api/pc/procesos").RequireAuthorization();

        p.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok((await Leer<ProcesosEntrada>(db, ClaveProcesos, ct))?.Procesos ?? []));

        p.MapPost("/actualizar", async (AppDbContext db, EstadoAgente agente, TimeProvider reloj, CancellationToken ct) =>
        {
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha." });
            await OrdenesPc.Encolar(db, reloj, "procesos", "", "aplicaciones abiertas", ct);
            return Results.Accepted();
        });

        p.MapPost("/cerrar", async (CierreEntrada e, AppDbContext db, EstadoAgente agente, IAvisos avisos, HttpContext ctx, TimeProvider reloj, CancellationToken ct) =>
        {
            var lista = (await Leer<ProcesosEntrada>(db, ClaveProcesos, ct))?.Procesos ?? [];
            // solo lo que sale en la última lista y con su hora de inicio: así no se cierra «lo que haya» con ese número
            var x = lista.FirstOrDefault(v => v.Pid == e.Pid && Math.Abs(v.InicioUnix - e.Inicio) <= 2);
            if (x is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["pid"] = ["Esa aplicación no está en la lista: actualízala."] });
            if (x.Protegido || EsProtegido(x.Nombre)) return Results.Conflict(new { detalle = $"{x.Nombre} es un programa protegido: no se cierra desde aquí." });
            if (!agente.Conectado(OrdenesPc.AgenteConectado)) return Results.Conflict(new { detalle = "El agente de Windows no está en marcha." });
            // el cierre amable pide Face ID o contraseña como cualquier orden delicada; forzar, también
            if (!await Reautenticar.Vigente(ctx, db, reloj)) return Reautenticar.Pedir();
            var forzar = e.Forzar == true;
            var o = await OrdenesPc.Encolar(db, reloj, "cerrar", $"{x.Pid}:{x.InicioUnix}:{(forzar ? "forzar" : "amable")}", Dispositivo.Nombre(ctx.Request.Headers.UserAgent.ToString()) + " · " + IpCliente.De(ctx), ct);
            _ = avisos.PorTelegram($"💻 Orden al PC: {(forzar ? "forzar el cierre de" : "cerrar")} «{x.Nombre}»\nDesde {o.Desde}.");
            return Results.Ok(new { o.Id });
        }).RequireRateLimiting(Limites.Sensible);

        // ---- lo que manda el agente ----
        var a2 = app.MapGroup("/api/sistema/agente").AllowAnonymous().RequireRateLimiting(Limites.Agente);

        a2.MapPost("/apps", async (HttpRequest req, AppsDisponibles e, AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            var l = (e.Apps ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Nombre) && AppIdValido(x.AppId)).Take(300).ToList();
            await Guardar(db, ClaveApps, new AppsDisponibles(l), ct);
            return Results.NoContent();
        });

        a2.MapPost("/procesos", async (HttpRequest req, ProcesosEntrada e, AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            if (OrdenesPc.ComprobarAgente(req, config) is { } no) return no;
            var l = (e.Procesos ?? []).Where(x => x.Pid > 4 && x.InicioUnix > 0 && x.Nombre.Length is > 0 and <= 80).Take(80)
                .Select(x => x with { Titulo = x.Titulo.Length > 120 ? x.Titulo[..120] : x.Titulo, Protegido = x.Protegido || EsProtegido(x.Nombre) }).ToList();
            await Guardar(db, ClaveProcesos, new ProcesosEntrada(l), ct);
            return Results.NoContent();
        });
    }
}
