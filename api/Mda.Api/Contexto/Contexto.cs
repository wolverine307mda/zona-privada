using System.Globalization;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Domotica;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Mda.Api.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Contexto;

// Escenas que se ponen solas según el contexto: llegas o te vas de casa (el iPhone avisa con una automatización de Atajos), una hora del día, el tiempo que hace.
// «Cuando llegue a casa, de noche y si llueve, pon la escena Cine de casa y la del PC».
//   Evento:  llego (llegaste a casa) · salgo (te vas) · hora (todos los días a la hora de «Desde», una sola vez)
//   Franja:  Desde/Hasta (HH:mm; pasa de medianoche si Hasta < Desde) y los días de la semana
//   Tiempo:  lluvia · seco · frio (≤ 10 °) · calor (≥ 28 °); vacío = da igual
// Las escenas del PC que lo apagan nunca se aplican solas.
public class ReglaContexto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activa { get; set; } = true;
    public string Evento { get; set; } = "llego";
    public string? Desde { get; set; }
    public string? Hasta { get; set; }
    public int Dias { get; set; } = 127;                  // máscara como la de los hábitos: lunes = 1 … domingo = 64
    public string Tiempo { get; set; } = "";
    public bool SoloEnCasa { get; set; }                  // en las de «hora»: solo si estás en casa
    public int? EscenaCasaId { get; set; }
    public int? EscenaPcId { get; set; }
    public string Aviso { get; set; } = "";               // un mensaje por Telegram al aplicarla
    public int EsperaMinutos { get; set; } = 60;          // mínimo entre una vez y la siguiente
    public DateTimeOffset? Ultima { get; set; }
    public string Resultado { get; set; } = "";
}

public record ReglaContextoEntrada(string? Nombre, bool? Activa, string? Evento, string? Desde, string? Hasta, int? Dias, string? Tiempo, bool? SoloEnCasa, int? EscenaCasaId, int? EscenaPcId, string? Aviso, int? EsperaMinutos, bool QuitarEscenaCasa = false, bool QuitarEscenaPc = false);
public record EventoContexto(string? Evento);
public record PresenciaEntrada(string? Estado);

public static class ServicioContexto
{
    public const int Maximo = 20;
    public const string ClavePresencia = "contexto-presencia", ClaveDesde = "contexto-presencia-desde";
    public static readonly string[] Eventos = ["llego", "salgo", "hora"];
    public static readonly string[] Tiempos = ["", "lluvia", "seco", "frio", "calor"];

