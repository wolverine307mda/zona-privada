using System.Formats.Tar;
using System.Globalization;

namespace Mda.Lanzador;

// Lo que hacía el contenedor «copias», pero con los programas de PostgreSQL y age de «componentes»: dump diario, copia cifrada fuera del PC
// y simulacro semanal de restauración. Mira cada 10 minutos. Nunca borra nada que no sea una copia vieja con su nombre de siempre.
sealed class CopiasNativas
{
    private readonly RutasNativas _r;
    private readonly string _clave;           // contraseña de PostgreSQL (solo se pasa al proceso por variable de entorno)
    private readonly string _agePublica;      // clave pública de age (vacía = solo copia local)
    private readonly string _nube;            // carpeta de la nube (OneDrive…) a la que se suben las copias cifradas
    private readonly Action<string> _log;

    public CopiasNativas(RutasNativas r, string clavePostgres, string agePublica, string nube, Action<string> log)
    {
        _r = r; _clave = clavePostgres; _agePublica = agePublica; _nube = nube; _log = log;
    }

    private string Pg(string exe) => Path.Combine(_r.PgBin, exe);
    private Dictionary<string, string> Entorno => new() { ["PGPASSWORD"] = _clave };
    private string Conexion => $"-h 127.0.0.1 -p {ConfigNativa.PuertoPostgres} -U mda";

