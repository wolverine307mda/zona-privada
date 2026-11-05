using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;

// Fotos bajo demanda del PC: la pantalla (al instante, sin nada más) o la cámara web (con ffmpeg, gratis: «winget install Gyan.FFmpeg»).
// Solo se hacen si la zona privada manda una orden firmada, que a su vez solo existe si tú has encendido las fotos en sus ajustes. Una sola imagen por orden,
// nada de vídeo ni de audio; la foto sale directa a la API, que la guarda cifrada. En el PC no queda ningún archivo (la de la cámara se borra al subirla).
//   AGENTE_FFMPEG   ruta de ffmpeg.exe si no está en el PATH
//   AGENTE_CAMARA   nombre de la cámara (tal como la lista «ffmpeg -list_devices true -f dshow -i dummy»); por defecto, la primera
static class Captura
{
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int indice);
    const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    public static async Task<ResultadoOrden> Hacer(string parametro, HttpClient http, CancellationToken ct)
    {
        if (!CapturaLogica.Valido(parametro, out var origen, out var motivo)) return new(false, "parámetro no válido");
        var (jpeg, error) = origen == "pantalla" ? (Pantalla(), "no se pudo capturar la pantalla (¿está el PC bloqueado?)") : await Camara(ct);
        if (jpeg is null) return new(false, error);
        using var contenido = new ByteArrayContent(jpeg);
        contenido.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var r = await http.PostAsync($"/api/sistema/agente/captura?origen={origen}&motivo={motivo}", contenido, ct);
        return r.IsSuccessStatusCode ? new(true, "foto guardada (cifrada)")
            : r.StatusCode == System.Net.HttpStatusCode.Conflict ? new(false, "las fotos están apagadas en la zona privada")
            : new(false, $"la API responde {(int)r.StatusCode}");
    }

    // Todas las pantallas juntas, reducidas a 1280 px y en JPEG
    static byte[]? Pantalla()
    {
        try
        {
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN), w = GetSystemMetrics(SM_CXVIRTUALSCREEN), h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (w < 1 || h < 1) return null;
            using var completa = new Bitmap(w, h);
            using (var g = Graphics.FromImage(completa)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
            var (aw, ah) = CapturaLogica.Reducir(w, h);
            using var final = aw == w && ah == h ? completa : new Bitmap(completa, aw, ah);
            using var ms = new MemoryStream();
            var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parametros = new EncoderParameters(1);
            parametros.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
            final.Save(ms, jpeg, parametros);
            return ms.ToArray();
        }
        catch (Exception e) when (e is ExternalException or ArgumentException or InvalidOperationException or Win32Exception) { return null; }
    }

    static async Task<(byte[]?, string)> Camara(CancellationToken ct)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("AGENTE_FFMPEG") is { Length: > 0 } a ? a : BuscarEnPath("ffmpeg.exe");
        if (ffmpeg is null) return (null, "falta ffmpeg para usar la cámara (instálalo con «winget install Gyan.FFmpeg»)");
        try
        {
            var nombre = Environment.GetEnvironmentVariable("AGENTE_CAMARA") is { Length: > 0 } c ? c : null;
            if (nombre is null)
            {
                var lista = await Ejecutar(ffmpeg, ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"], TimeSpan.FromSeconds(10), ct);
                nombre = CapturaLogica.CamarasDe(lista).FirstOrDefault();
            }
            if (nombre is null) return (null, "no encuentro ninguna cámara conectada");
            var tmp = Path.Combine(Path.GetTempPath(), $"mda-cam-{Guid.NewGuid():N}.jpg");
            try
            {
                // los primeros fotogramas salen oscuros (la cámara ajusta la exposición): se descartan y se guarda el 13.º
                await Ejecutar(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "dshow", "-video_size", "1280x720", "-i", $"video={nombre}",
                    "-vf", "select=gte(n\\,12)", "-frames:v", "1", "-fps_mode", "passthrough", "-q:v", "4", tmp], TimeSpan.FromSeconds(25), ct);
                return File.Exists(tmp) && new FileInfo(tmp).Length > 0 ? (await File.ReadAllBytesAsync(tmp, ct), "") : (null, "la cámara no dio imagen (¿la usa otra aplicación o Windows no la deja a las apps de escritorio?)");
            }
            finally { try { File.Delete(tmp); } catch (IOException) { /* la limpia Windows con los temporales */ } }
        }
        catch (Exception e) when (e is Win32Exception or IOException) { return (null, "no se pudo usar ffmpeg: " + e.Message); }
    }

    static string? BuscarEnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => { try { return Path.Combine(d.Trim('"'), exe); } catch (ArgumentException) { return ""; } }).FirstOrDefault(File.Exists);

    // ffmpeg cuenta lo que lista por la salida de error; se devuelven las dos juntas
    static async Task<string> Ejecutar(string programa, string[] argumentos, TimeSpan limite, CancellationToken ct)
    {
        var info = new ProcessStartInfo(programa) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in argumentos) info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new Win32Exception("no se pudo arrancar " + programa);
        var salida = p.StandardOutput.ReadToEndAsync(ct);
        var error = p.StandardError.ReadToEndAsync(ct);
        using var tiempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tiempo.CancelAfter(limite);
        try { await p.WaitForExitAsync(tiempo.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { /* ya había terminado */ } if (ct.IsCancellationRequested) throw; }
        return await salida + "\n" + await error;
    }
}