    public static bool HoraValida(string? h) => TimeOnly.TryParseExact(h, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    private static TimeOnly Hora(string h) => TimeOnly.ParseExact(h, "HH:mm", CultureInfo.InvariantCulture);

    // Códigos de la OMS que usa Open-Meteo: 51–67 llovizna y lluvia, 80–82 chubascos, 95–99 tormenta
    public static bool Llueve(int codigo) => codigo is >= 51 and <= 67 or >= 80 and <= 82 or >= 95 and <= 99;

    public static bool TiempoCumple(string tiempo, Actual? a)
    {
        if (tiempo.Length == 0) return true;
        if (a is null) return false;   // sin datos del tiempo no se arriesga: la regla espera
        return tiempo switch
        {
            "lluvia" => Llueve(a.Codigo),
            "seco" => !Llueve(a.Codigo),
            "frio" => a.Temperatura <= 10,
            "calor" => a.Temperatura >= 28,
            _ => true,
        };
    }

    // ¿La hora local cae en la franja? Sin franja, siempre; con una sola punta, desde/hasta ella.
    public static bool EnFranja(string? desde, string? hasta, TimeOnly t)
    {
        var d = HoraValida(desde) ? Hora(desde!) : (TimeOnly?)null;
        var h = HoraValida(hasta) ? Hora(hasta!) : (TimeOnly?)null;
        if (d is null && h is null) return true;
        if (d is null) return t <= h!.Value;
        if (h is null) return t >= d.Value;
        return d <= h ? t >= d && t <= h : t >= d || t <= h;
    }

    // Todo lo que tiene que cumplirse para que la regla se aplique ahora. `local` es la hora de tu zona; `ultimaLocal`, la de la última vez en tu zona.
    public static bool Cumple(ReglaContexto r, string evento, DateTime local, string presencia, Actual? clima, DateTimeOffset ahora, DateTime? ultimaLocal)
    {
        if (!r.Activa || r.Evento != evento) return false;
        if (!Habitos.Rachas.Toca(r.Dias, DateOnly.FromDateTime(local))) return false;
        var t = TimeOnly.FromDateTime(local);
        if (evento == "hora")
        {
            if (!HoraValida(r.Desde)) return false;
            // una vez al día, dentro de la hora siguiente a «Desde» (o hasta «Hasta» si la hay)
            var fin = HoraValida(r.Hasta) ? Hora(r.Hasta!) : Hora(r.Desde!).AddMinutes(60);
            var desde = Hora(r.Desde!);
            var dentro = desde <= fin ? t >= desde && t <= fin : t >= desde || t <= fin;
            if (!dentro) return false;
            if (ultimaLocal?.Date == local.Date) return false;
            if (r.SoloEnCasa && presencia != "casa") return false;
        }
        else if (!EnFranja(r.Desde, r.Hasta, t)) return false;
        if (r.Ultima is { } u && ahora - u < TimeSpan.FromMinutes(Math.Max(1, r.EsperaMinutos))) return false;
        return TiempoCumple(r.Tiempo, clima);
    }

    public static Dictionary<string, string[]> Validar(ReglaContextoEntrada e, bool nueva)
    {
        var mal = new Dictionary<string, string[]>();
        if (nueva || e.Nombre is not null) { if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 60) mal["nombre"] = ["Ponle un nombre (máximo 60 letras)."]; }
        if (e.Evento is not null && !Eventos.Contains(e.Evento)) mal["evento"] = ["El evento es llego, salgo u hora."];
        if (!string.IsNullOrEmpty(e.Desde) && !HoraValida(e.Desde)) mal["desde"] = ["La hora va como 20:30."];
        if (!string.IsNullOrEmpty(e.Hasta) && !HoraValida(e.Hasta)) mal["hasta"] = ["La hora va como 23:30."];
        if (e.Dias is { } d && d is < 1 or > 127) mal["dias"] = ["Elige al menos un día."];
        if (e.Tiempo is not null && !Tiempos.Contains(e.Tiempo)) mal["tiempo"] = ["El tiempo es lluvia, seco, frío o calor."];
        if (e.Aviso is { Length: > 120 }) mal["aviso"] = ["El aviso llega a 120 letras."];
        if (e.EsperaMinutos is { } w && w is < 1 or > 1440) mal["esperaMinutos"] = ["La espera va de 1 minuto a 24 horas."];
        return mal;
    }

    public static async Task<(string Estado, DateTimeOffset? Desde)> LeerPresencia(AppDbContext db, CancellationToken ct)
    {
        var e = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClavePresencia, ct))?.Valor;
        var d = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == ClaveDesde, ct))?.Valor;
        return (e is "casa" or "fuera" ? e : "?", DateTimeOffset.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var f) ? f : null);
    }

    public static async Task PonerPresencia(AppDbContext db, string estado, DateTimeOffset ahora, CancellationToken ct)
    {
        async Task Guardar(string clave, string valor)
        {
            var a = await db.Ajustes.FindAsync([clave], ct);
            if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = clave });
            a.Valor = valor;
        }
        await Guardar(ClavePresencia, estado);
        await Guardar(ClaveDesde, ahora.ToString("O", CultureInfo.InvariantCulture));
        await db.SaveChangesAsync(ct);
    }
}

