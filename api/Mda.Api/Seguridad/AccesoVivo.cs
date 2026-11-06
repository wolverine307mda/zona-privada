using Mda.Api.Sistema;
using Microsoft.AspNetCore.SignalR;

namespace Mda.Api.Seguridad;

// Cada acceso (bien o mal) se cuenta al instante a las pestañas con sesión por el canal en tiempo real («acceso»): el radar del globo lanza su rayo sin esperar al repaso de cada 12 s.
// Solo llega a quien ya tiene sesión (el canal exige estar dentro) y con lo mismo que enseña la lista de accesos.
public static class AccesoVivo
{
    public static async Task Emitir(IHubContext<SistemaHub> hub, GeoIp geo, Acceso a)
    {
        try
        {
            var sitio = geo.Buscar(a.Ip);
            var lugar = sitio is null ? null : sitio.Ciudad.Length > 0 ? $"{sitio.Ciudad}, {sitio.Pais}" : sitio.Pais;
            await hub.Clients.All.SendAsync("acceso", new { a.Id, a.Fecha, a.Exito, a.Motivo, Lugar = lugar, Lat = sitio?.Lat, Lon = sitio?.Lon });
        }
        catch (Exception) { /* el aviso en directo es un extra: si falla, el radar lo verá en el siguiente repaso */ }
    }
}
