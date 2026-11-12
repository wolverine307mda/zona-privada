using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Mda.Api.Avisos;

namespace Mda.Api.Seguridad;

public record ResultadoDireccion(bool Responde, int? DiasCertificado, string? Error, DateTimeOffset Hora);

// Lo que hay que decir tras cada comprobación (puro, para probarlo sin red): avisa al caerse (a la segunda vez seguida, para no gritar por un parpadeo),
// al volver, y cuando el certificado está a punto de caducar (una vez por certificado).
public sealed class EstadoDireccion
{
    public const int DiasAviso = 10;
    private int _fallos;
    private bool _avisadoCaida;
    private DateTimeOffset? _certAvisado;

    public ResultadoDireccion? Ultimo { get; private set; }

    public List<string> Evaluar(ResultadoDireccion r)
    {
        Ultimo = r;
        var avisos = new List<string>();
        if (!r.Responde)
        {
            _fallos++;
            if (_fallos >= 2 && !_avisadoCaida)
            {
                _avisadoCaida = true;
                avisos.Add($"🌐 La app no responde desde Internet ({r.Error ?? "sin respuesta"}). Si el PC está encendido, revisa el túnel de Cloudflare: `docker compose logs tunel`.");
            }
        }
        else
        {
            if (_avisadoCaida) avisos.Add("✅ La app vuelve a responder desde Internet.");
            _fallos = 0; _avisadoCaida = false;
            if (r.DiasCertificado is { } d && d <= DiasAviso)
            {
                // un certificado se avisa una vez al acercarse y otra vez si llega a caducar
                var caduca = r.Hora.AddDays(d).Date.AddTicks(d <= 0 ? 1 : 0);
                if (_certAvisado != caduca)
                {
                    _certAvisado = caduca;
                    avisos.Add(d <= 0 ? "🔐 El certificado HTTPS de la app ha caducado." : $"🔐 El certificado HTTPS de la app caduca en {d} día(s). Cloudflare debería renovarlo solo: si no lo hace, reinicia el túnel (`docker compose restart tunel`).");
                }
            }
        }
        return avisos;
    }
}

// Cada 10 minutos pide /api/salud a la dirección pública (App:UrlPublica) como lo haría cualquiera desde fuera y mira cuánto le queda al certificado.
// Es el complemento del latido de Healthchecks.io: ese avisa si el PC se apaga, este si el PC está bien pero el túnel o el certificado no.
public class VigilanteDireccion(IConfiguration config, EstadoDireccion estado, IAvisos avisos, TimeProvider reloj, ILogger<VigilanteDireccion> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (config["App:UrlPublica"] is not { Length: > 0 } url || !Uri.TryCreate(url.TrimEnd('/') + "/api/salud", UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try { await Task.Delay(TimeSpan.FromMinutes(2), reloj, ct); } catch (OperationCanceledException) { return; }
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(10), reloj);
        try
        {
            do
            {
                try { foreach (var a in estado.Evaluar(await Comprobar(uri, ct))) await avisos.PorTelegram(a, ct); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Fallo al vigilar la dirección pública"); }
            } while (await t.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    public async Task<ResultadoDireccion> Comprobar(Uri uri, CancellationToken ct)
    {
        X509Certificate2? cert = null;
        using var manejador = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, c, _, errores) => { if (c is not null) cert = new X509Certificate2(c); return errores == SslPolicyErrors.None; },
            },
        };
        using var cliente = new HttpClient(manejador) { Timeout = TimeSpan.FromSeconds(15) };
        var ahora = reloj.GetUtcNow();
        try
        {
            using var r = await cliente.GetAsync(uri, ct);
            int? dias = cert is null ? null : (int)Math.Floor((new DateTimeOffset(cert.NotAfter.ToUniversalTime()) - ahora).TotalDays);
            return new ResultadoDireccion(r.IsSuccessStatusCode, dias, r.IsSuccessStatusCode ? null : $"HTTP {(int)r.StatusCode}", ahora);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("La dirección pública no responde ({Motivo})", e.GetType().Name);
            int? dias = cert is null ? null : (int)Math.Floor((new DateTimeOffset(cert.NotAfter.ToUniversalTime()) - ahora).TotalDays);
            return new ResultadoDireccion(false, dias, e is HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException } ? "certificado no válido" : e.GetType().Name, ahora);
        }
    }
}
