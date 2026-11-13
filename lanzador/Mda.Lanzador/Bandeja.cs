using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Mda.Lanzador;

// El icono junto al reloj: arranca Docker y los contenedores, aloja el agente del PC y enseña cómo va todo
// (verde = todo en marcha, ámbar = funciona pero algo falla, rojo = no hay app).
sealed class Bandeja : ApplicationContext
{
    // «publico» arranca también Tailscale (la salida a Internet); vacío, solo en este PC. Se cambia desde el menú y se guarda en config.json
    private string _perfil = "publico";
    private string Opc => _perfil.Length > 0 ? $"--profile {_perfil} " : "";
    private static readonly string Version = typeof(Bandeja).Assembly.GetName().Version?.ToString(3) ?? "1.0";
    private static readonly string Datos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZonaPrivada");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly NotifyIcon _icono = new() { Visible = true };
    private readonly Dictionary<Nivel, Icon> _iconos = new();
    private readonly System.Windows.Forms.Timer _reloj = new() { Interval = 15_000 };
    private readonly ToolStripMenuItem _estado = new("Arrancando…") { Enabled = false };
    private readonly ToolStripMenuItem _inicio = new("Iniciar con Windows") { CheckOnClick = false };
    private readonly ToolStripMenuItem _internet = new("Salida a Internet (Tailscale)");
    private CancellationTokenSource _agente = new();
    private readonly ToolStripMenuItem _modoNativo = new("Modo escritorio (beta, sin Docker)");
    private readonly ToolStripMenuItem _ensayoItem = new("Ensayo: no avisar ni tocar aparatos");
    private readonly ToolStripMenuItem _tsServe = new("Ver la versión de escritorio desde mis dispositivos (Tailscale)");
    private readonly ToolStripMenuItem _tsFunnel = new("Abrir la versión de escritorio a Internet (Funnel)");
    private readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();

    // «docker» (lo de siempre) o «nativo» (PostgreSQL y la API como procesos de Windows, sin Docker). En «nativo» el ensayo mantiene a Docker como producción:
    // la copia de escritorio no avisa ni toca aparatos, y el agente del PC sigue hablando con la API de Docker.
    private string _modo = "docker";
    private bool _ensayo = true;
    private string _instalacion = "";
    private string _urlNativa = "";   // la dirección de Tailscale de ESTE equipo, si se comparte; los passkeys de la versión de escritorio van atados a ella
    private ServidorNativo? _nativo;
    private Dictionary<string, string> _envNativo = [];
    private bool Nativo => _modo == "nativo";
    private string UrlLocal => Nativo ? ConfigNativa.UrlApi : "http://localhost:8080";
    private RutasNativas Rutas => new(_instalacion.Length > 0 ? _instalacion : AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ZonaPrivada"));

    private string _carpeta = "";
    private string _urlPublica = "";
    private string _estadoAgente = "arrancando";
    private Nivel? _nivel;
    private bool _ocupado;
    private DateTime _ultimoArranque = DateTime.MinValue;

    // «ZonaPrivada.exe --salir» (o quien abra este evento) pide salir con calma: lo usa actualizar-escritorio.ps1 antes de cambiar los archivos.
    internal const string EventoSalir = @"Global\ZonaPrivada.Lanzador.Salir";
    private EventWaitHandle? _pedirSalida;
    private readonly System.Windows.Forms.Timer _vigilaSalida = new() { Interval = 1000 };

    internal static int PedirSalida()
    {
        if (!EventWaitHandle.TryOpenExisting(EventoSalir, out var e)) return 1; // no hay lanzador abierto (o es de una versión sin esta señal)
        using (e) e.Set();
        return 0;
    }

    // Global\ y con permisos explícitos: quien pide salir puede ser el instalador con permisos de administrador o un servicio (SYSTEM).
    // Se mira desde el hilo de la interfaz (un reloj), no desde otro hilo, para que Salir() haga lo mismo que el menú.
    private void EscucharPeticionDeSalida()
    {
        try
        {
            var acl = new EventWaitHandleSecurity();
            var derechos = EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize;
            foreach (var sid in new SecurityIdentifier[] { WindowsIdentity.GetCurrent().User!, new(WellKnownSidType.BuiltinAdministratorsSid, null), new(WellKnownSidType.LocalSystemSid, null) })
                acl.AddAccessRule(new EventWaitHandleAccessRule(sid, derechos, AccessControlType.Allow));
            _pedirSalida = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, EventoSalir, out _, acl);
            _vigilaSalida.Tick += (_, _) =>
            {
                if (_pedirSalida is null || !_pedirSalida.WaitOne(0)) return;
                Log("salida pedida desde fuera");
                _vigilaSalida.Stop();
                Salir();
            };
            _vigilaSalida.Start();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException or PlatformNotSupportedException)
        {
            Log($"no se pudo abrir la señal de salida: {e.Message}");
        }
    }

