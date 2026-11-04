using System.Diagnostics;

// Test de velocidad de Internet contra el servidor público de Cloudflare: latencia (mediana de 4 peticiones mínimas), bajada (hasta 25 MB o 12 s) y subida (8 MB).
// Gasta unos 35 MB por prueba. La cuenta de Mbps y la mediana van aparte y sin red para poder probarlas.
record MedidaVelocidad(double BajadaMbps, double SubidaMbps, int? PingMs);

static class Velocidad
{
    const string Base = "https://speed.cloudflare.com";

    // megabits por segundo (decimales: 1 Mbps = 1.000.000 bits) con un decimal
    public static double Mbps(long bytes, double segundos) => segundos > 0 ? Math.Round(bytes * 8 / 1_000_000.0 / segundos, 1) : 0;

    public static int? Mediana(IEnumerable<long> milisegundos)
    {
        var l = milisegundos.Order().ToList();
        return l.Count == 0 ? null : (int)Math.Round(l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2.0);
    }

    public static async Task<MedidaVelocidad> Medir(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        var pings = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            var reloj = Stopwatch.StartNew();
            using var r = await http.GetAsync($"{Base}/__down?bytes=0", ct);
            if (r.IsSuccessStatusCode) pings.Add(reloj.ElapsedMilliseconds);
        }

        long leidos = 0;
        var bajada = Stopwatch.StartNew();
        using (var corte = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            corte.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                using var r = await http.GetAsync($"{Base}/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, corte.Token);
                r.EnsureSuccessStatusCode();
                await using var s = await r.Content.ReadAsStreamAsync(corte.Token);
                var buffer = new byte[81920]; int n;
                while ((n = await s.ReadAsync(buffer, corte.Token)) > 0) leidos += n;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* pasaron los 12 s: se cuenta lo bajado hasta ahí */ }
        }
        var mbpsBajada = Mbps(leidos, bajada.Elapsed.TotalSeconds);

        var datos = new byte[8_000_000];
        var subida = Stopwatch.StartNew();
        using var up = await http.PostAsync($"{Base}/__up", new ByteArrayContent(datos), ct);
        up.EnsureSuccessStatusCode();
        return new MedidaVelocidad(mbpsBajada, Mbps(datos.Length, subida.Elapsed.TotalSeconds), Mediana(pings));
    }
}
