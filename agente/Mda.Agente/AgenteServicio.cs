using System.Net.Http.Json;
using Mda.Api.Sistema;

// El bucle del agente, compartido por el programa de consola (mda-agente.exe) y por el lanzador (que lo aloja dentro):
// cada 2 s las métricas del PC, cada 5 min la salud y, siempre, las órdenes firmadas (conexión saliente).
static class AgenteServicio
{
    public static async Task Ejecutar(string url, string token, Action<string> estadoCambiado, CancellationToken ct)
    {
        var lector = LectorSistema.ParaEsteEquipo();
        lector.Leer(); // primera lectura: fija la referencia de la CPU y de la red

        using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.Add("X-Agente-Token", token);
        using var esperaLarga = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(45) };
        esperaLarga.DefaultRequestHeaders.Add("X-Agente-Token", token);

        Vigias.Descargas(http, ct);
        Vigias.Bandeja(http, ct);
        _ = Task.Run(async () =>
        {
            try
            {
                using var cada5 = new PeriodicTimer(TimeSpan.FromMinutes(5));
                bool? reinicioAntes = null;
                do
                {
                    try
                    {
                        var s = Salud.Leer();
                        await http.PostAsJsonAsync("/api/sistema/agente/salud", s, ct);
                        // Windows acaba de pedir reiniciar (no al arrancar el agente, solo cuando pasa de «no» a «sí»)
                        if (s.ReinicioPendiente && reinicioAntes == false) await Vigias.Enviar(http, "reinicio-pendiente", "", ct);
                        reinicioAntes = s.ReinicioPendiente;
                    }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { /* la API no está: en 5 min otra vez */ }
                } while (await cada5.WaitForNextTickAsync(ct));
            }
            catch (OperationCanceledException) { }
        }, ct);
        _ = Ordenes.Bucle(esperaLarga, token, ct);

        // batería, volumen y brillo cambian despacio y WMI/Core Audio son caros: se miran cada 10 s
        (int Porcentaje, bool Cargando)? bateria = null; int? volumen = null, brillo = null;
        var ultimaLectura = DateTime.MinValue;

        string? ultimo = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                string estado;
                try
                {
                    if (DateTime.UtcNow - ultimaLectura > TimeSpan.FromSeconds(10))
                    {
                        (bateria, volumen, brillo) = (Dispositivos.Bateria(), Dispositivos.Volumen(), Dispositivos.Brillo());
                        ultimaLectura = DateTime.UtcNow;
                    }
                    var r = await http.PostAsJsonAsync("/api/sistema/agente", lector.Leer() with { Bateria = bateria?.Porcentaje, Cargando = bateria?.Cargando, Volumen = volumen, Brillo = brillo }, ct);
                    estado = r.IsSuccessStatusCode ? "conectado"
                        : r.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "token incorrecto"
                        : $"la API responde {(int)r.StatusCode}";
                }
                catch (HttpRequestException) { estado = "la API no responde (¿está arrancado Docker?)"; }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested) { estado = "la API tarda demasiado"; }

                // solo se avisa cuando cambia, para no llenar la consola
                if (estado != ultimo) estadoCambiado(estado);
                ultimo = estado;
            }
        }
        catch (OperationCanceledException) { }
    }
}
