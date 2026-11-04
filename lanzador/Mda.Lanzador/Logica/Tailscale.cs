using System.Text.Json;

namespace Mda.Lanzador;

// Tailscale de Windows para la versión de escritorio: las órdenes de «tailscale serve / funnel» y la lectura de su estado. Todo puro.
// «serve» enseña la app solo a tus dispositivos de la Tailnet; «funnel» la abre a Internet y solo se activa cuando tú lo decides (y sin ensayo).
public record EstadoTailscale(bool Conectado, string? Dns)
{
    public string? Url => Dns is { Length: > 0 } d ? "https://" + d : null;
}

public static class TailscaleOrdenes
{
    public static string Servir(int puerto) => $"serve --bg --https=443 http://127.0.0.1:{puerto}";
    public static string PararServir() => "serve --https=443 off";
    public static string AbrirAInternet(int puerto) => $"funnel --bg --https=443 http://127.0.0.1:{puerto}";
    public static string CerrarAInternet() => "funnel --https=443 off";
    public const string Estado = "status --json";

    // «tailscale status --json»: BackendState «Running» y el nombre de este equipo (Self.DNSName, con un punto final)
    public static EstadoTailscale LeerEstado(string json)
    {
        try
        {
            var raiz = JsonDocument.Parse(json).RootElement;
            var corriendo = raiz.TryGetProperty("BackendState", out var b) && b.GetString() == "Running";
            string? dns = raiz.TryGetProperty("Self", out var s) && s.TryGetProperty("DNSName", out var d) ? d.GetString()?.TrimEnd('.') : null;
            return new EstadoTailscale(corriendo, string.IsNullOrWhiteSpace(dns) ? null : dns);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return new EstadoTailscale(false, null); }
    }

    // dónde suele estar instalado, para buscarlo cuando no está en el PATH
    public static IEnumerable<string> Candidatos(string archivosDePrograma, string archivosDeProgramaX86) =>
        [Path.Combine(archivosDePrograma, "Tailscale", "tailscale.exe"), Path.Combine(archivosDeProgramaX86, "Tailscale", "tailscale.exe")];
}
