using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;

namespace Mda.Lanzador;

// El icono junto al reloj: arranca Docker y los contenedores, aloja el agente del PC y enseña cómo va todo
// (verde = todo en marcha, ámbar = funciona pero algo falla, rojo = no hay app).
sealed class Bandeja : ApplicationContext
{
    private const string Perfil = "publico";
    private static readonly string Datos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZonaPrivada");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly NotifyIcon _icono = new() { Visible = true };
    private readonly Dictionary<Nivel, Icon> _iconos = new();
    private readonly System.Windows.Forms.Timer _reloj = new() { Interval = 15_000 };
    private readonly ToolStripMenuItem _estado = new("Arrancando…") { Enabled = false };
    private readonly ToolStripMenuItem _inicio = new("Iniciar con Windows") { CheckOnClick = false };
    private readonly CancellationTokenSource _agente = new();
    private readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();

    private string _carpeta = "";
    private string _urlPublica = "";
    private string _estadoAgente = "arrancando";
    private Nivel? _nivel;
    private bool _ocupado;
    private DateTime _ultimoArranque = DateTime.MinValue;

    public Bandeja()
    {
        foreach (var n in Enum.GetValues<Nivel>()) _iconos[n] = Dibujar(n);
        _icono.Icon = _iconos[Nivel.Ambar];
        _icono.Text = "Zona privada: arrancando…";
        _icono.ContextMenuStrip = Menu();
        _icono.DoubleClick += (_, _) => Abrir(_urlPublica.Length > 0 ? _urlPublica : "http://localhost:8080");
        _reloj.Tick += async (_, _) => await Comprobar();

        if (!Configurar()) { ExitThread(); return; }
        _inicio.Checked = Instalador.TareaExiste();
        _ = ArrancarAgente();
        _ = ArrancarTodo(primeraVez: true);
        _reloj.Start();
    }

    // ---------- configuración: carpeta del proyecto, token y dirección pública ----------

