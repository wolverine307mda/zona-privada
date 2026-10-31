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

// Cada 15 minutos, de día, mira si hay algo que contar:
//   · los lunes desde las 9, el resumen de la semana por correo (y una notificación);
//   · el mantenimiento del coche que toca o está a punto (como mucho un aviso por semana);
//   · que la copia de seguridad de la base de datos se hizo (si la última tiene más de 30 horas, avisa una vez al día);
//   · desde las 8 de la tarde, si mañana llueve, hay tormenta, mucho calor o helada (una vez al día).
public class Vigilante(IServiceScopeFactory scopes, IAvisos avisos, ServicioConexiones conexiones, Tiempo.ServicioTiempo tiempo, Zona zona, TimeProvider reloj, IConfiguration config, ILogger<Vigilante> log) : BackgroundService
{
    public const string ResumenSemanal = "resumen-semanal";
    private const string ResumenUltimo = "resumen-ultimo";
    private const string CopiasAvisado = "copias-avisado";
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
        await Aislada(() => Copias(db, ct), "las copias");
        if (Local.Hour >= 20) await Aislada(() => Tiempo(db, ct), "el tiempo");
    }

    private async Task Aislada(Func<Task> paso, string nombre)
    {
        try { await paso(); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Fallo en la vigilancia de {Paso}", nombre);
        }
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
            ? $"La última copia de seguridad de la base de datos es del {TimeZoneInfo.ConvertTime(fecha, zona.Info):dd/MM/yyyy HH:mm}. Revisa el contenedor «copias» (docker compose logs copias)."
            : "No hay ninguna copia de seguridad de la base de datos. Revisa el contenedor «copias» (docker compose logs copias).";
        await avisos.PorPush("💾 Falta la copia de seguridad", texto, "/seguridad", ct);
        await avisos.PorCorreo("💾 Falta la copia de seguridad", texto, "/seguridad", ct);
        await avisos.PorTelegram($"💾 Falta la copia de seguridad\n{texto}", ct);
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
