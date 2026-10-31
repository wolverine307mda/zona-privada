using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Mda.Api.Avisos;

// El diseño de todos los correos: cabecera con la marca, tarjeta blanca con el contenido, botón para abrir
// la app y pie. Hecho con tablas y estilos en línea, que es lo único que respetan Gmail, Outlook y el Mail del iPhone.
public static partial class Plantilla
{
    private const string Marca = "#ff7a1f", Tinta = "#0b151c", Suave = "#5f6870", Fondo = "#eeece7", Noche = "#070d12";

    public static string Correo(string asunto, string cuerpoHtml, string? urlPublica, string? ruta, string? resumen = null, DateTimeOffset? cuando = null)
    {
        string H(string s) => WebUtility.HtmlEncode(s);
        var (icono, titulo) = Separar(asunto);
        var url = urlPublica?.TrimEnd('/');
        var anio = (cuando ?? DateTimeOffset.UtcNow).Year;
        var logo = url is null
            ? $"<span style=\"display:inline-block;width:34px;height:34px;line-height:34px;border-radius:10px;background:{Marca};color:{Noche};font-weight:800;font-size:15px;text-align:center\">M</span>"
            : $"<img src=\"{H(url)}/img/icono-192.png\" width=\"34\" height=\"34\" alt=\"\" style=\"display:block;border:0;border-radius:10px\">";

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\">");
        sb.Append($"<title>{H(titulo)}</title></head>");
        sb.Append($"<body style=\"margin:0;padding:0;background:{Fondo};-webkit-text-size-adjust:100%\">");
        // lo que se ve en la bandeja de entrada junto al asunto
        if (!string.IsNullOrWhiteSpace(resumen))
            sb.Append($"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0\">{H(resumen)}&#8203;&nbsp;&#8203;&nbsp;&#8203;&nbsp;</div>");
        sb.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{Fondo}\"><tr><td align=\"center\" style=\"padding:28px 12px\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:580px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif\">");

        // cabecera oscura con la marca
        sb.Append($"<tr><td style=\"background:{Noche};border-radius:18px 18px 0 0;padding:20px 28px\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>");
        sb.Append($"<td width=\"44\" valign=\"middle\">{logo}</td>");
        sb.Append($"<td valign=\"middle\" style=\"color:#ffffff;font-size:15px;font-weight:700;letter-spacing:.02em\">MDA <span style=\"color:{Marca}\">·</span> <span style=\"font-weight:500;color:#aeb7bd\">Zona privada</span></td>");
        sb.Append($"<td align=\"right\" valign=\"middle\" style=\"color:#6f7a82;font-size:12px\">{H((cuando ?? DateTimeOffset.UtcNow).ToString("d MMM yyyy", Es))}</td>");
        sb.Append("</tr></table></td></tr>");
        sb.Append($"<tr><td style=\"background:{Marca};height:4px;line-height:4px;font-size:0\">&nbsp;</td></tr>");

        // tarjeta
        sb.Append($"<tr><td style=\"background:#ffffff;padding:32px 28px 28px;color:{Tinta};font-size:16px;line-height:1.55\">");
        if (icono is not null)
            sb.Append($"<div style=\"width:52px;height:52px;line-height:52px;border-radius:16px;background:#fff1e6;text-align:center;font-size:26px;margin:0 0 16px\">{H(icono)}</div>");
        sb.Append($"<h1 style=\"margin:0 0 18px;font-size:24px;line-height:1.2;font-weight:800;letter-spacing:-.01em;color:{Tinta}\">{H(titulo)}</h1>");
        sb.Append(cuerpoHtml);
        if (url is not null)
        {
            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:28px 0 4px\"><tr>");
            sb.Append($"<td style=\"background:{Marca};border-radius:999px\"><a href=\"{H(url + (ruta ?? "/inicio"))}\" style=\"display:inline-block;padding:13px 26px;color:{Noche};font-weight:700;font-size:15px;text-decoration:none\">Abrir mi zona privada &rarr;</a></td>");
            sb.Append("</tr></table>");
        }
        sb.Append("</td></tr>");

        // pie
        sb.Append($"<tr><td style=\"background:#f8f7f4;border-radius:0 0 18px 18px;border-top:1px solid #e6e3dd;padding:20px 28px;color:{Suave};font-size:12px;line-height:1.6\">");
        sb.Append($"<p style=\"margin:0\">Este aviso te lo manda <strong style=\"color:{Tinta}\">tu zona privada</strong>, que solo usas tú.<br>");
        sb.Append(url is null ? "Los avisos se cambian en Seguridad &rsaquo; Avisos." : $"Los avisos se cambian en <a href=\"{H(url)}/seguridad\" style=\"color:{Suave}\">Seguridad &rsaquo; Avisos</a>.");
        sb.Append("</p>");
        if (url is not null)
        {
            string Enlace(string r, string t) => $"<a href=\"{H(url + r)}\" style=\"color:{Tinta};font-weight:600;text-decoration:none\">{t}</a>";
            sb.Append($"<p style=\"margin:10px 0 0\">{Enlace("/inicio", "Inicio")} &nbsp;·&nbsp; {Enlace("/agenda", "Agenda")} &nbsp;·&nbsp; {Enlace("/gastos", "Cartera")} &nbsp;·&nbsp; {Enlace("/notas", "Notas")}</p>");
        }
        sb.Append($"</td></tr><tr><td align=\"center\" style=\"padding:18px 0 0;color:#9aa2a8;font-size:11px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif\">© {anio} MDA &nbsp;·&nbsp; Hecho en casa, sin anuncios ni rastreadores</td></tr>");
        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    // El texto plano de los avisos, en HTML: párrafos, y las líneas «Clave: valor» seguidas como una ficha
    public static string DeTexto(string texto)
    {
        string H(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        foreach (var bloque in Regex.Split(texto.Trim().Replace("\r", ""), @"\n\s*\n"))
        {
            var lineas = bloque.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lineas.Length > 1 && lineas.All(l => Ficha().IsMatch(l)))
            {
                sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:0 0 16px;background:#f6f5f2;border-radius:12px\">");
                foreach (var l in lineas)
                {
                    var m = Ficha().Match(l);
                    sb.Append($"<tr><td style=\"padding:10px 16px;color:{Suave};font-size:13px;width:34%;border-bottom:1px solid #ebe8e2\">{H(m.Groups[1].Value)}</td>");
                    sb.Append($"<td style=\"padding:10px 16px;font-size:15px;font-weight:600;border-bottom:1px solid #ebe8e2\">{H(m.Groups[2].Value)}</td></tr>");
                }
                sb.Append("</table>");
            }
            else sb.Append($"<p style=\"margin:0 0 14px\">{string.Join("<br>", lineas.Select(H))}</p>");
        }
        return sb.ToString();
    }

    // «⏰ Llamar al taller» → icono aparte y título
    private static (string? Icono, string Titulo) Separar(string asunto)
    {
        var a = asunto.Trim();
        if (a.Length == 0) return (null, "Aviso");
        var e = StringInfo.GetNextTextElementLength(a);
        var primero = a[..e];
        return !char.IsLetterOrDigit(primero, 0) && a.Length > e && a[e] == ' ' ? (primero, a[(e + 1)..].Trim()) : (null, a);
    }

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    [GeneratedRegex(@"^([^:]{1,24}):\s+(.+)$")]
    private static partial Regex Ficha();
}