    public Bandeja()
    {
        Log($"arranque: lanzador {Version}, modo {_modo}");
        foreach (var n in Enum.GetValues<Nivel>()) _iconos[n] = Dibujar(n);
        _icono.Icon = _iconos[Nivel.Ambar];
        _icono.Text = "Zona privada: arrancando…";
        _icono.ContextMenuStrip = Menu();
        _icono.DoubleClick += (_, _) => Abrir(!Nativo && _urlPublica.Length > 0 ? _urlPublica : UrlLocal);
        _reloj.Tick += async (_, _) => await Comprobar();

        Log("arranque: configuración");
        if (!Configurar()) { Log("arranque: configuración cancelada"); ExitThread(); return; }
        _modoNativo.Checked = Nativo; _ensayoItem.Checked = _ensayo; _tsServe.Checked = _urlNativa.Length > 0;
        // la consulta a schtasks va fuera del hilo de la interfaz: esperarla aquí con .Result podía dejar el menú sin responder
        _ = ActualizarInicio();
        Log("arranque: agente y servicios");
        _ = ArrancarAgente();
        _ = ArrancarTodo(primeraVez: true);
        _reloj.Start();
        EscucharPeticionDeSalida();
        Log("arranque: listo");
    }

    private async Task ActualizarInicio()
    {
        var existe = await Task.Run(Instalador.TareaExiste);
        _ui.Post(_ => _inicio.Checked = existe, null);
    }

    // ---------- configuración: carpeta del proyecto, token y dirección pública ----------

