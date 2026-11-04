using System.Net;
using System.Net.Mail;

namespace Mda.Api.Avisos;

public record Canales(bool Correo, bool WhatsApp, bool Push = true, bool Telegram = false);

// Manda avisos al dueño: por correo (SMTP, por ejemplo Gmail con una contraseña de aplicación)
// y por WhatsApp con CallMeBot (gratis para uso personal: una API key ligada a tu número).
// Todo sale de la configuración (Correo:*, WhatsApp:*); sin ella, ese canal no se usa.
public interface IAvisos
{
    Canales Disponibles();
    Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default);
    // con la pantalla de la app que abre el botón del correo (por defecto, el inicio)
    Task<bool> PorCorreo(string asunto, string texto, string ruta, CancellationToken ct = default) => PorCorreo(asunto, texto, ct);
    Task<bool> PorWhatsApp(string texto, CancellationToken ct = default);
    // al chat de Telegram vinculado (el bot de la zona privada)
    Task<bool> PorTelegram(string texto, CancellationToken ct = default) => Task.FromResult(false);
    // notificación en los móviles y navegadores suscritos (Web Push)
    Task<bool> PorPush(string titulo, string texto, string? url = null, CancellationToken ct = default) => Task.FromResult(false);
    // correo con versión HTML (el resumen semanal); el texto plano va de respaldo
    Task<bool> PorCorreoHtml(string asunto, string html, string texto, string? ruta = null, CancellationToken ct = default) => PorCorreo(asunto, texto, ct);
}

public class Avisos(IConfiguration config, IHttpClientFactory http, Push push, Telegram.Bot bot, ILogger<Avisos> log) : IAvisos
{
    private string? C(string k) => string.IsNullOrWhiteSpace(config[k]) ? null : config[k];

    public Canales Disponibles() => new(
        C("Correo:Servidor") is not null && C("Correo:Para") is not null,
        C("WhatsApp:Telefono") is not null && C("WhatsApp:ApiKey") is not null,
        Telegram: bot.Vinculado);

    public Task<bool> PorTelegram(string texto, CancellationToken ct = default) => bot.Vinculado ? bot.Enviar(texto, ct: ct) : Task.FromResult(false);

    public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Correo(asunto, texto, null, null, ct);
    public Task<bool> PorCorreo(string asunto, string texto, string ruta, CancellationToken ct = default) => Correo(asunto, texto, null, ruta, ct);
    public Task<bool> PorCorreoHtml(string asunto, string html, string texto, string? ruta = null, CancellationToken ct = default) => Correo(asunto, texto, html, ruta, ct);
    public Task<bool> PorPush(string titulo, string texto, string? url = null, CancellationToken ct = default) => push.Enviar(titulo, texto, url, ct);

    // todos los correos salen con el mismo diseño (Plantilla); el texto plano va dentro por si el lector no pinta HTML
    private async Task<bool> Correo(string asunto, string texto, string? html, string? ruta, CancellationToken ct)
    {
        if (!Disponibles().Correo) return false;
        try
        {
            using var smtp = new SmtpClient(C("Correo:Servidor"), int.TryParse(C("Correo:Puerto"), out var p) ? p : 587)
            {
                EnableSsl = true,
                Credentials = C("Correo:Usuario") is { } u ? new NetworkCredential(u, C("Correo:Clave")) : null,
            };
            var de = C("Correo:De") ?? C("Correo:Usuario") ?? C("Correo:Para")!;
            using var msg = new MailMessage(new MailAddress(de, "Zona privada MDA"), new MailAddress(C("Correo:Para")!))
            {
                // un salto de línea en el asunto (un título pegado desde Telegram) hace que SMTP lo rechace
                Subject = asunto.ReplaceLineEndings(" "), Body = texto, IsBodyHtml = false,
            };
            var resumen = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            var pagina = Plantilla.Correo(asunto, html ?? Plantilla.DeTexto(texto), C("App:UrlPublica"), ruta, resumen);
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(pagina, System.Text.Encoding.UTF8, "text/html"));
            await smtp.SendMailAsync(msg, ct);
            return true;
        }
        catch (Exception e) when (e is SmtpException or InvalidOperationException or FormatException or ArgumentException or TaskCanceledException)
        {
            log.LogWarning(e, "No se pudo mandar el correo «{Asunto}»", asunto);
            return false;
        }
    }

    // CallMeBot limita el ritmo y el texto va en la URL: mensajes cortos (una URL larga da 414) y al menos 3 s entre uno y otro
    private static readonly SemaphoreSlim TurnoWhatsApp = new(1, 1);
    private static DateTimeOffset ultimoWhatsApp = DateTimeOffset.MinValue;
    public const int MaxWhatsApp = 300;

    public async Task<bool> PorWhatsApp(string texto, CancellationToken ct = default)
    {
        if (!Disponibles().WhatsApp) return false;
        if (texto.Length > MaxWhatsApp) texto = texto[..(MaxWhatsApp - 1)].TrimEnd() + "…";
        await TurnoWhatsApp.WaitAsync(ct);
        try
        {
            var espera = ultimoWhatsApp + TimeSpan.FromSeconds(3) - DateTimeOffset.UtcNow;
            if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);
            var url = $"https://api.callmebot.com/whatsapp.php?phone={Uri.EscapeDataString(C("WhatsApp:Telefono")!)}" +
                      $"&text={Uri.EscapeDataString(texto)}&apikey={Uri.EscapeDataString(C("WhatsApp:ApiKey")!)}";
            using var res = await http.CreateClient("avisos").GetAsync(url, ct);
            var cuerpo = await res.Content.ReadAsStringAsync(ct);
            // CallMeBot suele contestar 200 aunque falle: el motivo viene en el texto de la respuesta
            var fallo = !res.IsSuccessStatusCode || cuerpo.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || cuerpo.Contains("invalid", StringComparison.OrdinalIgnoreCase);
            if (fallo) log.LogWarning("CallMeBot no entregó el mensaje (HTTP {Codigo}): {Motivo}", (int)res.StatusCode, Resumen(cuerpo));
            return !fallo;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // la URL lleva la apikey: no se registra la excepción entera, solo el motivo
            log.LogWarning("No se pudo mandar el WhatsApp: {Motivo}", e.GetType().Name);
            return false;
        }
        finally { ultimoWhatsApp = DateTimeOffset.UtcNow; TurnoWhatsApp.Release(); }
    }

    private static string Resumen(string cuerpo)
    {
        var plano = System.Text.RegularExpressions.Regex.Replace(cuerpo, "<[^>]+>", " ");
        plano = System.Text.RegularExpressions.Regex.Replace(plano, @"\s+", " ").Trim();
        return plano.Length > 160 ? plano[..160] : plano;
    }
}
