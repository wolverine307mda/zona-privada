// Wake-on-LAN: seis bytes 0xFF y la dirección MAC repetida 16 veces (102 bytes), que se difunde por UDP al puerto 9.
// Va en su propio archivo, sin nada de Windows, para poder probarlo (lo compilan el agente, el lanzador y las pruebas del lanzador).
static class PaqueteMagico
{
    // «AA:BB:CC:DD:EE:FF» → los 102 bytes; null si no es una MAC
    public static byte[]? De(string? mac)
    {
        var trozos = (mac ?? "").Split(':');
        if (trozos.Length != 6 || trozos.Any(t => t.Length != 2 || !byte.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out _))) return null;
        var bytes = trozos.Select(t => Convert.ToByte(t, 16)).ToArray();
        var paquete = new byte[6 + 16 * 6];
        for (var i = 0; i < 6; i++) paquete[i] = 0xFF;
        for (var i = 0; i < 16; i++) bytes.CopyTo(paquete, 6 + i * 6);
        return paquete;
    }

    // la dirección de difusión de una red: la IP con todos los bits de host a 1 (192.168.1.37 con máscara 255.255.255.0 → 192.168.1.255)
    public static byte[] Difusion(byte[] ip, byte[] mascara) => ip.Zip(mascara, (a, m) => (byte)(a | ~m)).ToArray();
}
