using System.Globalization;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Domotica;

// «Cuando el termómetro pase de 26 ° durante 5 minutos, enciende el aire a 24 °». Si «Revertir», cuando baje de (umbral − histéresis) lo apaga:
// la histéresis y la espera mínima entre acciones evitan que se encienda y apague a cada rato.
public class ReglaCasa
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activa { get; set; } = true;
    public int SensorId { get; set; }
    public string SensorClave { get; set; } = "temperatura";
    public string Operador { get; set; } = ">";      // «>» o «<»
    public double Umbral { get; set; }
    public int Minutos { get; set; }                  // cuánto tiempo tiene que mantenerse la condición
    public int DestinoId { get; set; }
    public string Accion { get; set; } = "encender";  // encender o apagar
    public double? Objetivo { get; set; }             // al encender un aire: a cuántos grados
    public string? Modo { get; set; }                 // y en qué modo
    public bool Revertir { get; set; }
    public double Histeresis { get; set; } = 1;
    public int EsperaMinutos { get; set; } = 10;      // mínimo entre una acción y la siguiente
    public bool SoloAvisar { get; set; }              // no toca nada: solo te avisa por Telegram
    // estado de la regla
    public bool Disparada { get; set; }
    public DateTimeOffset? Desde { get; set; }
    public DateTimeOffset? Ultima { get; set; }
}

