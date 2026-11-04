using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Mda.Api.Seguridad;

// Cifrado de lo sensible en la base de datos (notas, portapapeles, claves de push…) con una clave maestra que solo vive en el
// .env (MDA_CLAVE_MAESTRA). Un hash no sirve aquí: no se puede deshacer, y estos datos hay que volver a leerlos.
// AES-256-GCM: cifra y además detecta si alguien toca el dato. Cada propósito (notas, portapapeles…) deriva su propia subclave
// con HKDF, así que una subclave sola no abre las demás. Formato guardado: «v1:» + base64(nonce 12 + etiqueta 16 + cifrado).
// Lo que se lee sin el prefijo «v1:» es un dato antiguo sin cifrar y se devuelve tal cual (se cifra al guardarlo de nuevo).
public static class Boveda
{
    public const string Prefijo = "v1:";
    public const string ClaveEnv = "MDA_CLAVE_MAESTRA";

    // sin clave configurada (desarrollo y pruebas) se usa una fija y conocida; en producción Iniciar exige la verdadera
    private static byte[] maestra = SHA256.HashData("zona-privada-desarrollo"u8);
    private static readonly Dictionary<string, byte[]> subclaves = [];

    public static void Iniciar(string? clave, bool exigir)
    {
        if (string.IsNullOrWhiteSpace(clave))
        {
            if (exigir) throw new InvalidOperationException($"Falta {ClaveEnv} en el .env: es la clave que cifra notas, portapapeles y claves de servicios. Genérala con «openssl rand -base64 32» y guárdala también fuera del PC.");
            return;
        }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(clave.Trim()); }
        catch (FormatException) { throw new InvalidOperationException($"{ClaveEnv} no es base64 válido (usa «openssl rand -base64 32»)."); }
        if (bytes.Length < 32) throw new InvalidOperationException($"{ClaveEnv} es demasiado corta: hacen falta al menos 32 bytes (44 letras en base64).");
        lock (subclaves) { maestra = bytes; subclaves.Clear(); }
    }

    private static byte[] Subclave(string proposito)
    {
        lock (subclaves)
        {
            if (!subclaves.TryGetValue(proposito, out var k))
                subclaves[proposito] = k = HKDF.DeriveKey(HashAlgorithmName.SHA256, maestra, 32, salt: null, info: Encoding.UTF8.GetBytes("zona-privada/" + proposito));
            return k;
        }
    }

    public static bool EstaCifrado(string? v) => v is not null && v.StartsWith(Prefijo, StringComparison.Ordinal);

    // el texto vacío se queda vacío: no hay nada que proteger y se evita ruido en la base
    public static string Cifrar(string? texto, string proposito)
    {
        if (string.IsNullOrEmpty(texto)) return texto ?? "";
        var datos = Encoding.UTF8.GetBytes(texto);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cifrado = new byte[datos.Length];
        var etiqueta = new byte[16];
        using (var aes = new AesGcm(Subclave(proposito), 16)) aes.Encrypt(nonce, datos, cifrado, etiqueta, Encoding.UTF8.GetBytes(proposito));
        var todo = new byte[12 + 16 + cifrado.Length];
        nonce.CopyTo(todo, 0); etiqueta.CopyTo(todo, 12); cifrado.CopyTo(todo, 28);
        return Prefijo + Convert.ToBase64String(todo);
    }

    public static string Descifrar(string? valor, string proposito)
    {
        if (string.IsNullOrEmpty(valor) || !EstaCifrado(valor)) return valor ?? "";
        try
        {
            var todo = Convert.FromBase64String(valor[Prefijo.Length..]);
            var datos = new byte[todo.Length - 28];
            using var aes = new AesGcm(Subclave(proposito), 16);
            aes.Decrypt(todo.AsSpan(0, 12), todo.AsSpan(28), todo.AsSpan(12, 16), datos, Encoding.UTF8.GetBytes(proposito));
            return Encoding.UTF8.GetString(datos);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException($"No se puede descifrar un dato ({proposito}): ¿{ClaveEnv} es la misma con la que se guardó?", e);
        }
    }

    // para el modelo de EF: la propiedad se guarda cifrada y en el código se ve en claro
    public static PropertyBuilder<string> Cifrada(this PropertyBuilder<string> p, string proposito) =>
        p.HasConversion(new ValueConverter<string, string>(v => Cifrar(v, proposito), v => Descifrar(v, proposito)));
}
