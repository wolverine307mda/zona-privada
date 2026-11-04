using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Mda.Lanzador;

// El blindaje de la versión de escritorio, sin Windows de por medio: qué permisos se dan, qué reglas de cortafuegos se ponen, cómo se comprueban
// los binarios y cómo se leen las listas de puertos y procesos. Lo que toca Windows de verdad está en SeguridadNativa.cs.

public static class UsuarioServicio
{
    // la API y PostgreSQL corren con esta cuenta local sin privilegios, nunca con la tuya ni como administrador
    public const string Nombre = "zp-servicio";

    // Contraseña larga y aleatoria que cumple la complejidad de Windows (mayúscula, minúscula, cifra y símbolo)
    public static string Contrasena()
    {
        const string may = "ABCDEFGHJKLMNPQRSTUVWXYZ", min = "abcdefghijkmnopqrstuvwxyz", cif = "23456789", sim = "!#$%&*+-=?@^_";
        var todos = may + min + cif + sim;
        var c = new List<char> { Elegir(may), Elegir(min), Elegir(cif), Elegir(sim) };
        while (c.Count < 40) c.Add(Elegir(todos));
        for (var i = c.Count - 1; i > 0; i--) { var j = RandomNumberGenerator.GetInt32(i + 1); (c[i], c[j]) = (c[j], c[i]); }
        return new string(c.ToArray());
    }

    private static char Elegir(string s) => s[RandomNumberGenerator.GetInt32(s.Length)];
}

public static class Permisos
{
    // SIDs conocidos: así funcionan igual en Windows en español que en inglés
    public const string Sistema = "*S-1-5-18", Administradores = "*S-1-5-32-544";

    // Las órdenes de icacls que dejan a la cuenta de servicio con lo mínimo:
    //  · datos: solo ella y los administradores; la configuración (.env, contraseñas) ni siquiera la ve el servicio
    //  · componentes: solo lectura para ella
    //  · tus carpetas compartidas: lectura o modificación según MODO_<carpeta> (ro/rw), sin tocar el resto de permisos de esas carpetas
    public static List<string> Icacls(RutasNativas r, string usuario, IReadOnlyDictionary<string, string> env)
    {
        var l = new List<string>
        {
            $"\"{r.Datos}\" /inheritance:r /grant:r {Sistema}:(OI)(CI)F {Administradores}:(OI)(CI)F",
            $"\"{r.Config}\" /inheritance:r /grant:r {Sistema}:(OI)(CI)F {Administradores}:(OI)(CI)F",
        };
        foreach (var d in new[] { r.PgDatos, r.Claves, r.Derivados, r.Registros, r.Carpetas, r.Temporal })
            l.Add($"\"{d}\" /inheritance:r /grant:r {Sistema}:(OI)(CI)F {Administradores}:(OI)(CI)F {usuario}:(OI)(CI)M");
        l.Add($"\"{r.Copias}\" /inheritance:r /grant:r {Sistema}:(OI)(CI)F {Administradores}:(OI)(CI)F {usuario}:(OI)(CI)R");
        l.Add($"\"{r.Componentes}\" /grant {usuario}:(OI)(CI)RX");
        foreach (var id in ConfigNativa.Raices)
        {
            if (!env.TryGetValue($"CARPETA_{id.ToUpperInvariant()}", out var carpeta) || string.IsNullOrWhiteSpace(carpeta) || !Path.IsPathRooted(carpeta)) continue;
            var modo = env.TryGetValue($"MODO_{id.ToUpperInvariant()}", out var m) && m.Equals("ro", StringComparison.OrdinalIgnoreCase) ? "R" : "M";
            l.Add($"\"{carpeta}\" /grant {usuario}:(OI)(CI){modo}");
        }
        return l;
    }
}

public static class Cortafuegos
{
    public const string Prefijo = "ZonaPrivada-Escritorio";

    // Bloquea la ENTRADA desde otros equipos a los puertos de la versión de escritorio (por si algo se enlazara por error a una dirección distinta
    // del bucle local). El tráfico local no pasa por el cortafuegos, así que no estorba. Se borra la regla y se vuelve a crear: es repetible.
    public static List<string> Comandos(params int[] puertos)
    {
        var lista = string.Join(',', puertos);
        return
        [
            $"advfirewall firewall delete rule name=\"{Prefijo}\"",
            $"advfirewall firewall add rule name=\"{Prefijo}\" dir=in action=block protocol=TCP localport={lista} profile=any remoteip=any",
        ];
    }
}

public record Escucha(string Direccion, int Puerto, int Pid);

public static class Escuchas
{
    // «netstat -ano»: líneas «  TCP    127.0.0.1:8090    0.0.0.0:0    LISTENING    1234»
    public static List<Escucha> Parsear(string netstat)
    {
        var l = new List<Escucha>();
        foreach (var linea in netstat.Split('\n'))
        {
            var m = Regex.Match(linea, @"^\s*TCP\s+(?<dir>\[[^\]]+\]|[\d\.]+):(?<puerto>\d+)\s+\S+\s+LISTENING\s+(?<pid>\d+)\s*$");
            if (m.Success) l.Add(new Escucha(m.Groups["dir"].Value.Trim('[', ']'), int.Parse(m.Groups["puerto"].Value), int.Parse(m.Groups["pid"].Value)));
        }
        return l;
    }

    public static bool EsLocal(string direccion) => direccion is "127.0.0.1" or "::1";

    // Lo que escucha en nuestros puertos y NO es el bucle local: eso es un fallo de seguridad
    public static List<Escucha> Expuestas(IEnumerable<Escucha> todas, params int[] puertos) =>
        todas.Where(e => puertos.Contains(e.Puerto) && !EsLocal(e.Direccion)).ToList();
}

public static class Procesos
{
    // «tasklist /V /FO CSV»: "Nombre","PID","Sesión","Núm.","Mem","Estado","Usuario","Tiempo","Título"; devuelve (imagen, pid, usuario)
    public static List<(string Imagen, int Pid, string Usuario)> Parsear(string csv)
    {
        var l = new List<(string, int, string)>();
        foreach (var linea in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var c = Regex.Matches(linea, "\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();
            if (c.Count >= 7 && int.TryParse(c[1], out var pid)) l.Add((c[0], pid, c[6]));
        }
        return l;
    }

    // ¿alguno de estos procesos corre con una cuenta que no es la de servicio?
    public static List<string> ConOtraCuenta(IEnumerable<(string Imagen, int Pid, string Usuario)> procesos, string usuarioServicio) =>
        procesos.Where(p => !p.Usuario.EndsWith("\\" + usuarioServicio, StringComparison.OrdinalIgnoreCase) && !p.Usuario.Equals(usuarioServicio, StringComparison.OrdinalIgnoreCase))
                .Select(p => $"{p.Imagen} (pid {p.Pid}) corre como {p.Usuario}").ToList();
}

public static class Integridad
{
    public static string Sha256(Stream s) => Convert.ToHexStringLower(SHA256.HashData(s));
    public static string Sha256(string texto) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    public static bool Coincide(string archivo, string esperado)
    {
        if (string.IsNullOrWhiteSpace(esperado) || !File.Exists(archivo)) return false;
        using var f = File.OpenRead(archivo);
        return string.Equals(Sha256(f), esperado.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
