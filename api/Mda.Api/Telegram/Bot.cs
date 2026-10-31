using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Telegram;

// Tu bot de Telegram (lo creas tú con @BotFather y su token va en Telegram:Token).
// Solo habla con un chat: el que se vincula desde Seguridad › Avisos con un enlace de un solo uso.
public class Bot(IConfiguration config, IHttpClientFactory http, IServiceScopeFactory scopes, TimeProvider reloj, ILogger<Bot> log)
{
    public const string ClaveChat = "telegram-chat";
    public const string ClaveNombre = "telegram-nombre";

    private string? Token => string.IsNullOrWhiteSpace(config["Telegram:Token"]) ? null : config["Telegram:Token"]!.Trim();
    public bool Configurado => Token is not null;

    // el chat vinculado, leído una vez de la base y guardado aquí
    private long? chat;
    private string? nombre;
    private bool leido;
    public bool Vinculado { get { Leer(); return Configurado && chat is not null; } }
    public long? Chat { get { Leer(); return chat; } }
    public string? Nombre { get { Leer(); return nombre; } }

    private void Leer()
    {
        if (leido) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        chat = long.TryParse(db.Ajustes.AsNoTracking().FirstOrDefault(a => a.Clave == ClaveChat)?.Valor, out var c) ? c : null;
        nombre = db.Ajustes.AsNoTracking().FirstOrDefault(a => a.Clave == ClaveNombre)?.Valor;
        leido = true;
    }

    // ---- vincular: un código que caduca a los 15 minutos, que llega con /start desde el enlace ----
    private (string Codigo, DateTimeOffset Caduca)? pendiente;

    public string NuevoCodigo()
    {
        var codigo = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        pendiente = (codigo, reloj.GetUtcNow().AddMinutes(15));
        return codigo;
    }

    public bool CodigoValido(string codigo) =>
        pendiente is { } p && p.Caduca > reloj.GetUtcNow() && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(p.Codigo), System.Text.Encoding.UTF8.GetBytes(codigo.Trim().ToLowerInvariant()));

    public async Task Vincular(long id, string quien, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Guardar(db, ClaveChat, id.ToString(), ct);
        await Guardar(db, ClaveNombre, quien, ct);
        await db.SaveChangesAsync(ct);
        (chat, nombre, leido, pendiente) = (id, quien, true, null);
    }

    public async Task Desvincular(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Ajustes.RemoveRange(db.Ajustes.Where(a => a.Clave == ClaveChat || a.Clave == ClaveNombre));
        await db.SaveChangesAsync(ct);
        (chat, nombre, leido) = (null, null, true);
    }

    private static async Task Guardar(AppDbContext db, string clave, string valor, CancellationToken ct)
    {
        var a = await db.Ajustes.FindAsync([clave], ct) ?? db.Ajustes.Add(new Ajuste { Clave = clave }).Entity;
        a.Valor = valor;
    }

    // ---- la API de Telegram ----
    private string? usuario;

    // el @nombre del bot, para el enlace t.me/…
    public async Task<string?> Usuario(CancellationToken ct = default)
    {
        if (usuario is not null) return usuario;
        var r = await Llamar("getMe", new { }, ct);
        usuario = r?.GetProperty("username").GetString();
        return usuario;
    }

    // texto plano por defecto; con html, el texto ya viene escapado y con <b>, <i>…
    public async Task<bool> Enviar(string texto, object? teclado = null, bool html = false, long? a = null, CancellationToken ct = default)
    {
        var destino = a ?? Chat;
        if (destino is null) return false;
        var cuerpo = new Dictionary<string, object> { ["chat_id"] = destino, ["text"] = texto, ["link_preview_options"] = new { is_disabled = true } };
        if (html) cuerpo["parse_mode"] = "HTML";
        if (teclado is not null) cuerpo["reply_markup"] = teclado;
        return await Llamar("sendMessage", cuerpo, ct) is not null;
    }

    public Task<JsonElement?> Llamar(string metodo, object cuerpo, CancellationToken ct = default, TimeSpan? espera = null)
        => LlamarCon(http.CreateClient(espera is null ? "telegram" : "telegram-largo"), metodo, cuerpo, ct);

    private async Task<JsonElement?> LlamarCon(HttpClient cliente, string metodo, object cuerpo, CancellationToken ct)
    {
        if (Token is not { } t) return null;
        try
        {
            using var res = await cliente.PostAsJsonAsync($"https://api.telegram.org/bot{t}/{metodo}", cuerpo, ct);
            var j = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (j.TryGetProperty("ok", out var ok) && ok.GetBoolean()) return j.GetProperty("result");
            log.LogWarning("Telegram {Metodo}: {Error}", metodo, j.TryGetProperty("description", out var d) ? d.GetString() : res.StatusCode.ToString());
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // sin la excepción entera: el mensaje de HttpRequestException puede llevar la URL con el token del bot
            log.LogWarning("Telegram {Metodo} no responde ({Motivo})", metodo, e.GetType().Name);
            return null;
        }
    }
}
