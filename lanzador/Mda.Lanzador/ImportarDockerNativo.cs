using System.Diagnostics;
using System.Globalization;

namespace Mda.Lanzador;

// «Importar datos de Docker»: copia la base de datos y las claves de cifrado de la API de Docker a la versión de escritorio.
// Docker solo se LEE (pg_dump dentro del contenedor y docker cp de las claves): no se para, no se reinicia y no se escribe nada en él.
// Lo que había en la base de escritorio se guarda antes en una copia, por si acaso.
static class ImportarDockerNativo
{
    public static async Task<(bool Ok, string Mensaje)> Importar(RutasNativas r, string carpetaDocker, IReadOnlyDictionary<string, string> envDocker,
        IReadOnlyDictionary<string, string> envNativo, ServidorNativo servidor, Action<string> log)
    {
        var problemas = ImportarDocker.Problemas(envDocker, envNativo);
        if (problemas.Count > 0) return (false, string.Join("\n", problemas));
        if (!await Docker.Responde()) return (false, "Docker no responde: arráncalo para poder leer sus datos (no se modificará).");

        var clave = envNativo.GetValueOrDefault("POSTGRES_PASSWORD", "");
        var tmp = Path.Combine(r.Temporal, "importar");
        Directory.CreateDirectory(tmp);
        var dump = Path.Combine(tmp, "docker.dump");
        try
        {
            // 1) lo de Docker, solo lectura
            var (cd, errorDump) = await VolcarAArchivo(Docker.Exe, ImportarDocker.VolcarDocker, carpetaDocker, dump, TimeSpan.FromMinutes(10));
            if (cd != 0 || !File.Exists(dump) || new FileInfo(dump).Length < 1024) return (false, $"No se pudo leer la base de Docker ({cd}): {Recorte(errorDump)}");
            var (cu, su) = await Docker.Ejecutar(Docker.Exe, ImportarDocker.Contar(ImportarDocker.SqlUsuarios), carpetaDocker, TimeSpan.FromSeconds(30));
            var (ct, st) = await Docker.Ejecutar(Docker.Exe, ImportarDocker.Contar(ImportarDocker.SqlTablas), carpetaDocker, TimeSpan.FromSeconds(30));
            if (cu != 0 || ct != 0) return (false, "No se pudieron contar los usuarios y tablas de Docker.");
            int usuariosD = Entero(su), tablasD = Entero(st);

            var (ci, id) = await Docker.Ejecutar(Docker.Exe, ImportarDocker.IdApiDocker, carpetaDocker, TimeSpan.FromSeconds(30));
            var claves = Path.Combine(tmp, "claves");
            Directory.CreateDirectory(claves);
            if (ci == 0 && id.Trim().Length > 0)
            {
                var (cc, sc) = await Docker.Ejecutar(Docker.Exe, ImportarDocker.CopiarClaves(id.Trim().Split('\n')[0].Trim(), claves), carpetaDocker, TimeSpan.FromMinutes(1));
                if (cc != 0) return (false, "No se pudieron leer las claves de la API de Docker: " + Recorte(sc));
            }

            // 2) la base de escritorio se prepara sin la API encendida
            await servidor.PararApi();
            var (okBase, msgBase) = await servidor.ArrancarBase(envNativo, CancellationToken.None);
            if (!okBase) return (false, msgBase);
            var pg = r.PgBin;
            var entorno = new Dictionary<string, string> { ["PGPASSWORD"] = clave };
            var con = $"-h 127.0.0.1 -p {ConfigNativa.PuertoPostgres} -U mda";

            // lo que hubiera: copia previa (si hay algo)
            var (cx, sx) = await Proc.Ejecutar(Path.Combine(pg, "psql.exe"), $"{con} -d zonaprivada -tAc \"select count(*) from information_schema.tables where table_schema='public'\"", pg, TimeSpan.FromMinutes(1), null, entorno);
            if (cx == 0 && Entero(sx) > 0)
            {
                Directory.CreateDirectory(r.Copias);
                var antes = Path.Combine(r.Copias, $"antes-de-importar-{DateTime.Now:yyyyMMdd-HHmmss}.dump");
                var (cb, sb) = await Proc.Ejecutar(Path.Combine(pg, "pg_dump.exe"), $"{con} -d zonaprivada -Fc -f \"{antes}\"", pg, TimeSpan.FromMinutes(10), null, entorno);
                if (cb != 0) return (false, "No se pudo guardar una copia de lo que hay en la versión de escritorio: " + Recorte(sb));
                log($"importar: copia previa {Path.GetFileName(antes)}");
            }

            // 3) restaurar
            var (c1, s1) = await Proc.Ejecutar(Path.Combine(pg, "psql.exe"), $"{con} -d postgres -qc \"DROP DATABASE IF EXISTS zonaprivada WITH (FORCE)\" -c \"CREATE DATABASE zonaprivada TEMPLATE template0 ENCODING 'UTF8' LC_COLLATE 'C' LC_CTYPE 'C'\"", pg, TimeSpan.FromMinutes(1), null, entorno);
            if (c1 != 0) return (false, "No se pudo preparar la base de escritorio: " + Recorte(s1));
            var (c2, s2) = await Proc.Ejecutar(Path.Combine(pg, "pg_restore.exe"), $"{con} -d zonaprivada --no-owner --no-acl \"{dump}\"", pg, TimeSpan.FromMinutes(20), null, entorno);
            if (c2 != 0) log($"importar: pg_restore avisó ({c2}): {Recorte(s2)}");   // suele avisar de cosas sin importancia: lo que vale es la comprobación
            var (c3, s3) = await Proc.Ejecutar(Path.Combine(pg, "psql.exe"), $"{con} -d zonaprivada -tAc \"{ImportarDocker.SqlUsuarios}\"", pg, TimeSpan.FromMinutes(1), null, entorno);
            var (c4, s4) = await Proc.Ejecutar(Path.Combine(pg, "psql.exe"), $"{con} -d zonaprivada -tAc \"{ImportarDocker.SqlTablas}\"", pg, TimeSpan.FromMinutes(1), null, entorno);
            int usuariosN = c3 == 0 ? Entero(s3) : -1, tablasN = c4 == 0 ? Entero(s4) : -1;
            if (!ImportarDocker.Coincide(usuariosD, tablasD, usuariosN, tablasN))
                return (false, $"La comprobación no cuadra: Docker tiene {usuariosD} usuario(s) y {tablasD} tablas; la versión de escritorio, {usuariosN} y {tablasN}. Docker no se ha tocado.");

            // 4) las claves de cifrado de la API (para que las cookies y tokens sigan valiendo)
            if (Directory.GetFiles(claves).Length > 0)
            {
                Directory.CreateDirectory(r.Claves);
                foreach (var f in Directory.GetFiles(claves)) File.Copy(f, Path.Combine(r.Claves, Path.GetFileName(f)), true);
            }
            log($"importar: hecho ({usuariosN} usuarios, {tablasN} tablas)");
            return (true, $"Importado: {usuariosN} usuario(s) y {tablasN} tablas, iguales a las de Docker. Docker no se ha tocado.");
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    // «docker compose exec -T db pg_dump …» devuelve el volcado binario por la salida estándar: se copia tal cual a un archivo
    private static async Task<(int, string)> VolcarAArchivo(string exe, string args, string carpeta, string destino, TimeSpan limite)
    {
        var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = carpeta, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        try
        {
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(limite);
            await using (var f = File.Create(destino)) await p.StandardOutput.BaseStream.CopyToAsync(f, cts.Token);
            await p.WaitForExitAsync(cts.Token);
            return (p.ExitCode, await err);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException or IOException) { return (-1, e.Message); }
    }

    private static int Entero(string s) => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;
    private static string Recorte(string s) { s = s.Trim(); return s.Length <= 300 ? s : s[..300] + "…"; }
}
