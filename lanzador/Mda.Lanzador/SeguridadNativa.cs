using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Mda.Lanzador;

// La cuenta local sin privilegios con la que corren la API y PostgreSQL
sealed class CredencialServicio
{
    public string Usuario { get; }
    private readonly SecureString _clave = new();

    public CredencialServicio(string usuario, string clave) { Usuario = usuario; foreach (var c in clave) _clave.AppendChar(c); _clave.MakeReadOnly(); }

    // el proceso se lanza como esa cuenta (con su perfil, que PostgreSQL y .NET necesitan para sus carpetas temporales)
    public void Aplicar(ProcessStartInfo psi)
    {
        psi.UserName = Usuario; psi.Domain = Environment.MachineName; psi.Password = _clave; psi.LoadUserProfile = true;
    }
}

// Lanza una orden de un solo disparo (initdb, pg_ctl…) y espera; con credencial, como la cuenta de servicio
static class Proc
{
    public static async Task<(int Codigo, string Salida)> Ejecutar(string exe, string args, string? carpeta, TimeSpan limite, CredencialServicio? cred = null, IReadOnlyDictionary<string, string>? entorno = null)
    {
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = carpeta ?? "" };
        cred?.Aplicar(psi);
        if (entorno is not null) foreach (var (k, v) in entorno) psi.Environment[k] = v;
        try
        {
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(limite);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { p.Kill(true); return (-2, "tiempo agotado"); }
            return (p.ExitCode, await o + await e);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return (-1, e.Message); }
    }
}

// Lo que toca Windows de verdad: crear la cuenta, guardar su contraseña (DPAPI de máquina), dar permisos, poner el cortafuegos y comprobarlo todo.
// El lanzador ya corre como administrador. Si algo de los datos no se puede proteger, NO se arranca: no hay modo «menos seguro».
static class SeguridadNativa
{
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("zona-privada/servicio/v1");

    public static async Task<(CredencialServicio? Cred, string? Error)> Preparar(RutasNativas r, IReadOnlyDictionary<string, string> env, Action<string> log)
    {
        try
        {
            foreach (var d in r.DirectoriosDeDatos) Directory.CreateDirectory(d);
            var archivo = Path.Combine(r.Config, "servicio.dat");
            string clave;
            if (File.Exists(archivo))
                clave = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(archivo), Entropia, DataProtectionScope.LocalMachine));
            else
            {
                clave = UsuarioServicio.Contrasena();
                File.WriteAllBytes(archivo, ProtectedData.Protect(Encoding.UTF8.GetBytes(clave), Entropia, DataProtectionScope.LocalMachine));
            }
            var error = Cuenta.Asegurar(UsuarioServicio.Nombre, clave);
            if (error is not null) return (null, $"No se pudo crear o actualizar la cuenta «{UsuarioServicio.Nombre}»: {error}");
            Cuenta.OcultarDeLaPantallaDeInicio(UsuarioServicio.Nombre);

