using System.Buffers.Text;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Auth;

// Lo que Identity no guarda de un passkey: desde qué dispositivo se creó (IP, navegador, sistema, idioma, zona horaria, pantalla)
// y cuándo, desde dónde y para qué se ha usado. La clave es el id de la credencial en base64 de URL.
public class PasskeyInfo
{
    public string Id { get; set; } = "";
    public string UsuarioId { get; set; } = "";
    public DateTimeOffset AltaEn { get; set; }
    public string AltaIp { get; set; } = "";
    public string Dispositivo { get; set; } = "";      // «iPhone · Safari»
    public string Tipo { get; set; } = "otro";          // movil | tablet | ordenador | otro
    public string UserAgent { get; set; } = "";
    public string Idioma { get; set; } = "";
    public string Zona { get; set; } = "";
    public string Pantalla { get; set; } = "";
    public DateTimeOffset? UltimoUso { get; set; }
    public string UltimaIp { get; set; } = "";
    public string UltimoDispositivo { get; set; } = "";
    public string UltimoMotivo { get; set; } = "";     // entrada | desbloqueo | confirmación
    public int Usos { get; set; }
}

// lo que manda el navegador al crear el passkey (el servidor ya ve la IP y el User-Agent por su cuenta)
public record DispositivoCliente(string? Idioma, string? Zona, string? Pantalla);

public static class PasskeyInfos
{
    public static string IdDe(byte[] credencial) => Base64Url.EncodeToString(credencial);

    // el id de la credencial dentro del JSON que devuelve el navegador (ya viene en base64 de URL)
    public static string? IdDe(JsonElement credencial) =>
        credencial.ValueKind == JsonValueKind.Object && credencial.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;

    public static byte[]? Decodificar(string id)
    {
        try { return Base64Url.DecodeFromChars(id.Replace('+', '-').Replace('/', '_').TrimEnd('=')); } catch (FormatException) { return null; }
    }

    private static string Cortar(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();

    private static string Agente(HttpContext ctx) => Cortar(ctx.Request.Headers.UserAgent.ToString(), 400);

    public static async Task Alta(AppDbContext db, HttpContext ctx, string usuarioId, byte[] credencial, DispositivoCliente? d, TimeProvider reloj)
    {
        var ua = Agente(ctx);
        var id = IdDe(credencial);
        var fila = await db.PasskeysInfo.FindAsync(id);
        if (fila is null) db.PasskeysInfo.Add(fila = new PasskeyInfo { Id = id });
        fila.UsuarioId = usuarioId;
        fila.AltaEn = reloj.GetUtcNow();
        fila.AltaIp = IpCliente.De(ctx);
        fila.Dispositivo = Dispositivo.Nombre(ua);
        fila.Tipo = Dispositivo.Tipo(ua);
        fila.UserAgent = ua;
        fila.Idioma = Cortar(d?.Idioma, 20);
        fila.Zona = Cortar(d?.Zona, 60);
        fila.Pantalla = Cortar(d?.Pantalla, 20);
        await db.SaveChangesAsync();
    }

    public static async Task Uso(AppDbContext db, HttpContext ctx, string id, string motivo, TimeProvider reloj)
    {
        var fila = await db.PasskeysInfo.FindAsync(id);
        if (fila is null) return; // creado antes de que existiera este registro: no hay de dónde sacar el alta
        var ua = Agente(ctx);
        fila.UltimoUso = reloj.GetUtcNow();
        fila.UltimaIp = IpCliente.De(ctx);
        fila.UltimoDispositivo = Dispositivo.Nombre(ua);
        fila.UltimoMotivo = motivo;
        fila.Usos++;
        await db.SaveChangesAsync();
    }

    public static async Task Quitar(AppDbContext db, string id)
    {
        if (await db.PasskeysInfo.FindAsync(id) is { } f) { db.PasskeysInfo.Remove(f); await db.SaveChangesAsync(); }
    }
}