// Cada minuto mira las reglas «a una hora»; las de llegar y salir las dispara `Evento` cuando el iPhone avisa
public sealed class MotorContexto(IServiceScopeFactory ambitos, ServicioCasa casa, EstadoAgente agente, IAvisos avisos, ServicioTiempo tiempo, TimeProvider reloj, Zona zona, RegistroVigilancia registro, ILogger<MotorContexto> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(90), reloj, ct);
            using var t = new PeriodicTimer(TimeSpan.FromMinutes(1), reloj);
            do
            {
                try { using var scope = ambitos.CreateScope(); await Disparar(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "hora", ct); registro.Anotar("escenas por contexto"); }
                catch (Exception e) when (e is not OperationCanceledException) { registro.Anotar("escenas por contexto", e); log.LogWarning(e, "No se pudieron evaluar las escenas por contexto"); }
            } while (await t.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { /* se apaga la API */ }
    }

    // Llegaste o te fuiste (lo manda el iPhone): se apunta dónde estás y se aplican las reglas de ese evento
    public async Task Evento(AppDbContext db, string evento, CancellationToken ct)
    {
        if (evento is not ("llego" or "salgo")) return;
        await ServicioContexto.PonerPresencia(db, evento == "llego" ? "casa" : "fuera", reloj.GetUtcNow(), ct);
        await Disparar(db, evento, ct);
    }

    public async Task Disparar(AppDbContext db, string evento, CancellationToken ct)
    {
        var reglas = await db.ReglasContexto.Where(r => r.Activa && r.Evento == evento).ToListAsync(ct);
        if (reglas.Count == 0) return;
        var ahora = reloj.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(ahora, zona.Info).DateTime;
        var (presencia, _) = await ServicioContexto.LeerPresencia(db, ct);
        // el tiempo solo se consulta si alguna regla lo pide
        Actual? clima = reglas.Any(r => r.Tiempo.Length > 0) ? (await tiempo.Leer(ct))?.Ahora : null;
        foreach (var r in reglas)
        {
            DateTime? ultimaLocal = r.Ultima is { } u ? TimeZoneInfo.ConvertTime(u, zona.Info).DateTime : null;
            if (!ServicioContexto.Cumple(r, evento, local, presencia, clima, ahora, ultimaLocal)) continue;
            r.Ultima = ahora;
            r.Resultado = await Aplicar(db, r, evento == "llego" ? "llegaste a casa" : evento == "salgo" ? "te vas de casa" : "a su hora", ct);
        }
        await db.SaveChangesAsync(ct);
    }

    // Hace lo que dice la regla y devuelve un resumen corto de qué pasó
    public async Task<string> Aplicar(AppDbContext db, ReglaContexto r, string motivo, CancellationToken ct)
    {
        var partes = new List<string>();
        if (r.EscenaCasaId is { } idCasa)
        {
            if (await db.EscenasCasa.FindAsync([idCasa], ct) is { } ec)
            {
                var (hechas, fallos) = await EscenasCasa.Aplicar(ec, db, casa, ct);
                partes.Add($"casa «{ec.Nombre}»: {hechas} ajuste(s){(fallos.Count > 0 ? $", {fallos.Count} fallo(s)" : "")}");
            }
            else partes.Add("la escena de casa ya no existe");
        }
        if (r.EscenaPcId is { } idPc)
        {
            if (await db.Escenas.FindAsync([idPc], ct) is { } ep)
            {
                if (ep.ApagarMinutos is not null) partes.Add($"PC «{ep.Nombre}» no se aplica sola: apaga el equipo");
                else
                {
                    var (ok, mensaje) = await Mda.Api.Pc.Escenas.Aplicar(ep, db, agente, avisos, reloj, "Contexto · " + r.Nombre, ct);
                    partes.Add(ok ? $"PC «{ep.Nombre}»" : $"PC: {mensaje}");
                }
            }
            else partes.Add("la escena del PC ya no existe");
        }
        var resumen = partes.Count == 0 ? "sin escenas" : string.Join(" · ", partes);
        var texto = $"🧭 «{r.Nombre}» ({motivo}): {resumen}.{(r.Aviso.Length > 0 ? "\n" + r.Aviso : "")}";
        await avisos.PorTelegram(texto, ct);
        return resumen.Length > 200 ? resumen[..200] : resumen;
    }
}

