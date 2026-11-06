using System.Net;
using System.Net.Sockets;

// «Descargar en el PC»: la zona privada manda una dirección (firmada, con Face ID) y el agente la baja a «Descargas\Zona privada».
// Se hace en segundo plano para no frenar las demás órdenes; al acabar manda a la API el resultado de la misma orden (la API te avisa por Telegram).
// Sin rutas en la orden, sin programas, sin la red local y sin pasar de 4 GB: ver DescargaLogica.
static class Descargas
{
    static readonly string Carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Zona privada");
    static readonly TimeSpan Limite = TimeSpan.FromHours(2);

    // Devuelve enseguida («descargando…») y sigue en segundo plano; `informar` manda el resultado final
    public static ResultadoOrden Iniciar(string parametro, Func<ResultadoOrden, Task> informar, CancellationToken ct)
    {
        if (DescargaLogica.Direccion(parametro) is not { } url) return new(false, "eso no se puede descargar: solo direcciones http(s) normales");
        _ = Task.Run(async () =>
        {
            ResultadoOrden r;
            try { r = await Bajar(url, ct); }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException or UnauthorizedAccessException or SocketException)
            { r = new(false, "falló la descarga: " + e.Message); }
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);   // que el «descargando…» inicial llegue antes que el final
            try { await informar(r); } catch (Exception) { /* la API no se entera: el archivo ya está */ }
        }, CancellationToken.None);
        return new(true, $"descargando {url.Host}…");
    }

    // Un cliente que solo conecta a direcciones de Internet: lo comprueba al conectar de verdad (no antes), así un DNS que cambie no lo engaña
    static HttpClient Cliente()
    {
        var manejador = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,      // las redirecciones se siguen a mano, comprobando cada salto
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            ConnectCallback = async (contexto, ct) =>
            {
                var todas = await Dns.GetHostAddressesAsync(contexto.DnsEndPoint.Host, ct);
                var buenas = todas.Where(i => !DescargaLogica.EsLocal(i)).ToArray();
                if (buenas.Length == 0 || buenas.Length != todas.Length) throw new HttpRequestException("esa dirección apunta a la red local o a este PC: no se descarga");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try { await socket.ConnectAsync(buenas, contexto.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            },
        };
        var cliente = new HttpClient(manejador, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("ZonaPrivada-Agente/1.0");
        return cliente;
    }

    static async Task<ResultadoOrden> Bajar(Uri inicial, CancellationToken ct)
    {
        using var tope = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tope.CancelAfter(Limite);
        using var http = Cliente();
        var url = inicial;
        HttpResponseMessage? r = null;
        try
        {
            for (var salto = 0; ; salto++)
            {
                r?.Dispose();
                r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, tope.Token);
                if ((int)r.StatusCode is >= 300 and < 400 && r.Headers.Location is { } destino)
                {
                    if (salto >= DescargaLogica.MaxRedirecciones) return new(false, "demasiadas redirecciones");
                    var siguiente = destino.IsAbsoluteUri ? destino : new Uri(url, destino);
                    if (DescargaLogica.Direccion(siguiente.AbsoluteUri) is not { } valida) return new(false, "la redirección lleva a una dirección no permitida");
                    url = valida;
                    continue;
                }
                break;
            }
            if (r is null) return new(false, "sin respuesta");
            if (!r.IsSuccessStatusCode) return new(false, $"el servidor responde {(int)r.StatusCode}");
            var largo = r.Content.Headers.ContentLength;
            if (largo > DescargaLogica.MaxBytes) return new(false, $"pesa demasiado ({Limpieza.Tam(largo.Value)}; el máximo son {Limpieza.Tam(DescargaLogica.MaxBytes)})");

            var propuesto = r.Content.Headers.ContentDisposition?.FileNameStar ?? r.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            var nombre = DescargaLogica.Nombre(propuesto, url);
            if (DescargaLogica.Bloqueado(nombre)) return new(false, $"«{nombre}» es un programa: no se descargan programas desde la app");

            Directory.CreateDirectory(Carpeta);
            var libre = new DriveInfo(Path.GetPathRoot(Carpeta)!).AvailableFreeSpace;
            if (largo is { } l && libre - l < DescargaLogica.ReservaLibre) return new(false, "no hay espacio libre suficiente (se deja una reserva de 2 GB)");

            var parcial = Path.Combine(Carpeta, $".{Guid.NewGuid():N}.parte");
            long total = 0;
            try
            {
                await using (var origen = await r.Content.ReadAsStreamAsync(tope.Token))
                await using (var destino = new FileStream(parcial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    int n;
                    while ((n = await origen.ReadAsync(buffer, tope.Token)) > 0)
                    {
                        total += n;
                        if (total > DescargaLogica.MaxBytes) return new(false, "pasó del máximo de 4 GB y se canceló");
                        if (libre - total < DescargaLogica.ReservaLibre) return new(false, "se acabó el espacio libre (se deja una reserva de 2 GB) y se canceló");
                        await destino.WriteAsync(buffer.AsMemory(0, n), tope.Token);
                    }
                }
                var final = DescargaLogica.Libre(Carpeta, nombre);
                File.Move(parcial, Path.Combine(Carpeta, final));
                return new(true, $"descargado: {final} ({Limpieza.Tam(total)}) en Descargas\\Zona privada");
            }
            finally { try { File.Delete(parcial); } catch (IOException) { } }
        }
        finally { r?.Dispose(); }
    }
}