    private bool Configurar()
    {
        Directory.CreateDirectory(Datos);
        var guardado = Path.Combine(Datos, "config.json");
        string? carpeta = null;
        try { if (File.Exists(guardado)) carpeta = JsonDocument.Parse(File.ReadAllText(guardado)).RootElement.GetProperty("carpeta").GetString(); } catch (Exception e) when (e is JsonException or KeyNotFoundException or IOException) { }
        if (carpeta is null || !File.Exists(Path.Combine(carpeta, "compose.yaml")))
            carpeta = Entorno.BuscarCarpeta([AppContext.BaseDirectory, Environment.CurrentDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Proyectos", "zona-privada")]);
        if (carpeta is null)
        {
            using var d = new FolderBrowserDialog { Description = "Elige la carpeta de la zona privada (la que tiene compose.yaml)", UseDescriptionForTitle = true };
            if (d.ShowDialog() != DialogResult.OK || !File.Exists(Path.Combine(d.SelectedPath, "compose.yaml"))) return false;
            carpeta = d.SelectedPath;
        }
        _carpeta = carpeta;
        File.WriteAllText(guardado, JsonSerializer.Serialize(new { carpeta }));
        var env = File.Exists(Path.Combine(carpeta, ".env")) ? Entorno.LeerEnv(File.ReadAllLines(Path.Combine(carpeta, ".env"))) : [];
        _urlPublica = env.GetValueOrDefault("URL_PUBLICA", "").TrimEnd('/');
        _token = env.GetValueOrDefault("AGENTE_TOKEN", "");
        return true;
    }
    private string _token = "";

    // ---------- agente del PC (el mismo bucle que mda-agente.exe) ----------

    private Task ArrancarAgente()
    {
        if (_token.Length == 0) { _estadoAgente = "falta AGENTE_TOKEN en el .env"; return Task.CompletedTask; }
        return Task.Run(async () =>
        {
            try { await AgenteServicio.Ejecutar("http://localhost:8080", _token, e => { _estadoAgente = e; Log($"agente: {e}"); }, _agente.Token); }
            catch (Exception e) when (e is not OperationCanceledException) { _estadoAgente = "error: " + e.Message; Log("agente cayó: " + e); }
        });
    }

    // ---------- Docker y contenedores ----------

    private async Task ArrancarTodo(bool primeraVez = false)
    {
        if (_ocupado) return;
        _ocupado = true; _ultimoArranque = DateTime.UtcNow;
        try
        {
            Estado("Arrancando Docker…", Nivel.Ambar);
            if (!await Docker.Responde())
            {
                Docker.AbrirDockerDesktop();
                var limite = DateTime.UtcNow.AddMinutes(4);
                while (DateTime.UtcNow < limite && !await Docker.Responde()) await Task.Delay(3000);
                if (!await Docker.Responde()) { Estado("Docker no arranca", Nivel.Rojo); Aviso("Docker no ha arrancado en 4 minutos. Ábrelo a mano."); return; }
            }
            Estado("Arrancando la zona privada…", Nivel.Ambar);
            var (codigo, salida) = await Docker.Cmd($"compose --profile {Perfil} up -d", _carpeta, 300);
            Log($"up -d → {codigo}: {salida.Trim()}");
            if (codigo != 0) Aviso("No se pudieron arrancar los contenedores. Mira el registro del lanzador.");
            else if (primeraVez) Aviso("Zona privada en marcha.");
        }
        finally { _ocupado = false; }
        await Comprobar();
    }

    private async Task Accion(string texto, string argumentos, int segundos = 300)
    {
        if (_ocupado) return;
        _ocupado = true;
        try
        {
            Estado(texto + "…", Nivel.Ambar);
            var (codigo, salida) = await Docker.Cmd(argumentos, _carpeta, segundos);
            Log($"{argumentos} → {codigo}: {salida.Trim()}");
            Aviso(codigo == 0 ? $"{texto}: hecho." : $"{texto}: falló (mira el registro).");
        }
        finally { _ocupado = false; }
        await Comprobar();
    }

    // ---------- comprobación periódica ----------

    private async Task Comprobar()
    {
        if (_ocupado) return;
        bool docker = await Docker.Responde();
        HashSet<string> esperados = [], corriendo = [];
        if (docker)
        {
            esperados = Entorno.Servicios((await Docker.Cmd($"compose --profile {Perfil} config --services", _carpeta, 20)).Salida);
            corriendo = Entorno.Servicios((await Docker.Cmd($"compose --profile {Perfil} ps --services --filter status=running", _carpeta, 20)).Salida);
        }
        var api = await Responde("http://localhost:8080/api/salud");
        bool? publico = _urlPublica.Length > 0 && api ? await Responde(_urlPublica + "/api/salud") : null;
        var (nivel, texto) = Estados.Resumir(new Comprobaciones(docker, esperados, corriendo, api, publico, _estadoAgente));
        Estado(texto, nivel);

        // si Docker está bien pero faltan contenedores (p. ej. tras un reinicio de Docker), se vuelven a levantar cada 5 minutos como mucho
        if (docker && nivel == Nivel.Rojo && DateTime.UtcNow - _ultimoArranque > TimeSpan.FromMinutes(5)) _ = ArrancarTodo();
    }

    private static async Task<bool> Responde(string url)
    {
        try { using var r = await Http.GetAsync(url); return r.IsSuccessStatusCode; } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return false; }
    }

    private void Estado(string texto, Nivel nivel)
    {
        _ui.Post(_ =>
        {
            _estado.Text = texto;
            _icono.Icon = _iconos[nivel];
            _icono.Text = ("Zona privada: " + texto)[..Math.Min(63, ("Zona privada: " + texto).Length)];
            // avisa solo al cambiar a rojo y al recuperarse, no cada 15 s
            if (_nivel is { } antes && antes != nivel)
            {
                if (nivel == Nivel.Rojo) Aviso($"Zona privada caída: {texto}");
                else if (antes == Nivel.Rojo && nivel == Nivel.Verde) Aviso("Zona privada recuperada.");
            }
            _nivel = nivel;
        }, null);
    }

    // ---------- menú ----------

    private ContextMenuStrip Menu()
    {
        var m = new ContextMenuStrip();
        void Item(string texto, Action alHacer) => m.Items.Add(texto, null, (_, _) => alHacer());
        m.Items.Add(_estado);
        m.Items.Add(new ToolStripSeparator());
        Item("Abrir la zona privada", () => Abrir(_urlPublica.Length > 0 ? _urlPublica : "http://localhost:8080"));
        Item("Abrir en este PC (localhost)", () => Abrir("http://localhost:8080"));
        Item("Registros (Seq)", () => Abrir("http://localhost:5341"));
        m.Items.Add(new ToolStripSeparator());
        Item("Reiniciar la API", () => _ = Accion("Reiniciando la API", $"compose --profile {Perfil} restart api", 120));
        Item("Actualizar (reconstruir la API)", () => _ = Accion("Reconstruyendo la API", $"compose --profile {Perfil} up -d --build api", 900));
        Item("Arrancar todo", () => _ = ArrancarTodo());
        Item("Parar todo", () => _ = Accion("Parando todo", $"compose --profile {Perfil} stop", 120));
        m.Items.Add(new ToolStripSeparator());
        _inicio.Click += async (_, _) =>
        {
            var ok = _inicio.Checked ? await Instalador.QuitarTarea() : await Instalador.CrearTarea();
            if (ok) _inicio.Checked = !_inicio.Checked; else Aviso("No se pudo cambiar el inicio con Windows.");
        };
        m.Items.Add(_inicio);
        Item("Abrir la carpeta", () => Abrir(_carpeta));
        Item("Abrir el registro del lanzador", () => Abrir(Path.Combine(Datos, "lanzador.log")));
        m.Items.Add(new ToolStripSeparator());
        Item("Salir (deja Docker en marcha)", Salir);
        return m;
    }

    private static void Abrir(string destino)
    {
        try { Process.Start(new ProcessStartInfo(destino) { UseShellExecute = true }); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException) { }
    }

    private void Aviso(string texto)
    {
        _ui.Post(_ => _icono.ShowBalloonTip(5000, "Zona privada", texto, ToolTipIcon.Info), null);
        Log(texto);
    }

    private static void Log(string texto)
    {
        try
        {
            var f = Path.Combine(Datos, "lanzador.log");
            if (File.Exists(f) && new FileInfo(f).Length > 512 * 1024) File.Move(f, f + ".1", true);
            File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {texto}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    private void Salir()
    {
        _agente.Cancel();
        _reloj.Stop();
        _icono.Visible = false;
        _icono.Dispose();
        ExitThread();
    }

    // un círculo de color con una «Z»
    private static Icon Dibujar(Nivel nivel)
    {
        var color = nivel switch { Nivel.Verde => Color.FromArgb(46, 184, 100), Nivel.Ambar => Color.FromArgb(245, 166, 35), _ => Color.FromArgb(229, 72, 77) };
        using var b = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var pincel = new SolidBrush(color);
            g.FillEllipse(pincel, 1, 1, 30, 30);
            using var fuente = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            var tam = g.MeasureString("Z", fuente);
            g.DrawString("Z", fuente, Brushes.White, (32 - tam.Width) / 2, (32 - tam.Height) / 2);
        }
        return Icon.FromHandle(b.GetHicon());
    }
}