public static class ContextoEndpoints
{
    public static void MapContexto(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/contexto").RequireAuthorization();
        static IResult Mal(string campo, string texto) => Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [texto] });

        g.MapGet("/", async (AppDbContext db, ServicioTiempo tiempo, CancellationToken ct) =>
        {
            var (estado, desde) = await ServicioContexto.LeerPresencia(db, ct);
            var clima = (await tiempo.Leer(ct))?.Ahora;
            return Results.Ok(new
            {
                presencia = new { estado, desde },
                tiempo = clima is null ? null : new { clima.Temperatura, clima.Texto, lluvia = ServicioContexto.Llueve(clima.Codigo) },
                reglas = await db.ReglasContexto.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
                escenasCasa = await db.EscenasCasa.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Nombre, x.Icono }).ToListAsync(ct),
                escenasPc = await db.Escenas.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Nombre, x.Icono, apaga = x.ApagarMinutos != null }).ToListAsync(ct),
            });
        });

        g.MapPost("/", async (ReglaContextoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (ServicioContexto.Validar(e, true) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var evento = e.Evento ?? "llego";
            if (evento == "hora" && !ServicioContexto.HoraValida(e.Desde)) return Mal("desde", "Una regla a una hora necesita la hora.");
            if (e.EscenaCasaId is null && e.EscenaPcId is null && string.IsNullOrWhiteSpace(e.Aviso)) return Mal("escenaCasaId", "Elige una escena o escribe un aviso.");
            if (e.EscenaCasaId is { } c && !await db.EscenasCasa.AnyAsync(x => x.Id == c, ct)) return Mal("escenaCasaId", "Esa escena de casa no existe.");
            if (e.EscenaPcId is { } p && !await db.Escenas.AnyAsync(x => x.Id == p, ct)) return Mal("escenaPcId", "Esa escena del PC no existe.");
            if (await db.ReglasContexto.CountAsync(ct) >= ServicioContexto.Maximo) return Mal("nombre", $"Como mucho {ServicioContexto.Maximo} reglas: borra alguna.");
            var r = new ReglaContexto
            {
                Nombre = e.Nombre!.Trim(), Activa = e.Activa ?? true, Evento = evento,
                Desde = string.IsNullOrEmpty(e.Desde) ? null : e.Desde, Hasta = string.IsNullOrEmpty(e.Hasta) ? null : e.Hasta,
                Dias = e.Dias ?? 127, Tiempo = e.Tiempo ?? "", SoloEnCasa = e.SoloEnCasa ?? false,
                EscenaCasaId = e.EscenaCasaId, EscenaPcId = e.EscenaPcId, Aviso = (e.Aviso ?? "").Trim(), EsperaMinutos = e.EsperaMinutos ?? 60,
            };
            db.ReglasContexto.Add(r);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/contexto/{r.Id}", r);
        });

        g.MapPut("/{id:int}", async (int id, ReglaContextoEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (ServicioContexto.Validar(e, false) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var r = await db.ReglasContexto.FindAsync([id], ct);
            if (r is null) return Results.NotFound();
            if (e.EscenaCasaId is { } c && !await db.EscenasCasa.AnyAsync(x => x.Id == c, ct)) return Mal("escenaCasaId", "Esa escena de casa no existe.");
            if (e.EscenaPcId is { } p && !await db.Escenas.AnyAsync(x => x.Id == p, ct)) return Mal("escenaPcId", "Esa escena del PC no existe.");
            if (e.Nombre is not null) r.Nombre = e.Nombre.Trim();
            if (e.Activa is { } a) r.Activa = a;
            if (e.Evento is not null) r.Evento = e.Evento;
            if (e.Desde is not null) r.Desde = e.Desde == "" ? null : e.Desde;
            if (e.Hasta is not null) r.Hasta = e.Hasta == "" ? null : e.Hasta;
            if (e.Dias is { } d) r.Dias = d;
            if (e.Tiempo is not null) r.Tiempo = e.Tiempo;
            if (e.SoloEnCasa is { } s) r.SoloEnCasa = s;
            if (e.EscenaCasaId is { } ec) r.EscenaCasaId = ec; else if (e.QuitarEscenaCasa) r.EscenaCasaId = null;
            if (e.EscenaPcId is { } ep) r.EscenaPcId = ep; else if (e.QuitarEscenaPc) r.EscenaPcId = null;
            if (e.Aviso is not null) r.Aviso = e.Aviso.Trim();
            if (e.EsperaMinutos is { } w) r.EsperaMinutos = w;
            if (r.Evento == "hora" && !ServicioContexto.HoraValida(r.Desde)) return Mal("desde", "Una regla a una hora necesita la hora.");
            if (r.EscenaCasaId is null && r.EscenaPcId is null && r.Aviso.Length == 0) return Mal("escenaCasaId", "Elige una escena o escribe un aviso.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(r);
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var r = await db.ReglasContexto.FindAsync([id], ct);
            if (r is null) return Results.NotFound();
            db.ReglasContexto.Remove(r);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // «Probar»: la aplica ya, sin mirar la franja ni el tiempo (no cambia la espera)
        g.MapPost("/{id:int}/probar", async (int id, AppDbContext db, MotorContexto motor, CancellationToken ct) =>
        {
            var r = await db.ReglasContexto.FindAsync([id], ct);
            if (r is null) return Results.NotFound();
            var resumen = await motor.Aplicar(db, r, "prueba", ct);
            r.Resultado = resumen;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { resumen });
        });

        // Lo mismo que hace el iPhone al llegar o salir, desde la app (para probar sin salir de casa)
        g.MapPost("/evento", async (EventoContexto e, AppDbContext db, MotorContexto motor, CancellationToken ct) =>
        {
            if (e.Evento is not ("llego" or "salgo")) return Mal("evento", "El evento es llego o salgo.");
            await motor.Evento(db, e.Evento, ct);
            return Results.Ok(new { estado = e.Evento == "llego" ? "casa" : "fuera" });
        });

        // Solo cambia dónde estás, sin aplicar nada
        g.MapPut("/presencia", async (PresenciaEntrada e, AppDbContext db, TimeProvider reloj, CancellationToken ct) =>
        {
            if (e.Estado is not ("casa" or "fuera")) return Mal("estado", "El estado es casa o fuera.");
            await ServicioContexto.PonerPresencia(db, e.Estado, reloj.GetUtcNow(), ct);
            return Results.Ok(new { estado = e.Estado });
        });
    }
}