public class RegistroCasa
{
    public long Id { get; set; }
    public int? ReglaId { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public string Texto { get; set; } = "";
    public bool Ok { get; set; } = true;
}

public enum PasoRegla { Nada, Disparar, Revertir }

public static class EvaluadorReglas
{
    // Una regla de ESTADO («la puerta lleva abierta 10 min») es una regla numérica con el estado convertido a 1/0:
    // se dispara cuando el valor es 1 (> 0,5) durante `Minutos`, y se rearma cuando vuelve a 0. Así se reutiliza toda la histéresis y la espera.
    public static double? Numero(string? valor)
    {
        if (valor is null) return null;
        if (bool.TryParse(valor, out var b)) return b ? 1 : 0;
        return double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static bool EsEstado(string sensorTipo, string clave) => Catalogo.Buscar(sensorTipo, clave) is { Tipo: "bool", Clave: "abierto" or "encendido" };

    // la frase de la regla de estado: «Puerta de la calle: abierta más de 10 min»
    public static string FraseEstado(string clave, bool alReves = false) => (clave, alReves) switch
    {
        ("abierto", false) => "abierta", ("abierto", true) => "cerrada",
        ("encendido", false) => "encendido", ("encendido", true) => "apagado",
        _ => clave,
    };

    public static bool Cumple(ReglaCasa r, double v) => r.Operador == ">" ? v > r.Umbral : v < r.Umbral;

    // Un paso de la regla con la lectura actual: decide si hay que actuar y deja anotado su estado (Desde, Disparada, Ultima)
    public static PasoRegla Evaluar(ReglaCasa r, double v, DateTimeOffset ahora)
    {
        var espera = TimeSpan.FromMinutes(Math.Max(0, r.EsperaMinutos));
        var libre = r.Ultima is null || ahora - r.Ultima >= espera;
        if (!r.Disparada)
        {
            if (!Cumple(r, v)) { r.Desde = null; return PasoRegla.Nada; }
            r.Desde ??= ahora;
            if (ahora - r.Desde < TimeSpan.FromMinutes(Math.Max(0, r.Minutos)) || !libre) return PasoRegla.Nada;
            r.Disparada = true; r.Ultima = ahora; r.Desde = null;
            return PasoRegla.Disparar;
        }
        // ya disparada: se vuelve a armar cuando la lectura sale de la zona (con la histéresis si hay que revertir)
        var fuera = r.Operador == ">" ? v < r.Umbral - (r.Revertir ? r.Histeresis : 0) : v > r.Umbral + (r.Revertir ? r.Histeresis : 0);
        if (!fuera) return PasoRegla.Nada;
        if (r.Revertir)
        {
            if (!libre) return PasoRegla.Nada;
            r.Disparada = false; r.Ultima = ahora; r.Desde = null;
            return PasoRegla.Revertir;
        }
        r.Disparada = false; r.Desde = null;
        return PasoRegla.Nada;
    }

    public static string Resumen(ReglaCasa r, string sensor, string destino, bool estado = false) =>
        estado ? $"{sensor}: {FraseEstado(r.SensorClave)}{(r.Minutos > 0 ? $" más de {r.Minutos} min" : "")} → {(r.SoloAvisar ? "avisar" : $"{r.Accion} {destino}")}" :
        $"{sensor}: {(r.Operador == ">" ? "más de" : "menos de")} {r.Umbral.ToString("0.#", CultureInfo.GetCultureInfo("es-ES"))}{(r.Minutos > 0 ? $" durante {r.Minutos} min" : "")} → {(r.SoloAvisar ? "avisar" : $"{r.Accion} {destino}")}";
}

// Mira las reglas cada 30 segundos y actúa; cada vez que dispara o revierte lo apunta en el registro
public sealed class MotorReglas(IServiceScopeFactory ambitos, ServicioCasa casa, IAvisos avisos, Mda.Api.Telegram.Bot bot, TimeProvider reloj, ILogger<MotorReglas> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), ct);
            while (!ct.IsCancellationRequested)
            {
                try { using var scope = ambitos.CreateScope(); await Vuelta(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct); }
                catch (Exception e) when (!ct.IsCancellationRequested) { log.LogWarning(e, "No se pudieron evaluar las reglas de casa"); }
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }
        catch (OperationCanceledException) { /* se apaga la API */ }
    }

    // Una pasada por todas las reglas activas (pública para probarla sin esperar)
    public async Task Vuelta(AppDbContext db, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var reglas = await db.Reglas.Where(r => r.Activa).ToListAsync(ct);
        if (reglas.Count == 0) return;
        var aparatos = await db.Aparatos.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
        var entidades = await db.Entidades.AsNoTracking().ToListAsync(ct);
        foreach (var r in reglas)
        {
            if (!aparatos.TryGetValue(r.SensorId, out var sensor) || !aparatos.TryGetValue(r.DestinoId, out var destino)) continue;
            var e = entidades.FirstOrDefault(x => x.AparatoId == r.SensorId && x.Clave == r.SensorClave);
            // un dato con más de 30 minutos no sirve para decidir: puede que el sensor esté caído
            if (e?.Valor is null || e.Actualizado is null || ahora - e.Actualizado > TimeSpan.FromMinutes(30)) continue;
            if (EvaluadorReglas.Numero(e.Valor) is not { } v) continue;
            var estado = EvaluadorReglas.EsEstado(sensor.Tipo, r.SensorClave);
            var paso = EvaluadorReglas.Evaluar(r, v, ahora);
            if (paso == PasoRegla.Nada) continue;
            await Actuar(db, r, sensor, destino, v, paso, ahora, ct, estado, (int)Math.Max(0, (ahora - e.Actualizado.Value).TotalMinutes));
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task Actuar(AppDbContext db, ReglaCasa r, Aparato sensor, Aparato destino, double v, PasoRegla paso, DateTimeOffset ahora, CancellationToken ct, bool estado = false, int datoHace = 0)
    {
        if (estado && r.SoloAvisar)
        {
            // «Puerta de la calle lleva abierta más de 10 min» (con el último dato de hace X min) y, si se puede, un botón para apagarlo
            var frase = EvaluadorReglas.FraseEstado(r.SensorClave, paso == PasoRegla.Revertir);
            var t = paso == PasoRegla.Disparar
                ? $"«{r.Nombre}»: {sensor.Nombre} lleva {frase}{(r.Minutos > 0 ? $" más de {r.Minutos} min" : "")} (último dato hace {datoHace} min)."
                : $"«{r.Nombre}»: {sensor.Nombre} vuelve a estar {EvaluadorReglas.FraseEstado(r.SensorClave, true)}.";
            if (paso == PasoRegla.Disparar && bot.Vinculado)
            {
                var puedeApagar = Catalogo.Buscar(sensor.Tipo, "encendido") is { Escribible: true } && r.SensorClave == "encendido";
                var botones = puedeApagar
                    ? new[] { new { text = "Apagar", callback_data = $"ca:{sensor.Id}" }, new { text = "Ya lo sé", callback_data = "cv:" } }
                    : new[] { new { text = "Ya lo sé", callback_data = "cv:" } };
                _ = bot.Enviar($"🏠 {t}", new { inline_keyboard = new[] { botones } }, ct: ct);
            }
            else _ = avisos.PorTelegram($"🏠 {t}", ct);
            db.RegistrosCasa.Add(new RegistroCasa { ReglaId = r.Id, Fecha = ahora, Texto = t, Ok = true });
            return;
        }
        var enciende = (r.Accion == "encender") == (paso == PasoRegla.Disparar);
        var valor = v.ToString("0.#", CultureInfo.GetCultureInfo("es-ES"));
        string texto; var ok = true;
        if (r.SoloAvisar)
        {
            texto = paso == PasoRegla.Disparar ? $"«{r.Nombre}»: {sensor.Nombre} marca {valor}." : $"«{r.Nombre}»: {sensor.Nombre} vuelve a {valor}.";
            _ = avisos.PorTelegram($"🏠 {texto}", ct);
        }
        else
        {
            var fallos = new List<string>();
            async Task Mandar(string clave, string val)
            {
                var (bien, mensaje, _) = await casa.Ejecutar(db, destino, clave, val, ct);
                if (!bien) fallos.Add(mensaje);
            }
            if (Catalogo.Buscar(destino.Tipo, "encendido") is not null) await Mandar("encendido", enciende ? "true" : "false");
            if (enciende && paso == PasoRegla.Disparar && destino.Tipo == "clima")
            {
                if (r.Objetivo is { } o) await Mandar("objetivo", o.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(r.Modo)) await Mandar("modo", r.Modo);
            }
            ok = fallos.Count == 0;
            texto = $"«{r.Nombre}»: {sensor.Nombre} {valor} → {(enciende ? "enciende" : "apaga")} {destino.Nombre}{(ok ? "" : " (falló: " + fallos[0] + ")")}";
            _ = avisos.PorTelegram($"{(ok ? "🏠" : "⚠️")} {texto}", ct);
        }
        db.RegistrosCasa.Add(new RegistroCasa { ReglaId = r.Id, Fecha = ahora, Texto = texto, Ok = ok });
    }
}