    private bool Configurar()
    {
        Directory.CreateDirectory(Datos);
        var guardado = Path.Combine(Datos, "config.json");
        string? carpeta = null;
        try
        {
            if (File.Exists(guardado))
            {
                var raiz = JsonDocument.Parse(File.ReadAllText(guardado)).RootElement;
                carpeta = raiz.GetProperty("carpeta").GetString();
                if (raiz.TryGetProperty("perfil", out var p) && p.GetString() is { } perfil) _perfil = perfil;
                if (raiz.TryGetProperty("modo", out var mo) && mo.GetString() is "nativo" or "docker") _modo = mo.GetString()!;
                if (raiz.TryGetProperty("ensayo", out var en) && en.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False) _ensayo = en.GetBoolean();
                if (raiz.TryGetProperty("instalacion", out var ins) && ins.GetString() is { } instalacion) _instalacion = instalacion;
                if (raiz.TryGetProperty("urlNativa", out var un) && un.GetString() is { } urlNativa) _urlNativa = urlNativa;
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or IOException) { }
        if (carpeta is null || !File.Exists(Path.Combine(carpeta, "compose.yaml")))
            carpeta = Entorno.BuscarCarpeta([AppContext.BaseDirectory, Environment.CurrentDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Proyectos", "zona-privada")]);
        if (carpeta is null && !Nativo)
        {
            using var d = new FolderBrowserDialog { Description = "Elige la carpeta de la zona privada (la que tiene compose.yaml)", UseDescriptionForTitle = true };
            if (d.ShowDialog() != DialogResult.OK || !File.Exists(Path.Combine(d.SelectedPath, "compose.yaml"))) return false;
            carpeta = d.SelectedPath;
        }
        _carpeta = carpeta ?? "";
        GuardarConfig();
        if (carpeta is not null)
        {
            // primer arranque: si falta algo en el .env se completa (o se piden el correo y la contraseña)
            if (!Nativo && !Asistente.Completar(carpeta, Log)) return false;
            var env = File.Exists(Path.Combine(carpeta, ".env")) ? Entorno.LeerEnv(File.ReadAllLines(Path.Combine(carpeta, ".env"))) : [];
            _urlPublica = env.GetValueOrDefault("URL_PUBLICA", "").TrimEnd('/');
            _token = env.GetValueOrDefault("AGENTE_TOKEN", "");
        }
        return !Nativo || PrepararNativo();
    }

    // la configuración (.env) de la versión de escritorio vive en su carpeta de datos; la primera vez se ofrece copiar la de Docker (mismas claves y administrador)
    private bool PrepararNativo()
    {
        var r = Rutas;
        Directory.CreateDirectory(r.Config);
        var destino = Path.Combine(r.Config, ".env");
        if (!File.Exists(destino) && _carpeta.Length > 0 && File.Exists(Path.Combine(_carpeta, ".env")) &&
            MessageBox.Show("Primera vez con la versión de escritorio.\n\n¿Copio la configuración de Docker (mismas claves y mismo administrador)?\nSi dices que no, se crea una nueva desde cero.",
                "Zona privada", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            File.Copy(Path.Combine(_carpeta, ".env"), destino);
        if (!Asistente.Completar(r.Config, Log)) return false;
        _envNativo = Entorno.LeerEnv(File.ReadAllLines(destino));
        if (_urlNativa.Length > 0) _envNativo["NATIVO_URL_PUBLICA"] = _urlNativa;
        return true;
    }
    private string _token = "";

    private void GuardarConfig()
    {
        try { File.WriteAllText(Path.Combine(Datos, "config.json"), JsonSerializer.Serialize(new { carpeta = _carpeta, perfil = _perfil, modo = _modo, ensayo = _ensayo, instalacion = _instalacion, urlNativa = _urlNativa })); } catch (IOException) { }
    }

    // ---------- agente del PC (el mismo bucle que mda-agente.exe) ----------

    private Task ArrancarAgente()
    {
        // con la versión de escritorio en producción (sin ensayo) el agente habla con ella; si no, con Docker
        var token = Nativo && !_ensayo ? _envNativo.GetValueOrDefault("AGENTE_TOKEN", "") : _token;
        var url = ConfigNativa.UrlAgente(Nativo, _ensayo);
        if (token.Length == 0) { _estadoAgente = "falta AGENTE_TOKEN en el .env"; return Task.CompletedTask; }
        var ct = _agente.Token;
        return Task.Run(async () =>
        {
            try { await AgenteServicio.Ejecutar(url, token, e => { _estadoAgente = e; Log($"agente: {e}"); }, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { _estadoAgente = "error: " + e.Message; Log("agente cayó: " + e); }
        });
    }

    private void ReiniciarAgente()
    {
        _agente.Cancel();
        _agente = new CancellationTokenSource();
        _estadoAgente = "arrancando";
        _ = ArrancarAgente();
    }

    // ---------- versión de escritorio (sin Docker) ----------

    private async Task ArrancarNativo(bool primeraVez)
    {
        if (_ocupado) return;
        _ocupado = true; _ultimoArranque = DateTime.UtcNow;
        try
        {
            _nativo ??= new ServidorNativo(Rutas, Log);
            Estado("Arrancando la versión de escritorio…", Nivel.Ambar);
            var (ok, mensaje) = await _nativo.Arrancar(_envNativo, _ensayo, CancellationToken.None);
            Log($"escritorio: {(ok ? "en marcha" : mensaje)}");
            if (!ok) { Estado(mensaje, Nivel.Rojo); Aviso(mensaje); return; }
            if (primeraVez) Aviso(_ensayo ? "Versión de escritorio en marcha (ensayo): Docker sigue de producción." : "Zona privada en marcha.");
        }
        finally { _ocupado = false; }
        await Comprobar();
    }

    private async Task ComprobarNativo()
    {
        if (_ocupado) return;
        var pg = _nativo?.PostgresVivo ?? false;
        var api = _nativo is not null && await _nativo.ApiResponde();
        var (nivel, texto) = EstadosNativos.Resumir(new ComprobacionesNativas(pg, api, _estadoAgente, _ensayo));
        Estado(texto, nivel);
        // los procesos ya se reinician solos; esto solo vuelve a arrancar el conjunto si no queda nada, con 5 minutos de margen
        if (nivel == Nivel.Rojo && DateTime.UtcNow - _ultimoArranque > TimeSpan.FromMinutes(5)) _ = ArrancarNativo(false);
    }

    // Cambiar de modo NUNCA para Docker: solo se enciende o se apaga la versión de escritorio
    private async Task CambiarModo(bool nativo)
    {
        if (_ocupado || nativo == Nativo) return;
        var anterior = _modo;
        _modo = nativo ? "nativo" : "docker";
        if (nativo && !PrepararNativo()) { _modo = anterior; return; }
        GuardarConfig();
        _modoNativo.Checked = nativo;
        if (!nativo && _nativo is not null) { await _nativo.Parar(); _nativo = null; }
        ReiniciarAgente();
        Aviso(nativo ? "Modo escritorio: arranco PostgreSQL y la API sin Docker (Docker no se toca)." : "Modo Docker. La versión de escritorio se ha parado; Docker no se ha tocado.");
        await ArrancarTodo();
    }

    private async Task ComprobarSeguridad()
    {
        var r = await SeguridadNativa.Comprobar(Rutas);
        var texto = SeguridadNativa.Texto(r);
        Log("Comprobación de seguridad:" + Environment.NewLine + texto);
        MessageBox.Show(texto, r.All(x => x.Ok) ? "Seguridad: todo bien" : "Seguridad: hay algo que revisar", MessageBoxButtons.OK, r.All(x => x.Ok) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    // Tailscale de Windows: «serve» enseña la versión de escritorio solo a tus dispositivos; «funnel» la abre a Internet (solo sin ensayo y con tu confirmación)
    private async Task CompartirTailscale()
    {
        if (!Nativo) { Aviso("Esto es de la versión de escritorio: cambia primero al modo escritorio."); return; }
        if (_tsServe.Checked)
        {
            if (_tsFunnel.Checked) _ = await TailscaleNativa.Ejecutar(TailscaleOrdenes.CerrarAInternet());
            _tsFunnel.Checked = false;
            var (okP, textoP) = await TailscaleNativa.Ejecutar(TailscaleOrdenes.PararServir());
            _tsServe.Checked = !okP; if (!okP) { Aviso("No se pudo dejar de compartir: " + textoP); return; }
            _urlNativa = ""; _envNativo.Remove("NATIVO_URL_PUBLICA"); GuardarConfig();
            await ReiniciarNativo("Ya no se comparte por Tailscale.");
            return;
        }
        var estado = await TailscaleNativa.Estado();
        if (estado is null) { Aviso("Tailscale no está instalado en Windows. Instálalo desde tailscale.com/download e inicia sesión."); return; }
        if (!estado.Conectado || estado.Url is null) { Aviso("Tailscale está instalado pero no conectado. Abre Tailscale e inicia sesión."); return; }
        var (ok, texto) = await TailscaleNativa.Ejecutar(TailscaleOrdenes.Servir(ConfigNativa.PuertoApi));
        if (!ok) { Aviso("No se pudo compartir con Tailscale: " + texto); return; }
        _tsServe.Checked = true; _urlNativa = estado.Url; _envNativo["NATIVO_URL_PUBLICA"] = _urlNativa; GuardarConfig();
        await ReiniciarNativo($"Compartida con tus dispositivos en {_urlNativa}");
    }

    private async Task FunnelNativo()
    {
        if (!Nativo || !_tsServe.Checked) { Aviso("Primero comparte la versión de escritorio con Tailscale (modo escritorio)."); return; }
        if (_tsFunnel.Checked)
        {
            var (okC, textoC) = await TailscaleNativa.Ejecutar(TailscaleOrdenes.CerrarAInternet());
            _tsFunnel.Checked = !okC; Aviso(okC ? "Versión de escritorio cerrada a Internet." : "No se pudo cerrar: " + textoC);
            return;
        }
        if (_ensayo) { Aviso("La versión de escritorio está en ensayo: Docker sigue siendo la producción. Sal del ensayo antes de abrirla a Internet."); return; }
        if (MessageBox.Show("Esto abre la versión de escritorio a TODO Internet con HTTPS (Tailscale Funnel), igual que hoy Docker.\n\n¿Seguro?", "Zona privada", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var (ok, texto) = await TailscaleNativa.Ejecutar(TailscaleOrdenes.AbrirAInternet(ConfigNativa.PuertoApi));
        _tsFunnel.Checked = ok; Aviso(ok ? $"Abierta a Internet en {_urlNativa}" : "No se pudo abrir: " + texto);
    }

    private async Task ReiniciarNativo(string aviso)
    {
        Aviso(aviso);
        if (_nativo is not null) { await _nativo.Parar(); _nativo = null; }
        await ArrancarTodo();
    }

    // Trae la base de datos y las claves de Docker a la versión de escritorio. Docker solo se lee.
    private async Task ImportarDeDocker()
    {
        if (!Nativo) { Aviso("Esto es de la versión de escritorio: cambia primero al modo escritorio."); return; }
        if (_carpeta.Length == 0 || !File.Exists(Path.Combine(_carpeta, ".env"))) { Aviso("No encuentro la carpeta de Docker (compose.yaml y .env) de la que importar."); return; }
        if (_ocupado) return;
        if (MessageBox.Show("Voy a COPIAR la base de datos y las claves de Docker a la versión de escritorio.\n\n· Docker solo se lee: no se para ni se modifica.\n· Lo que haya ahora en la versión de escritorio se sustituye (antes se guarda una copia).\n\n¿Sigo?",
            "Importar datos de Docker", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _ocupado = true;
        string mensaje; bool ok;
        try
        {
            _nativo ??= new ServidorNativo(Rutas, Log);
            Estado("Importando datos de Docker…", Nivel.Ambar);
            var envDocker = Entorno.LeerEnv(File.ReadAllLines(Path.Combine(_carpeta, ".env")));
            (ok, mensaje) = await ImportarDockerNativo.Importar(Rutas, _carpeta, envDocker, _envNativo, _nativo, Log);
        }
        finally { _ocupado = false; }
        Log("importar: " + mensaje);
        MessageBox.Show(mensaje, ok ? "Importado" : "No se pudo importar", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        await ArrancarTodo();
    }

    private async Task CambiarEnsayo()
    {
        if (_ocupado) return;
        if (_ensayo && MessageBox.Show("Salir del ensayo hace que la versión de escritorio mande avisos, hable por Telegram y mande órdenes a tus aparatos, y que el agente del PC la use.\n\nSi Docker sigue encendido, se duplicarán.\n\n¿Seguro?",
            "Zona privada", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _ensayo = !_ensayo; _ensayoItem.Checked = _ensayo; GuardarConfig();
        ReiniciarAgente();
        if (Nativo && _nativo is not null) { await _nativo.Parar(); _nativo = null; await ArrancarTodo(); }
    }

    // ---------- Docker y contenedores ----------

    private async Task ArrancarTodo(bool primeraVez = false)
    {
        if (Nativo) { await ArrancarNativo(primeraVez); return; }
        if (_ocupado) return;
        _ocupado = true; _ultimoArranque = DateTime.UtcNow;
        try
        {
            if (!Docker.Instalado && !await InstalarDocker()) { Estado("Falta Docker Desktop", Nivel.Rojo); return; }
            Estado("Arrancando Docker…", Nivel.Ambar);
            if (!await Docker.Responde())
            {
                Docker.AbrirDockerDesktop();
                var limite = DateTime.UtcNow.AddMinutes(4);
                while (DateTime.UtcNow < limite && !await Docker.Responde()) await Task.Delay(3000);
                if (!await Docker.Responde()) { Estado("Docker no arranca", Nivel.Rojo); Aviso("Docker no ha arrancado en 4 minutos. Ábrelo a mano."); return; }
            }
            Estado("Arrancando la zona privada…", Nivel.Ambar);
            var (codigo, salida) = await Docker.Cmd($"compose {Opc}up -d", _carpeta, 300);
            Log($"up -d → {codigo}: {salida.Trim()}");
            if (codigo != 0) Aviso("No se pudieron arrancar los contenedores. Mira el registro del lanzador.");
            else
            {
                // «up -d» vuelve en cuanto crea los contenedores: se espera a que la API conteste de verdad (la primera vez aplica las migraciones)
                Estado("Esperando a la API…", Nivel.Ambar);
                var limiteApi = DateTime.UtcNow.AddMinutes(3);
                while (DateTime.UtcNow < limiteApi && !await Responde("http://localhost:8080/api/salud")) await Task.Delay(3000);
                if (!await Responde("http://localhost:8080/api/salud")) Aviso("La API no contesta tras 3 minutos. Mira «Exportar diagnóstico» o el monitor.");
                else if (primeraVez) Aviso("Zona privada en marcha.");
            }
        }
        finally { _ocupado = false; }
        await Comprobar();
    }

    // Docker Desktop no está: se ofrece instalarlo con winget (una sola vez)
    private async Task<bool> InstalarDocker()
    {
        var r = MessageBox.Show("Falta Docker Desktop, que es lo que ejecuta la zona privada.\n\n¿Lo instalo ahora con winget? Tarda unos minutos y puede pedirte reiniciar el PC.",
            "Zona privada", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r != DialogResult.Yes) return false;
        Estado("Instalando Docker Desktop…", Nivel.Ambar);
        var (ok, salida) = await Docker.InstalarConWinget();
        Log($"winget Docker Desktop → {(ok ? "ok" : "falló")}: {salida.Trim()}");
        if (!ok) { Aviso("No se pudo instalar Docker Desktop. Instálalo a mano desde docker.com."); return false; }
        Aviso("Docker Desktop instalado. Si Windows pide reiniciar, hazlo y vuelve a abrir la zona privada.");
        return Docker.Instalado;
    }

    // «Actualizar»: si la carpeta es un clon de git, trae los cambios; luego reconstruye la API (el front va dentro de su imagen)
    private async Task Actualizar()
    {
        if (_ocupado) return;
        if (Nativo) { Aviso("Estás en el modo escritorio: este botón es de Docker. Cambia al modo Docker para usarlo."); return; }
        if (Directory.Exists(Path.Combine(_carpeta, ".git")))
        {
            Estado("Trayendo cambios…", Nivel.Ambar);
            var (c, salida) = await Docker.Ejecutar("git.exe", "pull --ff-only", _carpeta, TimeSpan.FromMinutes(2));
            Log($"git pull → {c}: {salida.Trim()}");
            if (c != 0) { Aviso("No se pudieron traer los cambios (¿hay cambios sin guardar?). Mira el registro."); await Comprobar(); return; }
        }
        await Accion("Reconstruyendo la API", $"compose {Opc}up -d --build api", 900);
    }

    private async Task Accion(string texto, string argumentos, int segundos = 300)
    {
        if (_ocupado) return;
        if (Nativo) { Aviso("Estás en el modo escritorio: este botón es de Docker. Cambia al modo Docker para usarlo."); return; }
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
        if (Nativo) { await ComprobarNativo(); return; }
        if (_ocupado) return;
        bool docker = await Docker.Responde();
        HashSet<string> esperados = [], corriendo = [];
        if (docker)
        {
            esperados = Entorno.Servicios((await Docker.Cmd($"compose {Opc}config --services", _carpeta, 20)).Salida);
            corriendo = Entorno.Servicios((await Docker.Cmd($"compose {Opc}ps --services --filter status=running", _carpeta, 20)).Salida);
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
        m.Items.Add(new ToolStripMenuItem($"Zona privada · lanzador {Version}") { Enabled = false });
        m.Items.Add(_estado);
        m.Items.Add(new ToolStripSeparator());
        Item("Abrir la zona privada", () => Abrir(!Nativo && _urlPublica.Length > 0 ? _urlPublica : UrlLocal));
        Item("Abrir en este PC (localhost)", () => Abrir("http://localhost:8080"));
        Item("Abrir la versión de escritorio (localhost:8090)", () => Abrir(ConfigNativa.UrlApi));
        Item("Monitor del PC y registro…", AbrirMonitor);
        Item("Registros (Seq)", () => Abrir("http://localhost:5341"));
        m.Items.Add(new ToolStripSeparator());
        Item("Reiniciar la API", () => _ = Accion("Reiniciando la API", $"compose {Opc}restart api", 120));
        Item("Actualizar (traer cambios y reconstruir)", () => _ = Actualizar());
        Item("Reconstruir la API sin traer cambios", () => _ = Accion("Reconstruyendo la API", $"compose {Opc}up -d --build api", 900));
        Item("Arrancar todo", () => _ = ArrancarTodo());
        Item("Parar todo", () => _ = Accion("Parando todo", $"compose {Opc}stop", 120));
        m.Items.Add(new ToolStripSeparator());
        _inicio.Click += async (_, _) =>
        {
            var ok = _inicio.Checked ? await Instalador.QuitarTarea() : await Instalador.CrearTarea();
            if (ok) _inicio.Checked = !_inicio.Checked; else Aviso("No se pudo cambiar el inicio con Windows.");
        };
        m.Items.Add(_inicio);
        _internet.CheckOnClick = false; _internet.Checked = _perfil == "publico";
        _internet.Click += (_, _) =>
        {
            _perfil = _perfil == "publico" ? "" : "publico"; _internet.Checked = _perfil == "publico"; GuardarConfig();
            Aviso(_perfil == "publico" ? "Salida a Internet activada: levanto también Tailscale." : "Salida a Internet desactivada: la app solo se verá en este PC.");
            _ = ArrancarTodo();
        };
        m.Items.Add(_internet);
        m.Items.Add(new ToolStripSeparator());
        _modoNativo.Click += async (_, _) => await CambiarModo(!Nativo);
        m.Items.Add(_modoNativo);
        _ensayoItem.Click += async (_, _) => await CambiarEnsayo();
        m.Items.Add(_ensayoItem);
        m.Items.Add(new ToolStripSeparator());
        _tsServe.Click += async (_, _) => await CompartirTailscale();
        m.Items.Add(_tsServe);
        _tsFunnel.Click += async (_, _) => await FunnelNativo();
        m.Items.Add(_tsFunnel);
        Item("Importar datos de Docker (solo lectura)…", () => _ = ImportarDeDocker());
        Item("Comprobar seguridad de la versión de escritorio…", () => _ = ComprobarSeguridad());
        Item("Exportar diagnóstico (zip en el escritorio)", () => _ = ExportarDiagnostico());
        Item("Abrir la carpeta", () => Abrir(_carpeta));
        Item("Abrir el registro del lanzador", () => Abrir(Path.Combine(Datos, "lanzador.log")));
        m.Items.Add(new ToolStripSeparator());
        Item("Salir (deja Docker en marcha)", Salir);
        return m;
    }

    private Monitor? _monitor;
    private void AbrirMonitor()
    {
        if (_monitor is { IsDisposed: false }) { _monitor.Activate(); return; }
        _monitor = new Monitor(Path.Combine(Datos, "lanzador.log"), () => _estadoAgente);
        _monitor.Show();
    }

    private async Task ExportarDiagnostico()
    {
        try
        {
            Estado("Preparando el diagnóstico…", Nivel.Ambar);
            var zip = await Diagnostico.Crear(_carpeta, Datos, _estado.Text ?? "", Version);
            Aviso("Diagnóstico guardado en el escritorio (sin ninguna clave).");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Aviso("No se pudo crear el diagnóstico: " + e.Message); }
        await Comprobar();
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
        _vigilaSalida.Stop(); _pedirSalida?.Dispose(); _pedirSalida = null;
        // la versión de escritorio se para con calma; Docker no se toca
        if (_nativo is { } n) { try { Task.Run(n.Parar).Wait(TimeSpan.FromSeconds(60)); } catch (AggregateException) { } }
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
