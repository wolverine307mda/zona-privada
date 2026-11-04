using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Mda.Lanzador;

// Lo que hace falta para arrancar por primera vez sin tocar el .env a mano: qué claves faltan, cuáles se inventan y cómo se completan.
// Sin Windows ni Docker, así que se prueba aparte (Mda.Lanzador.Tests).
public static class Primera
{
    // lo que tiene que haber en el .env para que «docker compose up» funcione (el compose los exige)
    public static readonly string[] Obligatorias = ["POSTGRES_PASSWORD", "ADMIN_EMAIL", "ADMIN_PASSWORD", "SEQ_PASSWORD", "AGENTE_TOKEN", "MDA_CLAVE_MAESTRA"];
    // las que se pueden inventar; el correo y la contraseña de administrador los pone la persona
    public static readonly string[] Generables = ["POSTGRES_PASSWORD", "SEQ_PASSWORD", "AGENTE_TOKEN", "MDA_CLAVE_MAESTRA"];

    public static List<string> Faltan(IReadOnlyDictionary<string, string> env) =>
        Obligatorias.Where(k => !env.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)).ToList();

    // Una clave nueva para cada una: la maestra en base64 (32 bytes), el token del agente en hexadecimal (64 letras, la API pide 32 o más)
    // y las contraseñas en letras y cifras, que no dan problemas ni en el .env ni en las direcciones.
    public static string Generar(string clave) => clave switch
    {
        "MDA_CLAVE_MAESTRA" => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        "AGENTE_TOKEN" => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
        _ => Alfanumerica(32),
    };

    private static string Alfanumerica(int longitud)
    {
        const string letras = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var sb = new StringBuilder(longitud);
        for (var i = 0; i < longitud; i++) sb.Append(letras[RandomNumberGenerator.GetInt32(letras.Length)]);
        return sb.ToString();
    }

    public static bool ClaveAdminValida(string? p) => p is { Length: >= 12 };
    public static bool CorreoValido(string? c) => c is { Length: >= 5 } && Regex.IsMatch(c.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    // Añade al .env lo que falte. Una clave que ya tiene valor NO se toca (cambiar la clave maestra o la de la base dejaría los datos ilegibles);
    // una clave que está vacía («X=») se rellena y las que no están se añaden al final. Respeta los saltos de línea del archivo.
    public static string Completar(string contenido, IReadOnlyDictionary<string, string> nuevos)
    {
        var salto = contenido.Contains("\r\n") ? "\r\n" : "\n";
        var lineas = contenido.Replace("\r\n", "\n").Split('\n').ToList();
        if (lineas.Count > 0 && lineas[^1].Length == 0) lineas.RemoveAt(lineas.Count - 1);
        foreach (var (clave, valor) in nuevos)
        {
            var i = lineas.FindIndex(l => Regex.IsMatch(l, $@"^\s*{Regex.Escape(clave)}\s*="));
            if (i < 0) { lineas.Add($"{clave}={valor}"); continue; }
            var actual = lineas[i][(lineas[i].IndexOf('=') + 1)..].Trim().Trim('"', '\'');
            if (actual.Length == 0) lineas[i] = $"{clave}={valor}";
        }
        return string.Join(salto, lineas) + salto;
    }

    // Para el diagnóstico: los nombres del .env y si tienen valor, nunca el valor
    public static string EnvSinValores(IEnumerable<string> lineas)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in Entorno.LeerEnv(lineas).OrderBy(x => x.Key, StringComparer.Ordinal)) sb.AppendLine($"{k} = {(v.Length == 0 ? "<vacía>" : "<definida>")}");
        return sb.ToString();
    }
}
