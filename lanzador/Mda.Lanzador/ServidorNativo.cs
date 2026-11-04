using System.Diagnostics;

namespace Mda.Lanzador;

// La zona privada sin Docker: arranca PostgreSQL portable y la API como procesos hijos, espera a que contesten y los vigila.
// No toca Docker en ningún momento. Los programas están en «componentes» (solo lectura) y lo que cambia, en la carpeta de datos.
sealed class ServidorNativo
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly RutasNativas _r;
    private readonly Action<string> _log;
    private ProcesoHijo? _pg, _api;
    private bool _ensayo;
    private CredencialServicio? _cred;
    private CancellationTokenSource? _copias;

    public ServidorNativo(RutasNativas rutas, Action<string> log) { _r = rutas; _log = log; }

    public RutasNativas Rutas => _r;
    public bool PostgresVivo => _pg?.EnMarcha ?? false;
    public bool ApiVivo => _api?.EnMarcha ?? false;

    private string Pg(string exe) => Path.Combine(_r.PgBin, exe);

    public string? FaltaAlgo()
    {
        var falta = new[] { _r.ApiExe, Pg("postgres.exe"), Pg("initdb.exe"), Pg("pg_isready.exe"), Pg("psql.exe") }.Where(f => !File.Exists(f)).ToList();
        return falta.Count == 0 ? null : "Faltan componentes de la versión de escritorio: " + string.Join(", ", falta.Select(Path.GetFileName)) + ". Se instalan con el empaquetado (docs/09-escritorio.md).";
    }

    // Solo la base de datos (seguridad, PostgreSQL y la base «zonaprivada»), sin la API: para importar datos o repararla
    public async Task<(bool Ok, string Mensaje)> ArrancarBase(IReadOnlyDictionary<string, string> env, CancellationToken ct)
    {
        if (FaltaAlgo() is { } falta) return (false, falta);
        foreach (var d in _r.DirectoriosDeDatos) Directory.CreateDirectory(d);
        var clave = env.GetValueOrDefault("POSTGRES_PASSWORD", "");
        if (clave.Length < 16) return (false, "Falta POSTGRES_PASSWORD en la configuración de la versión de escritorio.");

        // cuenta sin privilegios, permisos mínimos y cortafuegos: sin esto no se arranca nada
        var (cred, errorSeguridad) = await SeguridadNativa.Preparar(_r, env, _log);
        if (cred is null) return (false, errorSeguridad ?? "No se pudo preparar la seguridad.");
        _cred = cred;

        if (!File.Exists(Path.Combine(_r.PgDatos, "PG_VERSION")) && !await IniciarBase(clave, cred)) return (false, "No se pudo crear la base de datos (mira el registro).");

        if (_pg is not { } pg0 || !pg0.EnMarcha)
        {
            _pg?.Dispose();
            _pg = new ProcesoHijo("postgres", () => new ProcessStartInfo(Pg("postgres.exe"), $"-D \"{_r.PgDatos}\"") { WorkingDirectory = _r.PgBin }, _log, credencial: cred);
            _pg.Iniciar();
        }
        if (!await EsperarPostgres(TimeSpan.FromSeconds(60), ct)) return (false, "PostgreSQL no arranca en 60 segundos.");
        if (!await CrearBaseSiFalta(clave)) return (false, "No se pudo crear la base «zonaprivada».");
        return (true, "");
    }

    // true si todo queda en marcha; si no, el texto de lo que falló
    public async Task<(bool Ok, string Mensaje)> Arrancar(IReadOnlyDictionary<string, string> env, bool ensayo, CancellationToken ct)
    {
        _ensayo = ensayo;
        var (okBase, mensajeBase) = await ArrancarBase(env, ct);
        if (!okBase) return (false, mensajeBase);
        var clave = env.GetValueOrDefault("POSTGRES_PASSWORD", "");
        var cred = _cred!;

        if (_api is not { } api0 || !api0.EnMarcha)
        {
            _api?.Dispose();
            var entorno = ConfigNativa.EntornoApi(env, _r, clave, ensayo);
            _api = new ProcesoHijo("api", () =>
            {
                var psi = new ProcessStartInfo(_r.ApiExe) { WorkingDirectory = _r.ApiDir };
                foreach (var (k, v) in entorno) psi.Environment[k] = v;
                return psi;
            }, _log, memoriaMaxima: 1L << 30, credencial: cred);
            _api.Iniciar();
        }
        if (!await EsperarApi(TimeSpan.FromMinutes(2), ct)) return (false, "La API no contesta en 2 minutos. Mira los registros en " + _r.Registros);

        // copias diarias, copia cifrada y simulacro semanal (lo que hacía el contenedor «copias»)
        if (_copias is null)
        {
            _copias = new CancellationTokenSource();
            var copias = new CopiasNativas(_r, clave, env.GetValueOrDefault("COPIAS_AGE_PUBLICA", ""), env.GetValueOrDefault("COPIAS_NUBE", ""), _log);
            _ = Task.Run(() => copias.Bucle(_copias.Token));
        }
        return (true, "En marcha");
    }

    // initdb con contraseña por fichero temporal (no por línea de órdenes, que se ve en la lista de procesos)
    private async Task<bool> IniciarBase(string clave, CredencialServicio cred)
    {
        var pwfile = Path.Combine(_r.Temporal, "pg.pwd");
        try
        {
            await File.WriteAllTextAsync(pwfile, clave);
            var (codigo, salida) = await Proc.Ejecutar(Pg("initdb.exe"), $"-D \"{_r.PgDatos}\" -U mda -E UTF8 --locale=C --auth=scram-sha-256 --pwfile=\"{pwfile}\"", _r.PgBin, TimeSpan.FromMinutes(2), cred);
            _log($"initdb → {codigo}: {Recorte(salida)}");
            if (codigo != 0) return false;
            await File.WriteAllTextAsync(Path.Combine(_r.PgDatos, "postgresql.conf"), ConfigNativa.PostgresConf());
            await File.WriteAllTextAsync(Path.Combine(_r.PgDatos, "pg_hba.conf"), ConfigNativa.PgHba());
            return true;
        }
        finally { try { File.Delete(pwfile); } catch (IOException) { } }
    }

    private async Task<bool> EsperarPostgres(TimeSpan limite, CancellationToken ct)
    {
        var hasta = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < hasta && !ct.IsCancellationRequested)
        {
            var (c, _) = await Docker.Ejecutar(Pg("pg_isready.exe"), $"-h 127.0.0.1 -p {ConfigNativa.PuertoPostgres}", _r.PgBin, TimeSpan.FromSeconds(10));
            if (c == 0) return true;
            await Task.Delay(1000, ct);
        }
        return false;
    }

    private async Task<bool> CrearBaseSiFalta(string clave)
    {
        var args = $"-h 127.0.0.1 -p {ConfigNativa.PuertoPostgres} -U mda -d postgres -tAc \"SELECT 1 FROM pg_database WHERE datname='zonaprivada'\"";
        var (c, salida) = await Sql(args, clave);
        if (c != 0) return false;
        if (salida.Trim() == "1") return true;
        var (c2, s2) = await Sql($"-h 127.0.0.1 -p {ConfigNativa.PuertoPostgres} -U mda -d postgres -c \"CREATE DATABASE zonaprivada ENCODING 'UTF8' TEMPLATE template0 LC_COLLATE 'C' LC_CTYPE 'C'\"", clave);
        _log($"CREATE DATABASE → {c2}: {Recorte(s2)}");
        return c2 == 0;
    }

    // psql con la contraseña por variable de entorno solo de ese proceso
    private async Task<(int, string)> Sql(string args, string clave)
    {
        var psi = new ProcessStartInfo(Pg("psql.exe"), args) { WorkingDirectory = _r.PgBin, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment["PGPASSWORD"] = clave;
        try
        {
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30_000)) { p.Kill(true); return (-1, "tiempo agotado"); }
            return (p.ExitCode, await o + await e);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return (-1, e.Message); }
    }

    private async Task<bool> EsperarApi(TimeSpan limite, CancellationToken ct)
    {
        var hasta = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < hasta && !ct.IsCancellationRequested)
        {
            if (await ApiResponde()) return true;
            await Task.Delay(2000, ct);
        }
        return false;
    }

    public async Task<bool> ApiResponde()
    {
        try { using var r = await Http.GetAsync(ConfigNativa.UrlApi + "/api/salud"); return r.IsSuccessStatusCode; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return false; }
    }

    public async Task PararApi()
    {
        if (_api is not null) { await _api.Parar(TimeSpan.FromSeconds(10)); _api = null; }
    }

    // La API primero y PostgreSQL con calma («pg_ctl stop -m fast»); si no se va, se mata. No toca Docker.
    public async Task Parar()
    {
        _copias?.Cancel(); _copias = null;
        if (_api is not null) { await _api.Parar(TimeSpan.FromSeconds(10)); _api = null; }
        if (_pg is not null)
        {
            _pg.NoReiniciar();
            var (c, s) = await Proc.Ejecutar(Pg("pg_ctl.exe"), $"stop -D \"{_r.PgDatos}\" -m fast -w -t 30", _r.PgBin, TimeSpan.FromSeconds(45), _cred);
            _log($"pg_ctl stop → {c}: {Recorte(s)}");
            await _pg.Parar(TimeSpan.FromSeconds(10));
            _pg = null;
        }
    }

    public bool Ensayo => _ensayo;
    private static string Recorte(string s) { s = s.Trim(); return s.Length <= 400 ? s : s[..400] + "…"; }
}