    public async Task Bucle(CancellationToken ct)
    {
        if (_agePublica.Length == 0) _log("copias: COPIAS_AGE_PUBLICA vacía, solo copia local (sin copia cifrada fuera del PC)");
        else if (!File.Exists(_r.AgeExe) || _nube.Length == 0 || !Directory.Exists(_nube)) _log("copias: falta age o la carpeta de la nube (COPIAS_NUBE): solo copia local");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Vuelta(DateTime.Now, ct); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { _log("copias: " + e.Message); }
                await Task.Delay(TimeSpan.FromMinutes(10), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task Vuelta(DateTime ahora, CancellationToken ct)
    {
        Directory.CreateDirectory(_r.Copias);
        var existentes = Directory.GetFiles(_r.Copias, $"{Copia.Prefijo}*.dump").Select(Path.GetFileName).OfType<string>().ToList();
        if (Copia.TocaDump(ahora, existentes) && await HacerDump(ahora, ct)) existentes = Directory.GetFiles(_r.Copias, $"{Copia.Prefijo}*.dump").Select(Path.GetFileName).OfType<string>().ToList();
        await Simulacro(ahora, existentes, ct);
    }

    private async Task<bool> HacerDump(DateTime ahora, CancellationToken ct)
    {
        var tmp = Path.Combine(_r.Copias, ".tmp.dump");
        var (c, salida) = await Proc.Ejecutar(Pg("pg_dump.exe"), $"{Conexion} -d zonaprivada -Fc -f \"{tmp}\"", _r.PgBin, TimeSpan.FromMinutes(10), null, Entorno);
        if (c != 0) { _log($"copias: pg_dump falló ({c}): {Recortar(salida)}"); return false; }
        var destino = Path.Combine(_r.Copias, Copia.Nombre(ahora));
        File.Move(tmp, destino, true);
        _log($"copias: copia hecha {Path.GetFileName(destino)}");
        foreach (var viejo in Copia.Sobrantes(Directory.GetFiles(_r.Copias, $"{Copia.Prefijo}*.dump").Select(Path.GetFileName).OfType<string>(), Copia.Locales)) File.Delete(Path.Combine(_r.Copias, viejo));
        await SubirCifrada(destino, ahora, ct);
        return true;
    }

    // el dump y las claves de cifrado de la API, cifrados con age para la clave pública: sin la privada (que guardas fuera del PC) no los lee nadie
    private async Task SubirCifrada(string dump, DateTime ahora, CancellationToken ct)
    {
        if (_agePublica.Length == 0 || !File.Exists(_r.AgeExe) || _nube.Length == 0 || !Directory.Exists(_nube)) return;
        var cifDump = Path.Combine(_nube, Copia.CifradoDump(ahora));
        var cifClaves = Path.Combine(_nube, Copia.CifradoClaves(ahora));
        var tar = Path.Combine(_r.Temporal, "claves.tar");
        try
        {
            if (!await Cifrar(dump, cifDump)) return;
            if (Directory.Exists(_r.Claves))
            {
                File.Delete(tar);
                TarFile.CreateFromDirectory(_r.Claves, tar, includeBaseDirectory: false);
                if (!await Cifrar(tar, cifClaves)) return;
            }
            if (Copia.EsDomingo(ahora))
            {
                var semanal = Path.Combine(_nube, "semanal");
                Directory.CreateDirectory(semanal);
                File.Copy(cifDump, Path.Combine(semanal, Path.GetFileName(cifDump)), true);
                if (File.Exists(cifClaves)) File.Copy(cifClaves, Path.Combine(semanal, Path.GetFileName(cifClaves)), true);
                Limpiar(semanal, $"{Copia.Prefijo}*.dump.age", Copia.Semanales); Limpiar(semanal, $"{Copia.Prefijo}*-claves.tar.age", Copia.Semanales);
            }
            Limpiar(_nube, $"{Copia.Prefijo}*.dump.age", Copia.Diarias); Limpiar(_nube, $"{Copia.Prefijo}*-claves.tar.age", Copia.Diarias);
            _log($"copias: copia cifrada subida {Path.GetFileName(cifDump)}");
        }
        finally { try { File.Delete(tar); } catch (IOException) { } }
    }

    private async Task<bool> Cifrar(string origen, string destino)
    {
        var (c, s) = await Proc.Ejecutar(_r.AgeExe, $"-r {_agePublica} -o \"{destino}\" \"{origen}\"", null, TimeSpan.FromMinutes(5));
        if (c != 0) _log($"copias: fallo al cifrar {Path.GetFileName(origen)}: {Recortar(s)}");
        return c == 0;
    }

    private static void Limpiar(string carpeta, string patron, int guardar)
    {
        foreach (var f in Copia.Sobrantes(Directory.GetFiles(carpeta, patron).Select(Path.GetFileName).OfType<string>(), guardar)) File.Delete(Path.Combine(carpeta, f));
    }

    // restaura la última copia en una base vacía y apunta el resultado donde lo lee la API
    private async Task Simulacro(DateTime ahora, List<string> existentes, CancellationToken ct)
    {
        if (!SimulacroCopia.Toca(ahora, marca => File.Exists(Path.Combine(_r.Copias, marca)))) return;
        var ultimo = existentes.OrderByDescending(n => n, StringComparer.Ordinal).FirstOrDefault();
        if (ultimo is null) return;
        var ruta = Path.Combine(_r.Copias, ultimo);
        await Proc.Ejecutar(Pg("psql.exe"), $"{Conexion} -d postgres -qc \"DROP DATABASE IF EXISTS simulacro\" -c \"CREATE DATABASE simulacro TEMPLATE template0 LC_COLLATE 'C' LC_CTYPE 'C'\"", _r.PgBin, TimeSpan.FromMinutes(1), null, Entorno);
        int usuarios = 0, tablas = 0; var ok = false;
        var (c, s) = await Proc.Ejecutar(Pg("pg_restore.exe"), $"{Conexion} -d simulacro --no-owner \"{ruta}\"", _r.PgBin, TimeSpan.FromMinutes(15), null, Entorno);
        if (c == 0)
        {
            usuarios = await Contar("select count(*) from \"AspNetUsers\"");
            tablas = await Contar("select count(*) from information_schema.tables where table_schema='public'");
            ok = SimulacroCopia.Vale(usuarios, tablas);
        }
        else _log($"copias: pg_restore falló ({c}): {Recortar(s)}");
        await Proc.Ejecutar(Pg("psql.exe"), $"{Conexion} -d postgres -qc \"DROP DATABASE IF EXISTS simulacro\"", _r.PgBin, TimeSpan.FromMinutes(1), null, Entorno);
        File.WriteAllText(Path.Combine(_r.Copias, ".ultimo-simulacro"), SimulacroCopia.Linea(ok, usuarios, tablas, ultimo, DateTime.UtcNow) + Environment.NewLine);
        File.WriteAllText(Path.Combine(_r.Copias, SimulacroCopia.Marca(ahora)), "");
        foreach (var vieja in Directory.GetFiles(_r.Copias, ".simulacro-*").OrderByDescending(f => f, StringComparer.Ordinal).Skip(4)) File.Delete(vieja);
        _log($"copias: simulacro de restauración {(ok ? "ok" : "FALLO")} ({usuarios} usuarios, {tablas} tablas)");
    }

    private async Task<int> Contar(string sql)
    {
        var (c, s) = await Proc.Ejecutar(Pg("psql.exe"), $"{Conexion} -d simulacro -tAc \"{sql}\"", _r.PgBin, TimeSpan.FromMinutes(1), null, Entorno);
        return c == 0 && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private static string Recortar(string s) { s = s.Trim(); return s.Length <= 300 ? s : s[..300] + "…"; }
}
