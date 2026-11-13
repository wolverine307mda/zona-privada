using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Agenda;

// Dónde se copian los eventos nuevos (cuenta y calendario de Google) y qué calendarios de cada cuenta se ven en la agenda
public record DestinoGoogle(string Cuenta, string Calendario);

public record AjustesGoogle(DestinoGoogle? Defecto, Dictionary<string, string[]> Mostrar)
{
    public static readonly AjustesGoogle Vacios = new(null, []);
}

public record CalendarioGoogle(string Id, string Nombre, string? Color, bool Principal, bool Escribible);

public record CuentaGoogle(string Clave, string? Cuenta, IReadOnlyList<CalendarioGoogle> Calendarios);

public static class GoogleAjustes
{
    public const string Clave = "google-agenda";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<AjustesGoogle> Leer(AppDbContext db, CancellationToken ct)
    {
        var v = (await db.Ajustes.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == Clave, ct))?.Valor;
        try { return v is null ? AjustesGoogle.Vacios : JsonSerializer.Deserialize<AjustesGoogle>(v, Json) is { Mostrar: not null } a ? a : AjustesGoogle.Vacios; }
        catch (JsonException) { return AjustesGoogle.Vacios; }
    }

    public static async Task Guardar(AppDbContext db, AjustesGoogle a, CancellationToken ct)
    {
        var valor = JsonSerializer.Serialize(a, Json);
        var fila = await db.Ajustes.FindAsync([Clave], ct);
        if (fila is null) db.Ajustes.Add(new Ajuste { Clave = Clave, Valor = valor }); else fila.Valor = valor;
        await db.SaveChangesAsync(ct);
    }

    // Los calendarios de una cuenta que se ven en la agenda: los elegidos o, si no ha elegido, solo el principal
    public static IReadOnlyList<CalendarioGoogle> Mostrados(CuentaGoogle cuenta, AjustesGoogle a) =>
        a.Mostrar.TryGetValue(cuenta.Clave, out var ids)
            ? cuenta.Calendarios.Where(c => ids.Contains(c.Id)).ToList()
            : cuenta.Calendarios.Where(c => c.Principal).ToList();

    // Dónde va un evento: lo que pida (si existe y se puede escribir), si no el calendario por defecto y, si tampoco, el principal de la primera cuenta. Null si no hay ninguna cuenta.
    public static DestinoGoogle? Destino(string? cuenta, string? calendario, AjustesGoogle a, IReadOnlyList<CuentaGoogle> cuentas)
    {
        DestinoGoogle? Valido(string? cu, string? ca)
        {
            if (cuentas.FirstOrDefault(x => x.Clave == cu) is not { } c) return null;
            var cal = ca is null ? c.Calendarios.FirstOrDefault(x => x.Principal && x.Escribible) : c.Calendarios.FirstOrDefault(x => x.Id == ca && x.Escribible);
            return cal is null ? null : new DestinoGoogle(c.Clave, cal.Id);
        }
        return Valido(cuenta, calendario) ?? (a.Defecto is { } d ? Valido(d.Cuenta, d.Calendario) : null) ?? cuentas.Select(c => Valido(c.Clave, null)).FirstOrDefault(x => x is not null);
    }

    // Lo que llega de la pantalla: solo cuentas y calendarios que existen (y el de por defecto, escribible)
    public static string? Validar(AjustesGoogle a, IReadOnlyList<CuentaGoogle> cuentas)
    {
        foreach (var (clave, ids) in a.Mostrar)
        {
            if (cuentas.FirstOrDefault(c => c.Clave == clave) is not { } c) return "Esa cuenta de Google no está enlazada.";
            if (ids.Any(i => c.Calendarios.All(x => x.Id != i))) return "Ese calendario no existe en la cuenta.";
        }
        if (a.Defecto is { } d && (cuentas.FirstOrDefault(c => c.Clave == d.Cuenta) is not { } cd || cd.Calendarios.All(x => x.Id != d.Calendario || !x.Escribible)))
            return "El calendario por defecto tiene que existir y poder escribirse.";
        return null;
    }
}
