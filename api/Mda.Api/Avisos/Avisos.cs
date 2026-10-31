using System.Net;
using System.Net.Mail;

namespace Mda.Api.Avisos;

public record Canales(bool Correo, bool WhatsApp, bool Push = true);

// Manda avisos al dueño: por correo (SMTP, por ejemplo Gmail con una contraseña de aplicación)
// y por WhatsApp con CallMeBot (gratis para uso personal: una API key ligada a tu número).
// Todo sale de la configuración (Correo:*, WhatsApp:*); sin ella, ese canal no se usa.
public interface IAvisos
{
    Canales Disponibles();
    Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default);
    Task<bool> PorWhatsApp(string texto, CancellationToken ct = default);
    // notificación en los móviles y navegadores suscritos (Web Push)
    Task<bool> PorPush(string titulo, string texto, string? url = null, CancellationToken ct = default) => Task.FromResult(false);
    // correo con versión HTML (el resumen semanal); el texto plano va de respaldo
    Task<bool> PorCorreoHtml(string asunto, string html, string texto, CancellationToken ct = default) => PorCorreo(asunto, texto, ct);
}

public class Avisos(IConfiguration config, IHttpClientFactory http, Push push, ILogger<Avisos> log) : IAvisos
{
    private string? C(string k) => string.IsNullOrWhiteSpace(config[k]) ? null : config[k];

    public Canales Disponibles() => new(
        C("Correo:Servidor") is not null && C("Correo:Para") is not null,
        C("WhatsApp:Telefono") is not null && C("WhatsApp:ApiKey") is not null);

    public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Correo(asunto, texto, null, ct);
    public Task<bool> PorCorreoHtml(string asunto, string html, string texto, CancellationToken ct = default) => Correo(asunto, texto, html, ct);
    public Task<bool> PorPush(string titulo, string texto, string? url = null, CancellationToken ct = default) => push.Enviar(titulo, texto, url, ct);

    private async Task<bool> Correo(string asunto, string texto, string? html, CancellationToken ct)
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
                Subject = asunto, Body = texto, IsBodyHtml = false,
            };
            if (html is not null)
                msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, System.Text.Encoding.UTF8, "text/html"));
            await smtp.SendMailAsync(msg, ct);
            return true;
        }
        catch (Exception e) when (e is SmtpException or InvalidOperationException or FormatException)
        {
            log.LogWarning(e, "No se pudo mandar el correo «{Asunto}»", asunto);
            return false;
        }
    }

    public async Task<bool> PorWhatsApp(string texto, CancellationToken ct = default)
    {
        if (!Disponibles().WhatsApp) return false;
        try
        {
            var url = $"https://api.callmebot.com/whatsapp.php?phone={Uri.EscapeDataString(C("WhatsApp:Telefono")!)}" +
                      $"&text={Uri.EscapeDataString(texto)}&apikey={Uri.EscapeDataString(C("WhatsApp:ApiKey")!)}";
            using var res = await http.CreateClient("avisos").GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) log.LogWarning("CallMeBot respondió {Codigo}", (int)res.StatusCode);
            return res.IsSuccessStatusCode;
        }
        catch (HttpRequestException e)
        {
            log.LogWarning(e, "No se pudo mandar el WhatsApp");
            return false;
        }
    }
}