            // permisos: lo de los datos es obligatorio; una carpeta compartida que no se pueda tocar solo se avisa
            var datos = new HashSet<string>(r.DirectoriosDeDatos.Append(r.Datos).Select(d => $"\"{d}\""));
            foreach (var orden in Permisos.Icacls(r, UsuarioServicio.Nombre, env))
            {
                var (codigo, salida) = await Docker.Ejecutar("icacls.exe", orden, null, TimeSpan.FromSeconds(60));
                if (codigo == 0) continue;
                log($"icacls {orden} → {codigo}: {salida.Trim()}");
                if (datos.Any(d => orden.StartsWith(d, StringComparison.Ordinal)) || orden.StartsWith($"\"{r.Componentes}\"", StringComparison.Ordinal))
                    return (null, "No se pudieron aplicar los permisos de la carpeta de datos (mira el registro del lanzador).");
            }
            foreach (var comando in Cortafuegos.Comandos(ConfigNativa.PuertoApi, ConfigNativa.PuertoPostgres))
            {
                var (c, s) = await Docker.Ejecutar("netsh.exe", comando, null, TimeSpan.FromSeconds(30));
                if (c != 0 && !comando.Contains(" delete ")) log($"netsh {comando} → {c}: {s.Trim()}"); // sin la regla se sigue: el servicio ya escucha solo en el bucle local
            }
            return (new CredencialServicio(UsuarioServicio.Nombre, clave), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or System.ComponentModel.Win32Exception)
        {
            return (null, "Seguridad: " + e.Message);
        }
    }

    // «Comprobar seguridad»: cada línea es (bien, texto). Sin secretos.
    public static async Task<List<(bool Ok, string Texto)>> Comprobar(RutasNativas r)
    {
        var l = new List<(bool, string)>();
        l.Add((Cuenta.Existe(UsuarioServicio.Nombre), $"Cuenta «{UsuarioServicio.Nombre}» sin privilegios"));

        var (_, net) = await Docker.Ejecutar("netstat.exe", "-ano -p TCP", null, TimeSpan.FromSeconds(20));
        var expuestas = Escuchas.Expuestas(Escuchas.Parsear(net), ConfigNativa.PuertoApi, ConfigNativa.PuertoPostgres);
        l.Add((expuestas.Count == 0, expuestas.Count == 0 ? $"Los puertos {ConfigNativa.PuertoApi} y {ConfigNativa.PuertoPostgres} solo escuchan en este equipo" : "ESCUCHAN FUERA del equipo: " + string.Join(", ", expuestas.Select(e => $"{e.Direccion}:{e.Puerto}"))));

        var (_, tareas) = await Docker.Ejecutar("tasklist.exe", "/V /FO CSV /NH", null, TimeSpan.FromSeconds(30));
        var nuestros = Procesos.Parsear(tareas).Where(p => p.Imagen.Equals("Mda.Api.exe", StringComparison.OrdinalIgnoreCase) || p.Imagen.Equals("postgres.exe", StringComparison.OrdinalIgnoreCase)).ToList();
        var mal = Procesos.ConOtraCuenta(nuestros, UsuarioServicio.Nombre);
        l.Add((mal.Count == 0, nuestros.Count == 0 ? "La API y PostgreSQL no están en marcha" : mal.Count == 0 ? "La API y PostgreSQL corren con la cuenta de servicio" : string.Join("; ", mal)));

        var (cf, _) = await Docker.Ejecutar("netsh.exe", $"advfirewall firewall show rule name=\"{Cortafuegos.Prefijo}\"", null, TimeSpan.FromSeconds(20));
        l.Add((cf == 0, cf == 0 ? "Regla de cortafuegos: entrada bloqueada a los puertos de la versión de escritorio" : "Falta la regla de cortafuegos"));

        var (_, acl) = await Docker.Ejecutar("icacls.exe", $"\"{r.Config}\"", null, TimeSpan.FromSeconds(20));
        var cfgOk = Directory.Exists(r.Config) && !acl.Contains(UsuarioServicio.Nombre, StringComparison.OrdinalIgnoreCase);
        l.Add((cfgOk, cfgOk ? "La configuración (claves) no es legible por la cuenta de servicio" : "La cuenta de servicio podría leer la configuración"));
        return l;
    }

    public static string Texto(IEnumerable<(bool Ok, string Texto)> r) => string.Join(Environment.NewLine, r.Select(x => (x.Ok ? "✔ " : "✘ ") + x.Texto));
}

// Cuentas locales de Windows sin pasar la contraseña por la línea de órdenes (netapi32)
static class Cuenta
{
    private const uint UF_SCRIPT = 0x1, UF_PASSWD_CANT_CHANGE = 0x40, UF_DONT_EXPIRE_PASSWD = 0x10000, USER_PRIV_USER = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct USER_INFO_1 { public string Nombre, Clave; public uint Edad, Privilegio; public string? Carpeta, Comentario; public uint Banderas; public string? Script; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct USER_INFO_1003 { public string Clave; }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetUserAdd(string? servidor, uint nivel, ref USER_INFO_1 info, out uint error);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetUserSetInfo(string? servidor, string usuario, uint nivel, ref USER_INFO_1003 info, out uint error);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetUserGetInfo(string? servidor, string usuario, uint nivel, out nint buffer);
    [DllImport("netapi32.dll")] private static extern int NetApiBufferFree(nint buffer);

    public static bool Existe(string usuario)
    {
        var r = NetUserGetInfo(null, usuario, 0, out var b);
        if (r == 0) NetApiBufferFree(b);
        return r == 0;
    }

    // la crea (cuenta de usuario normal, sin caducidad ni cambio de contraseña) o, si ya existe, pone la contraseña guardada; null si va bien
    public static string? Asegurar(string usuario, string clave)
    {
        if (Existe(usuario))
        {
            var cambio = new USER_INFO_1003 { Clave = clave };
            var r = NetUserSetInfo(null, usuario, 1003, ref cambio, out _);
            return r == 0 ? null : $"error {r} al poner la contraseña";
        }
        var info = new USER_INFO_1
        {
            Nombre = usuario, Clave = clave, Privilegio = USER_PRIV_USER, Comentario = "Cuenta sin privilegios de la zona privada (API y base de datos)",
            Banderas = UF_SCRIPT | UF_PASSWD_CANT_CHANGE | UF_DONT_EXPIRE_PASSWD,
        };
        var a = NetUserAdd(null, 1, ref info, out _);
        return a == 0 ? null : $"error {a} al crearla";
    }

    // que no salga como un usuario más en la pantalla de inicio de Windows
    public static void OcultarDeLaPantallaDeInicio(string usuario)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList");
            k?.SetValue(usuario, 0, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
    }
}
