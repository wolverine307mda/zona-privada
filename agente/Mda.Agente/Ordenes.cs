using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// Las órdenes que manda la zona privada. El agente NUNCA abre un puerto: es él quien pregunta a la API (conexión saliente).
// Solo ejecuta una lista cerrada y solo si la firma cuadra con el secreto compartido y no ha caducado:
// una orden inventada, retocada, repetida tarde o de otro tipo se descarta.
record OrdenRecibida(int Id, string Tipo, string Parametro, long Caduca, string Firma);
record ResultadoOrden(bool Ok, string Resultado);
record TextoPortapapeles(string Texto);

static partial class Ordenes
{
    static readonly HashSet<string> Permitidas = ["bloquear", "despierto", "reiniciar", "suspender", "apagar", "cancelar", "volumen", "brillo", "multimedia", "portapapeles", "copiar", "wol", "energia", "limpiar", "bateria-informe", "abrir", "velocidad", "captura", "descargar", "ubicacion", "espacio", "apps", "abrir-app", "procesos", "cerrar", "recibir"];

    public static async Task Bucle(HttpClient esperaLarga, string secreto, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // la API retiene la petición hasta 20 s si no hay órdenes
                var lista = await esperaLarga.GetFromJsonAsync<List<OrdenRecibida>>("/api/sistema/agente/ordenes", ct) ?? [];
                foreach (var o in lista)
                {
                    var r = await EjecutarAsync(o, secreto, esperaLarga, ct);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] orden {o.Tipo}: {(r.Ok ? "hecha" : "rechazada")} ({r.Resultado})");
                    try { await esperaLarga.PostAsJsonAsync($"/api/sistema/agente/ordenes/{o.Id}", r, ct); } catch (HttpRequestException) { /* la API no se entera: caduca */ }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct); // API reiniciándose o sin red: se reintenta con calma
            }
        }
    }

    static string Firma(string secreto, OrdenRecibida o) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), Encoding.UTF8.GetBytes($"{o.Id}|{o.Tipo}|{o.Parametro}|{o.Caduca}"))).ToLowerInvariant();

    // firma, caducidad y lista cerrada: lo primero que se mira siempre, sea cual sea la orden
    static ResultadoOrden? Rechazo(OrdenRecibida o, string secreto)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Firma(secreto, o)), Encoding.UTF8.GetBytes(o.Firma ?? "")))
            return new(false, "firma no válida");
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > o.Caduca) return new(false, "caducada");
        if (!Permitidas.Contains(o.Tipo)) return new(false, "orden no permitida");
        return null;
    }

    // las órdenes del portapapeles hablan con la API (el texto no viaja en la orden, solo su número)
    static async Task<ResultadoOrden> EjecutarAsync(OrdenRecibida o, string secreto, HttpClient http, CancellationToken ct)
    {
        if (Rechazo(o, secreto) is { } no) return no;
        try
        {
            if (o.Tipo == "portapapeles")
            {
                var texto = (await http.GetFromJsonAsync<TextoPortapapeles>($"/api/sistema/agente/portapapeles/{int.Parse(o.Parametro)}", ct))?.Texto;
                if (string.IsNullOrEmpty(texto)) return new(false, "ese texto ya no está");
                return await Dispositivos.PonerPortapapeles(texto) ? new(true, "copiado al portapapeles del PC") : new(false, "Windows no dejó copiarlo");
            }
            if (o.Tipo == "bateria-informe") return await InformeDeBateria(http, ct);
            if (o.Tipo == "velocidad") return await TestDeVelocidad(http, ct);
            if (o.Tipo == "espacio") return await QueOcupa(o.Parametro, http, ct);
            if (o.Tipo == "apps") return await ListaDeApps(http, ct);
            if (o.Tipo == "recibir") return await Recibir(o.Parametro, http, ct);
            if (o.Tipo == "abrir-app") { var (ok, m) = await AppsPc.Abrir(o.Parametro, ct); return new(ok, m); }
            if (o.Tipo == "procesos") return await ListaDeProcesos(http, ct);
            if (o.Tipo == "cerrar")
            {
                if (AppsPc.Cierre(o.Parametro) is not { } c) return new(false, "orden de cierre no válida");
                var (ok, m) = await AppsPc.Cerrar(c.Pid, c.Inicio, c.Forzar, ct);
                return new(ok, m);
            }
            if (o.Tipo == "captura") return await Captura.Hacer(o.Parametro, http, ct);
            if (o.Tipo == "ubicacion") return await Ubicacion.Hacer(o.Parametro, ct);
            // la descarga sigue en segundo plano y manda su resultado final a la misma orden cuando acaba
            if (o.Tipo == "descargar") return Descargas.Iniciar(o.Parametro, r => PublicarResultado(http, o.Id, r), ct);
            if (o.Tipo == "copiar")
            {
                var texto = await Dispositivos.LeerPortapapeles();
                if (string.IsNullOrWhiteSpace(texto)) return new(false, "el portapapeles del PC está vacío (o no es texto)");
                var r = await http.PostAsJsonAsync("/api/sistema/agente/portapapeles", new TextoPortapapeles(texto.Length > 2000 ? texto[..2000] : texto), ct);
                return r.IsSuccessStatusCode ? new(true, "portapapeles del PC traído") : new(false, $"la API responde {(int)r.StatusCode}");
            }
        }
        catch (Exception e) when (e is HttpRequestException or FormatException or System.Text.Json.JsonException) { return new(false, e.Message); }
        return Ejecutar(o, secreto);
    }

    static async Task PublicarResultado(HttpClient http, int id, ResultadoOrden r)
    {
        try { await http.PostAsJsonAsync($"/api/sistema/agente/ordenes/{id}", r); } catch (HttpRequestException) { /* la API no se entera */ }
    }

    internal static ResultadoOrden Ejecutar(OrdenRecibida o, string secreto)
    {
        if (Rechazo(o, secreto) is { } no) return no;
        try
        {
            switch (o.Tipo)
            {
                case "bloquear": return LockWorkStation() ? new(true, "pantalla bloqueada") : new(false, "Windows no la dejó bloquear");
                case "despierto": Despierto.Poner(o.Parametro != "off"); return new(true, o.Parametro == "off" ? "puede suspenderse" : "se mantiene despierto");
                case "reiniciar": Lanzar("shutdown", "/r /t 30 /c \"Zona privada: reinicio en 30 s (cancelable)\""); return new(true, "reinicio en 30 s");
                case "apagar":
                    if (Apagado.Segundos(o.Parametro) is not { } seg) return new(false, "tiempo de apagado no válido");
                    Lanzar("shutdown", $"/s /t {seg} /c \"Zona privada: apagado en {Apagado.Texto(seg)} (cancelable)\"");
                    return new(true, $"apagado en {Apagado.Texto(seg)}");
                case "suspender": Lanzar("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"); return new(true, "suspendiendo");
                case "cancelar": Lanzar("shutdown", "/a"); return new(true, "cancelado");
                case "multimedia":
                    return Dispositivos.TeclaMultimedia(o.Parametro) ? new(true, $"música: {o.Parametro}") : new(false, "acción de música no válida");
                case "wol": return Despertar(o.Parametro);
                case "energia": return PlanEnergia(o.Parametro);
                case "limpiar": return LimpiarTemporales();
                case "abrir": return AbrirEnElPc(o.Parametro);
                case "volumen":
                    if (!int.TryParse(o.Parametro, out var vol) || vol is < 0 or > 100) return new(false, "volumen no válido");
                    return Dispositivos.PonerVolumen(vol) ? new(true, $"volumen al {vol} %") : new(false, "Windows no dejó cambiar el volumen");
                case "brillo":
                    if (!int.TryParse(o.Parametro, out var bri) || bri is < 0 or > 100) return new(false, "brillo no válido");
                    return Dispositivos.PonerBrillo(bri) ? new(true, $"brillo al {bri} %") : new(false, "esta pantalla no deja cambiar el brillo (¿es un portátil?)");
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return new(false, e.Message); }
        return new(false, "sin acción");
    }

    // Abrir en el PC: la dirección se abre con el explorador de Windows (así el navegador no hereda los permisos de administrador del agente)
    // y las aplicaciones son solo las de la lista. Nada pasa por una consola.
    static ResultadoOrden AbrirEnElPc(string parametro)
    {
        if (Abrir.Destino(parametro) is not { } d) return new(false, "eso no se puede abrir: solo direcciones http(s) o las aplicaciones de la lista");
        if (d.Tipo == "url") Process.Start(new ProcessStartInfo("explorer.exe", d.Valor) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        else Process.Start(new ProcessStartInfo(d.Valor) { UseShellExecute = true })?.Dispose();
        return new(true, d.Tipo == "url" ? "dirección abierta en el navegador del PC" : $"{parametro.Trim().ToLowerInvariant()} abierto");
    }

    // «Enviar al PC»: baja el archivo que subiste a la app y lo deja en «Zona privada\Bandeja\Recibidos» (nombre seguro, sin pisar nada, sin ejecutables)
    static async Task<ResultadoOrden> Recibir(string parametro, HttpClient http, CancellationToken ct)
    {
        if (!int.TryParse(parametro, out var id) || id <= 0) return new(false, "orden de recepción no válida");
        try
        {
            using var r = await http.GetAsync($"/api/sistema/agente/bandeja/{id}", HttpCompletionOption.ResponseHeadersRead, ct);
            if (!r.IsSuccessStatusCode) return await Hecho(http, id, false, $"la API responde {(int)r.StatusCode}", ct);
            var nombre = BandejaLogica.NombreSeguro(r.Content.Headers.ContentDisposition?.FileNameStar ?? r.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            if (nombre is null || BandejaLogica.Bloqueada(nombre)) return await Hecho(http, id, false, "nombre o tipo de archivo no permitido", ct);
            if ((r.Content.Headers.ContentLength ?? 0) > BandejaLogica.Maximo) return await Hecho(http, id, false, "pasa de 100 MB", ct);
            var carpeta = Path.Combine(Vigias.RutaBandeja, "Recibidos");
            Directory.CreateDirectory(carpeta);
            var destino = Path.Combine(carpeta, BandejaLogica.Unico(nombre, n => File.Exists(Path.Combine(carpeta, n))));
            await using (var f = File.Create(destino)) await r.Content.CopyToAsync(f, ct);
            return await Hecho(http, id, true, $"guardado como {Path.GetFileName(destino)}", ct);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException) { return await Hecho(http, id, false, "no se pudo guardar: " + e.Message, ct); }
    }

    static async Task<ResultadoOrden> Hecho(HttpClient http, int id, bool ok, string mensaje, CancellationToken ct)
    {
        try { await http.PostAsJsonAsync($"/api/sistema/agente/bandeja/{id}/hecho", new { ok, mensaje }, ct); } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { /* la API no se entera: el archivo ya está */ }
        return new(ok, mensaje);
    }

    // El catálogo de aplicaciones del menú Inicio (nombre y AppID) para que elijas cuáles se pueden abrir desde la app
    static async Task<ResultadoOrden> ListaDeApps(HttpClient http, CancellationToken ct)
    {
        try
        {
            var apps = await AppsPc.Instaladas(ct);
            var r = await http.PostAsJsonAsync("/api/sistema/agente/apps", new { apps }, ct);
            return r.IsSuccessStatusCode ? new(true, $"{apps.Count} aplicaciones") : new(false, $"la API responde {(int)r.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception) { return new(false, "no se pudo leer la lista: " + e.Message); }
    }

    // Las aplicaciones con ventana abierta (nombre, memoria, hora de inicio) para poder cerrarlas desde la app
    static async Task<ResultadoOrden> ListaDeProcesos(HttpClient http, CancellationToken ct)
    {
        try
        {
            var l = await Task.Run(AppsPc.ConVentana, ct);
            var r = await http.PostAsJsonAsync("/api/sistema/agente/procesos", new { procesos = l }, ct);
            return r.IsSuccessStatusCode ? new(true, $"{l.Count} aplicaciones abiertas") : new(false, $"la API responde {(int)r.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return new(false, "no se pudo mandar la lista: " + e.Message); }
    }

    // «Qué ocupa»: cuenta las carpetas de una raíz (solo lectura, unidades locales, 60 s como mucho) y manda a la API las 15 mayores
    static async Task<ResultadoOrden> QueOcupa(string parametro, HttpClient http, CancellationToken ct)
    {
        if (Espacio.RaizValida(parametro) is not { } raiz) return new(false, "carpeta no válida");
        try
        {
            if (new DriveInfo(raiz[..1]).DriveType != DriveType.Fixed) return new(false, "solo se mira en unidades locales del PC");
            if (!Directory.Exists(raiz)) return new(false, "esa carpeta no existe");
            var r = await Task.Run(() => Espacio.Escanear(raiz), ct);
            var enviado = await http.PostAsJsonAsync("/api/sistema/agente/espacio", r, ct);
            var texto = $"{r.Carpetas.Count} carpetas en {r.Raiz}{(r.Parcial ? " (parcial: se cortó a los 60 s)" : "")}";
            return enviado.IsSuccessStatusCode ? new(true, texto) : new(false, $"la API responde {(int)enviado.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or ArgumentException) { return new(false, "no se pudo mirar: " + e.Message); }
    }

    // Test de velocidad: mide contra Cloudflare (unos 35 MB) y manda a la API las tres cifras
    static async Task<ResultadoOrden> TestDeVelocidad(HttpClient http, CancellationToken ct)
    {
        try
        {
            var m = await Velocidad.Medir(ct);
            var r = await http.PostAsJsonAsync("/api/sistema/agente/velocidad", m, ct);
            var texto = $"↓ {m.BajadaMbps:0} Mbps · ↑ {m.SubidaMbps:0} Mbps" + (m.PingMs is { } p ? $" · {p} ms" : "");
            return r.IsSuccessStatusCode ? new(true, texto) : new(false, $"la API responde {(int)r.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return new(false, "no se pudo medir: " + e.Message); }
    }

    // Informe de batería: Windows lo genera en XML, el agente lo interpreta y manda a la API solo tres números (diseño, actual y ciclos)
    static async Task<ResultadoOrden> InformeDeBateria(HttpClient http, CancellationToken ct)
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"mda-bateria-{Guid.NewGuid():N}.xml");
        try
        {
            if (Correr("powercfg", $"/batteryreport /xml /output \"{ruta}\"", 45_000) != 0 || !File.Exists(ruta)) return new(false, "Windows no pudo hacer el informe (¿este equipo no tiene batería?)");
            if (InformeBateria.Parsear(await File.ReadAllTextAsync(ruta, ct)) is not { } d) return new(false, "el informe no trae datos de batería");
            var r = await http.PostAsJsonAsync("/api/sistema/agente/bateria", new { d.Diseno, d.Completa, d.Ciclos }, ct);
            return r.IsSuccessStatusCode ? new(true, $"desgaste del {d.Desgaste:0} %: carga {d.Completa / 1000.0:0.0} Wh de {d.Diseno / 1000.0:0.0}") : new(false, $"la API responde {(int)r.StatusCode}");
        }
        finally { try { File.Delete(ruta); } catch (IOException) { } }
    }

    // Limpiar: los temporales del usuario y de Windows con más de un día (lo que esté en uso se queda) y la papelera de reciclaje.
    // Solo esas carpetas fijas: la orden no lleva ninguna ruta.
    static ResultadoOrden LimpiarTemporales()
    {
        var limite = DateTime.UtcNow.AddDays(-1);
        long bytes = 0; var archivos = 0;
        foreach (var carpeta in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") })
        {
            var (b, n) = Limpieza.Barrer(carpeta, limite);
            bytes += b; archivos += n;
        }
        var papelera = new InfoPapelera { Tamano = Marshal.SizeOf<InfoPapelera>() };
        var vaciada = false;
        if (ConsultarPapelera(null, ref papelera) == 0 && papelera.Bytes > 0 && VaciarPapelera(IntPtr.Zero, null, 0x7) == 0) { bytes += papelera.Bytes; vaciada = true; }
        return new(true, $"liberados {Limpieza.Tam(bytes)}: {archivos} archivos temporales{(vaciada ? " y la papelera" : "")}");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)] struct InfoPapelera { public int Tamano; public long Bytes; public long Elementos; }
    [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode)] static extern int ConsultarPapelera(string? raiz, ref InfoPapelera info);
    [DllImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", CharSet = CharSet.Unicode)] static extern int VaciarPapelera(IntPtr ventana, string? raiz, uint opciones);

    static bool cineActivo;

    // Plan de energía con «powercfg». Si Windows no trae el de alto rendimiento (los portátiles modernos a veces lo esconden) se vuelve a crear.
    // «cine» es el equilibrado con el PC y la pantalla despiertos; al elegir otro plan se deja de forzar.
    static ResultadoOrden PlanEnergia(string perfil)
    {
        if (Energia.Plan(perfil) is not { } plan) return new(false, "plan de energía no válido");
        var codigo = Correr("powercfg", $"/setactive {plan}");
        if (codigo != 0 && perfil == "rendimiento") { Correr("powercfg", $"-duplicatescheme {plan} {plan}"); codigo = Correr("powercfg", $"/setactive {plan}"); }
        if (codigo != 0) return new(false, "Windows no tiene ese plan de energía en este equipo");
        if (perfil == "cine") { Despierto.Poner(true, pantalla: true); cineActivo = true; }
        else if (cineActivo) { Despierto.Poner(false); cineActivo = false; }
        return new(true, perfil switch { "rendimiento" => "plan de alto rendimiento", "ahorro" => "plan de ahorro de energía", "cine" => "modo cine: equilibrado y sin que se apague la pantalla", _ => "plan equilibrado" });
    }

    static int Correr(string exe, string args, int milisegundos = 15_000)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
        if (p is null) return -1;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        return p.WaitForExit(milisegundos) ? p.ExitCode : -1;
    }

    // lo manda desde cada interfaz IPv4 activa: la de casa, la de Tailscale… (la que no deje difundir se salta)
    static ResultadoOrden Despertar(string mac)
    {
        if (PaqueteMagico.De(mac) is not { } paquete) return new(false, "dirección MAC no válida");
        var enviados = 0;
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback))
        {
            foreach (var ua in nic.GetIPProperties().UnicastAddresses.Where(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && u.IPv4Mask is not null))
            {
                try
                {
                    var ip = ua.Address.GetAddressBytes(); var masc = ua.IPv4Mask!.GetAddressBytes();
                    var difusion = new System.Net.IPAddress(PaqueteMagico.Difusion(ip, masc));
                    using var udp = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(ua.Address, 0)) { EnableBroadcast = true };
                    udp.Send(paquete, paquete.Length, new System.Net.IPEndPoint(difusion, 9));
                    udp.Send(paquete, paquete.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 9));
                    enviados++;
                }
                catch (System.Net.Sockets.SocketException) { /* esa interfaz no deja difundir: se prueba con las demás */ }
            }
        }
        return enviados > 0 ? new(true, $"paquete de despertar enviado por {enviados} interfaz(es)") : new(false, "no hay ninguna red por la que difundir");
    }

    static void Lanzar(string exe, string args) =>
        Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false })?.Dispose();

    [LibraryImport("user32.dll", EntryPoint = "LockWorkStation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();
}

// «Mantener despierto»: Windows pide que SetThreadExecutionState lo llame un hilo que siga vivo, así que hay uno dedicado
static class Despierto
{
    const uint Continuo = 0x80000000, Sistema = 0x00000001, Pantalla = 0x00000002;
    static Thread? hilo;
    static readonly ManualResetEventSlim Parar = new(false);

    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint estado);

    public static void Poner(bool activo, bool pantalla = false)
    {
        // si ya había uno, se para y se vuelve a crear con lo nuevo (con o sin la pantalla encendida)
        if (hilo is { IsAlive: true }) { Parar.Set(); hilo.Join(500); hilo = null; }
        if (!activo) return;
        Parar.Reset();
        hilo = new Thread(() => { SetThreadExecutionState(Continuo | Sistema | (pantalla ? Pantalla : 0)); Parar.Wait(); SetThreadExecutionState(Continuo); }) { IsBackground = true, Name = "despierto" };
        hilo.Start();
    }
}
