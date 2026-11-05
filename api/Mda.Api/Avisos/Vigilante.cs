using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Conexiones;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Avisos;

public record Copia(bool Vigilada, DateTimeOffset? Fecha, long Tamano, string? Nombre);
public record Simulacro(bool Ok, int Usuarios, int Tablas, string Copia, DateTimeOffset Fecha);

// Cada 15 minutos, de día, mira si hay algo que contar:
//   · los lunes desde las 9, el resumen de la semana por correo (y una notificación);
//   · el mantenimiento del coche que toca o está a punto (como mucho un aviso por semana);
//   · que la copia de seguridad de la base de datos se hizo (si la última tiene más de 30 horas, avisa una vez al día);
//   · desde las 8 de la tarde, si mañana llueve, hay tormenta, mucho calor o helada (una vez al día).
public class Vigilante(IServiceScopeFactory scopes, IAvisos avisos, ServicioConexiones conexiones, Tiempo.ServicioTiempo tiempo, Luz.ServicioIss iss, Luz.ServicioLuz luz, Zona zona, TimeProvider reloj, IConfiguration config, ILogger<Vigilante> log) : BackgroundService
{
    public const string ResumenSemanal = "resumen-semanal";
    private const string ResumenUltimo = "resumen-ultimo";
    private const string CopiasAvisado = "copias-avisado";
    private const string SimulacroAvisado = "simulacro-avisado";
    private const string CopiaNotificada = "copia-notificada";
    private static readonly CultureInfo Es = new("es-ES");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), reloj, ct); } catch (OperationCanceledException) { return; }
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(15), reloj);
        do
        {
            try { await Revisar(ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Fallo en la vigilancia de avisos"); }
        } while (await t.WaitForNextTickAsync(ct));
    }

    private DateTime Local => TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona.Info).DateTime;

    public async Task Revisar(CancellationToken ct)
    {
        if (Local.Hour < 9 || Local.Hour >= 23) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // cada comprobación por separado: si una falla, las demás se hacen igual
        await Aislada(() => Resumen(db, ct), "el resumen semanal");
        await Aislada(() => Coche(db, ct), "el coche");
        await Aislada(() => Cartera.ComprobarTodos(db, avisos, DateOnly.FromDateTime(Local), log, ct), "los presupuestos");
        await Aislada(() => Copias(db, ct), "las copias");
        await Aislada(() => ResultadoSimulacro(db, ct), "el simulacro de restauración");
        await Aislada(() => CopiaNueva(db, ct), "la copia nueva");
        if (Local.Hour >= 20) await Aislada(() => Tiempo(db, ct), "el tiempo");
        await Aislada(() => Cielo(db, ct), "el cielo (ISS y meteoros)");
        await Aislada(() => Habitos(db, ct), "los hábitos");
    }

    private async Task Aislada(Func<Task> paso, string nombre)
    {
        try { await paso(); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Fallo en la vigilancia de {Paso}", nombre);
        }
    }

    // dónde mirar si falla una copia: en Docker, el contenedor «copias»; en la versión de escritorio, la bandeja del lanzador
    public static string AyudaCopias(IConfiguration config, bool minuscula = false)
    {
        var nativo = config["Copias:Origen"] == "nativo";
        if (minuscula) return nativo ? "revisa las copias desde el icono de ZonaPrivada en la bandeja (Comprobar)." : "revisa «docker compose logs copias».";
        return nativo ? "Revisa las copias desde el icono de ZonaPrivada en la bandeja (Comprobar)." : "Revisa el contenedor «copias» (docker compose logs copias).";
    }

    // ---- copia de seguridad ----
    public Copia UltimaCopia()
    {
        var ruta = config["Copias:Ruta"];
        if (string.IsNullOrWhiteSpace(ruta) || !Directory.Exists(ruta)) return new Copia(false, null, 0, null);
        var f = new DirectoryInfo(ruta).GetFiles("zonaprivada-*.dump").MaxBy(x => x.LastWriteTimeUtc);
        return f is null ? new Copia(true, null, 0, null) : new Copia(true, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero), f.Length, f.Name);
    }

    private async Task Copias(AppDbContext db, CancellationToken ct)
    {
        var c = UltimaCopia();
        if (!c.Vigilada || (c.Fecha is { } f && reloj.GetUtcNow() - f < TimeSpan.FromHours(30))) return;
        var hoy = DateOnly.FromDateTime(Local).ToString("O");
        var a = await db.Ajustes.FindAsync([CopiasAvisado], ct);
        if (a?.Valor == hoy) return;
        if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = CopiasAvisado });
        a.Valor = hoy;
        await db.SaveChangesAsync(ct);
        var texto = c.Fecha is { } fecha
            ? $"La última copia de seguridad de la base de datos es del {TimeZoneInfo.ConvertTime(fecha, zona.Info):dd/MM/yyyy HH:mm}. {AyudaCopias(config)}"
            : $"No hay ninguna copia de seguridad de la base de datos. {AyudaCopias(config)}";
        await avisos.PorPush("💾 Falta la copia de seguridad", texto, "/seguridad", ct);
        await avisos.PorCorreo("💾 Falta la copia de seguridad", texto, "/seguridad", ct);
        await avisos.PorTelegram($"💾 Falta la copia de seguridad\n{texto}", ct);
    }

    // «💾 Copia hecha»: un aviso por cada copia nueva (si lo tienes activado en Mi PC › Avisos)
    public async Task CopiaNueva(AppDbContext db, CancellationToken ct)
    {
        var c = UltimaCopia();
        if (!c.Vigilada || c.Nombre is null) return;
        var a = await db.Ajustes.FindAsync([CopiaNotificada], ct);
        if (a?.Valor == c.Nombre) return;
        var primera = a is null;
        if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = CopiaNotificada });
        a.Valor = c.Nombre;
        await db.SaveChangesAsync(ct);
        if (primera || !(await Pc.AvisosPcAjustes.Leer(db, ct)).Copias) return; // la primera vez solo se anota cuál es la última
        await avisos.PorTelegram($"💾 Copia de seguridad hecha: {c.Nombre} ({c.Tamano / 1024.0 / 1024:0.#} MB).", ct);
    }

    // ---- simulacro de restauración ----
    // El contenedor «copias» restaura cada semana la última copia en una base vacía y deja el resultado en «.ultimo-simulacro»
    // con el formato «ok|usuarios|tablas|archivo|fecha» (o «fallo|0|0|archivo|fecha»). Una copia que no se puede restaurar no es una copia.
    public static Simulacro? LeerSimulacro(string? linea)
    {
        var p = (linea ?? "").Trim().Split('|');
        if (p.Length < 5 || p[0] is not ("ok" or "fallo") || !int.TryParse(p[1], out var u) || !int.TryParse(p[2], out var t)
            || !DateTimeOffset.TryParse(p[4], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var f)) return null;
        return new Simulacro(p[0] == "ok", u, t, p[3], f);
    }

    public async Task ResultadoSimulacro(AppDbContext db, CancellationToken ct)
    {
        var ruta = config["Copias:Ruta"];
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(Path.Combine(ruta, ".ultimo-simulacro"))) return;
        if (LeerSimulacro(await File.ReadAllTextAsync(Path.Combine(ruta, ".ultimo-simulacro"), ct)) is not { } s) return;
        var a = await db.Ajustes.FindAsync([SimulacroAvisado], ct);
        var marca = s.Fecha.ToString("O");
        if (a?.Valor == marca) return;
        if (a is null) db.Ajustes.Add(a = new Ajuste { Clave = SimulacroAvisado });
        a.Valor = marca;
        await db.SaveChangesAsync(ct);
        if (s.Ok)
            await avisos.PorTelegram($"✅ Copia verificada: «{s.Copia}» se restauró en una base vacía ({s.Usuarios} usuario(s), {s.Tablas} tablas).", ct);
        else
        {
            var texto = $"La copia «{s.Copia}» NO se pudo restaurar en la prueba semanal. Haz una copia a mano y {AyudaCopias(config, minuscula: true)}";
            await avisos.PorPush("⚠️ Copia de seguridad dudosa", texto, "/seguridad", ct);
            await avisos.PorTelegram($"⚠️ {texto}", ct);
        }
    }

    // ---- el tiempo de mañana ----
    public async Task Tiempo(AppDbContext db, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(Local);
        if (await db.Ajustes.AnyAsync(a => a.Clave == Mda.Api.Tiempo.ServicioTiempo.ClaveAvisos && a.Valor == "no", ct)) return;
        var hecho = await db.Ajustes.FindAsync([Mda.Api.Tiempo.ServicioTiempo.ClaveAvisado], ct);
        if (hecho?.Valor == hoy.ToString("O")) return;
        if (await tiempo.Leer(ct) is not { } p) return; // sin sitio o sin conexión: se mira en el siguiente repaso
        if (hecho is null) db.Ajustes.Add(hecho = new Ajuste { Clave = Mda.Api.Tiempo.ServicioTiempo.ClaveAvisado });
        hecho.Valor = hoy.ToString("O");
        await db.SaveChangesAsync(ct);
        if (Mda.Api.Tiempo.ServicioTiempo.AvisoDe(p, hoy.AddDays(1)) is not { } aviso) return;
        await avisos.PorPush(aviso.Titulo, aviso.Texto, "/inicio", ct);
        await avisos.PorTelegram($"{aviso.Titulo}\n{aviso.Texto}", ct);
    }

    // ---- hábitos: a su hora, si aún no están hechos, un aviso (una vez al día), con la racha en juego si es larga ----
    public async Task Habitos(AppDbContext db, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(Local);
        var hora = TimeOnly.FromDateTime(Local);
        var pendientes = await db.Habitos.Where(h => !h.Archivado && h.HoraAviso != null && h.AvisadoFecha != hoy).ToListAsync(ct);
        pendientes = pendientes.Where(h => Mda.Api.Habitos.Rachas.Toca(h.Dias, hoy) && TimeOnly.TryParseExact(h.HoraAviso, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a) && hora >= a).ToList();
        if (pendientes.Count == 0) return;
        var ids = pendientes.Select(h => h.Id).ToList();
        var regs = (await db.RegistrosHabito.AsNoTracking().Where(r => ids.Contains(r.HabitoId) && r.Fecha >= hoy.AddDays(-400)).ToListAsync(ct))
            .GroupBy(r => r.HabitoId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Fecha, r => r.Cantidad));
        foreach (var h in pendientes)
        {
            h.AvisadoFecha = hoy;   // se marca aunque ya esté hecho: hoy no hay que volver a mirarlo
            var r = regs.GetValueOrDefault(h.Id) ?? [];
            if (r.GetValueOrDefault(hoy) >= h.Objetivo) continue;
            var racha = Mda.Api.Habitos.Rachas.Calcular(h.Dias, h.Objetivo, r, Mda.Api.Habitos.ServicioHabitos.Inicio(h, r, hoy), hoy).Actual;
            var texto = racha >= 3 ? $"Aún no has hecho «{h.Nombre}» hoy. Llevas {racha} días seguidos: no la pierdas." : $"Aún no has hecho «{h.Nombre}» hoy.";
            await avisos.PorPush($"✅ {h.Nombre}", texto, "/habitos", ct);
            await avisos.PorTelegram($"✅ {texto}\nEscribe /hecho {h.Nombre.ToLowerInvariant()} para apuntarlo.", ct);
        }
        await db.SaveChangesAsync(ct);
    }

    // ---- el cielo: pasadas de la ISS y lluvias de meteoros ----
    public const string IssAvisada = "iss-avisada", MeteorosAvisado = "meteoros-avisado";

    public async Task Cielo(AppDbContext db, CancellationToken ct)
    {
        if (await tiempo.LugarGuardado(ct) is not { } lugar) return;
        var ahora = reloj.GetUtcNow();
        var nubes = await luz.Nubes(lugar, ct);
        int? NubesA(DateTimeOffset t)
        {
            var l = TimeZoneInfo.ConvertTime(t, zona.Info).DateTime;
            return nubes?.FirstOrDefault(x => x.Hora == new DateTime(l.Year, l.Month, l.Day, l.Hour, 0, 0))?.Total;
        }

        // ISS: una pasada buena, con el cielo despejado
        var ultima = await db.Ajustes.FindAsync([IssAvisada], ct);
        DateTimeOffset? ultimaPasada = DateTimeOffset.TryParse(ultima?.Valor, out var up) ? up : null;
        if (await iss.Tle(ct) is { } tle)
        {
            var pasadas = await Task.Run(() => Luz.Pasadas.Calcular(tle.L1, tle.L2, lugar.Lat, lugar.Lon, ahora, 2), ct);
            if (Luz.Pasadas.ParaAvisar(pasadas, ahora, zona.Info, ultimaPasada) is { } p)
            {
                // se marca como avisada aunque haya nubes: con el cielo cubierto no hay que insistir en el siguiente repaso
                if (ultima is null) db.Ajustes.Add(ultima = new Ajuste { Clave = IssAvisada });
                ultima.Valor = p.Inicio.ToString("O");
                await db.SaveChangesAsync(ct);
                if (NubesA(p.Maximo) is not > 40)
                {
                    var hora = TimeZoneInfo.ConvertTime(p.Inicio, zona.Info);
                    var texto = $"La Estación Espacial Internacional pasa a las {hora:HH:mm}: sale por el {p.DesdeRumbo}, sube hasta {p.AlturaMax:0}° y se esconde por el {p.HastaRumbo}. Se ve como una estrella muy brillante que no parpadea.";
                    await avisos.PorPush("🛰️ La ISS sobre ti", texto, "/fotografia", ct);
                    await avisos.PorTelegram($"🛰️ La ISS sobre ti\n{texto}", ct);
                }
            }
        }

        // Meteoros: por la tarde, si esta noche es el máximo de una lluvia con luna discreta y cielo despejado
        if (Local.Hour >= 18)
        {
            foreach (var ev in Luz.Eventos.Proximos(ahora, zona.Info, 2).Where(e => e.Tipo == "lluvia" && e.Pico > ahora && e.Pico < ahora.AddHours(14) && e.LunaPct < 40))
            {
                var clave = $"{MeteorosAvisado}:{ev.Nombre}:{ev.Pico:yyyy}";
                if (await db.Ajustes.FindAsync([clave], ct) is not null) continue;
                db.Ajustes.Add(new Ajuste { Clave = clave, Valor = "si" });
                await db.SaveChangesAsync(ct);
                var cielo = NubesA(ev.Pico);
                if (cielo is > 50) continue;
                var texto = $"Esta noche es el máximo de las {ev.Nombre}: hasta {ev.Zhr} por hora, luna al {ev.LunaPct} %{(cielo is { } n ? $" y nubes al {n} %" : "")}. Lo mejor, hacia las 03:00 y lejos de las luces.";
                await avisos.PorPush($"☄️ {ev.Nombre} esta noche", texto, "/fotografia", ct);
                await avisos.PorTelegram($"☄️ {ev.Nombre} esta noche\n{texto}", ct);
            }
        }
    }

    // ---- mantenimiento del coche ----
    private async Task Coche(AppDbContext db, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(Local);
        var km = await Gastos.Coche.KmActual(db, ct);
        foreach (var m in await db.Mantenimientos.ToListAsync(ct))
        {
            if (m.Avisado is { } av && hoy.DayNumber - av.DayNumber < 7) continue;
            var e = Gastos.Coche.EstadoDe(m, km, hoy);
            if (e.Nivel is not ("vencido" or "pronto")) continue;
            m.Avisado = hoy;
            var cuando = new List<string>();
            if (e.FaltanKm is { } fk) cuando.Add(fk <= 0 ? $"te has pasado {-fk:N0} km" : $"faltan {fk:N0} km");
            if (e.FaltanDias is { } fd) cuando.Add(fd <= 0 ? $"venció el {e.ProximaFecha:dd/MM/yyyy}" : $"toca el {e.ProximaFecha:dd/MM/yyyy}");
            var titulo = e.Nivel == "vencido" ? $"🚗 {m.Nombre}: ya toca" : $"🚗 {m.Nombre}: falta poco";
            var texto = string.Join(" · ", cuando);
            await avisos.PorPush(titulo, texto, "/gastos/deudas", ct);
            await avisos.PorTelegram($"{titulo}\n{texto}", ct);
            await avisos.PorCorreo(titulo, $"{texto}.\n\nCuando lo hagas, márcalo como hecho en Cartera › Coche.", "/gastos/deudas", ct);
        }
        await db.SaveChangesAsync(ct);
    }

    // ---- resumen semanal ----
    private async Task Resumen(AppDbContext db, CancellationToken ct)
    {
        var local = Local;
        if (local.DayOfWeek != DayOfWeek.Monday) return;
        var semana = $"{ISOWeek.GetYear(local)}-W{ISOWeek.GetWeekOfYear(local):D2}";
        if (await db.Ajustes.AnyAsync(a => a.Clave == ResumenSemanal && a.Valor == "no", ct)) return;
        var hecho = await db.Ajustes.FindAsync([ResumenUltimo], ct);
        if (hecho?.Valor == semana) return;

        var (asunto, html, texto, corto) = await Componer(db, DateOnly.FromDateTime(local), ct);
        var ok = await avisos.PorCorreoHtml(asunto, html, texto, "/inicio", ct);
        ok |= await avisos.PorPush("📊 Tu semana", corto, "/gastos", ct);
        ok |= await avisos.PorTelegram($"📊 Tu semana\n{corto}", ct);
        if (!ok && avisos.Disponibles().Correo) return; // se reintenta en el siguiente repaso
        if (hecho is null) db.Ajustes.Add(hecho = new Ajuste { Clave = ResumenUltimo });
        hecho.Valor = semana;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Resumen semanal {Semana} enviado", semana);
    }

    public async Task<(string Asunto, string Html, string Texto, string Corto)> Componer(AppDbContext db, DateOnly lunes, CancellationToken ct)
    {
        var desde = lunes.AddDays(-7);
        var movs = await db.Movimientos.AsNoTracking().Where(m => m.Fecha >= desde && m.Fecha < lunes).ToListAsync(ct);
        var gastos = movs.Where(m => m.Tipo == TipoMovimiento.Gasto).Sum(m => m.Importe);
        var ingresos = movs.Where(m => m.Tipo == TipoMovimiento.Ingreso).Sum(m => m.Importe);
        var top = Calculos.PorCategoria(movs).Take(3).ToList();
        var saldo = await Cartera.SaldoActual(db, lunes.AddDays(-1), ct);

        // la semana que empieza
        var ini = new DateTimeOffset(lunes.ToDateTime(TimeOnly.MinValue), zona.Info.GetUtcOffset(lunes.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
        var fin = ini.AddDays(7);
        var eventos = await db.Eventos.AsNoTracking().Where(e => e.Repetir != Repeticion.No || (e.Inicio < fin && (e.Fin ?? e.Inicio) >= ini)).ToListAsync(ct);
        var agenda = eventos.SelectMany(e => Ocurrencias.Entre(e, ini, fin, zona.Info).Select(o => (e, o)))
            .Where(x => !(x.e.Tipo == TipoEvento.Recordatorio && x.e.Hecho)).OrderBy(x => x.o).Take(8).ToList();

        var km = await Gastos.Coche.KmActual(db, ct);
        var coche = (await db.Mantenimientos.AsNoTracking().ToListAsync(ct)).Select(m => Gastos.Coche.EstadoDe(m, km, lunes))
            .Where(e => e.Nivel is "vencido" or "pronto").ToList();

        var canciones = await TopCanciones(ct);

        string H(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append("<div>");
        sb.Append($"<p style=\"color:#5f6870;margin:-10px 0 20px\">Del {desde.ToString("d 'de' MMMM", Es)} al {lunes.AddDays(-1).ToString("d 'de' MMMM", Es)}</p>");
        sb.Append("<table style=\"width:100%;border-collapse:collapse;margin-bottom:20px\"><tr>");
        foreach (var (t, v, c) in new[] { ("Gastado", gastos, "#0b151c"), ("Ingresado", ingresos, "#138a4c"), ("Saldo", saldo, saldo < 0 ? "#c62828" : "#0b151c") })
            sb.Append($"<td style=\"padding:14px;background:#f4f2ee;border-radius:12px;border:4px solid #fff\"><div style=\"font-size:12px;color:#5f6870;text-transform:uppercase\">{t}</div><div style=\"font-size:22px;font-weight:700;color:{c}\">{H(Cartera.Euros(v))}</div></td>");
        sb.Append("</tr></table>");
        if (top.Count > 0)
        {
            sb.Append("<h2 style=\"font-size:16px\">En qué se fue</h2><ul style=\"padding-left:18px\">");
            foreach (var c in top) sb.Append($"<li>{H(Categorias.Todas.TryGetValue(c.Categoria, out var cat) ? cat.Nombre : c.Categoria)}: <b>{H(Cartera.Euros(c.Total))}</b></li>");
            sb.Append("</ul>");
        }
        sb.Append("<h2 style=\"font-size:16px\">Lo que viene esta semana</h2>");
        if (agenda.Count == 0) sb.Append("<p style=\"color:#5f6870\">Nada en la agenda.</p>");
        else
        {
            sb.Append("<ul style=\"padding-left:18px\">");
            foreach (var (e, o) in agenda)
            {
                var l = TimeZoneInfo.ConvertTime(o, zona.Info);
                sb.Append($"<li><b>{H(l.ToString(e.TodoElDia ? "dddd d" : "dddd d, HH:mm", Es))}</b> · {(e.Tipo == TipoEvento.Recordatorio ? "⏰ " : "")}{H(e.Titulo)}</li>");
            }
            sb.Append("</ul>");
        }
        if (coche.Count > 0)
        {
            sb.Append("<h2 style=\"font-size:16px\">El coche</h2><ul style=\"padding-left:18px\">");
            foreach (var c in coche) sb.Append($"<li>{H(c.Nombre)}: {(c.Nivel == "vencido" ? "ya toca" : "falta poco")}</li>");
            sb.Append("</ul>");
        }
        if (canciones.Count > 0)
        {
            sb.Append("<h2 style=\"font-size:16px\">Lo que más has escuchado</h2><ol style=\"padding-left:18px\">");
            foreach (var c in canciones) sb.Append($"<li>{H(c)}</li>");
            sb.Append("</ol>");
        }
        sb.Append("<p style=\"color:#8d979e;font-size:13px;margin:20px 0 0\">El resumen semanal se desactiva en Seguridad › Avisos.</p></div>");

        var texto = new StringBuilder($"Tu semana ({desde:dd/MM} – {lunes.AddDays(-1):dd/MM})\n\nGastado: {Cartera.Euros(gastos)}\nIngresado: {Cartera.Euros(ingresos)}\nSaldo: {Cartera.Euros(saldo)}\n");
        foreach (var (e, o) in agenda) texto.Append($"\n· {TimeZoneInfo.ConvertTime(o, zona.Info).ToString("dddd d HH:mm", Es)} {e.Titulo}");
        return ($"📊 Tu semana: {Cartera.Euros(gastos)} gastados", sb.ToString(), texto.ToString(),
            $"Gastaste {Cartera.Euros(gastos)} · saldo {Cartera.Euros(saldo)} · {agenda.Count} cosas en la agenda");
    }

    private async Task<List<string>> TopCanciones(CancellationToken ct)
    {
        try
        {
            using var res = await conexiones.Llamar(ServicioConexiones.Spotify, HttpMethod.Get, "https://api.spotify.com/v1/me/top/tracks?limit=3&time_range=short_term", ct: ct);
            if (res is null || !res.IsSuccessStatusCode) return [];
            var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            return j.GetProperty("items").EnumerateArray()
                .Select(t => $"{t.GetProperty("name").GetString()} — {string.Join(", ", t.GetProperty("artists").EnumerateArray().Select(a => a.GetProperty("name").GetString()))}").ToList();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException) { return []; }
    }
}
