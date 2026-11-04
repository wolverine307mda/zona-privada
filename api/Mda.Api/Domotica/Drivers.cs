using System.Collections.Concurrent;
using System.Globalization;

namespace Mda.Api.Domotica;

// Un driver sabe hablar con una marca o protocolo (Tuya, Cast, Home Assistant…). Lee el estado de un aparato y le manda órdenes.
// Valores siempre como texto: «true», «22.5», «#ffb347».
public interface IDriverCasa
{
    string Id { get; }
    Task<IReadOnlyDictionary<string, string>> Leer(Aparato a, CancellationToken ct);
    Task<(bool Ok, string Mensaje)> Ejecutar(Aparato a, Campo campo, string valor, CancellationToken ct);
}

// Aparatos de mentira para montar y probar la pantalla sin tener el hardware delante: los sensores varían un poco y las órdenes se obedecen.
public sealed class DriverDemo(TimeProvider reloj) : IDriverCasa
{
    public string Id => "demo";
    private readonly ConcurrentDictionary<int, Dictionary<string, string>> _estado = new();

    private static Dictionary<string, string> Inicial(string tipo) => tipo switch
    {
        "sensor" => new() { ["temperatura"] = "23.4", ["humedad"] = "52" },
        "puerta" => new() { ["abierto"] = "false" },
        "luz" => new() { ["encendido"] = "false", ["brillo"] = "60", ["color"] = "#ffb347" },
        "enchufe" => new() { ["encendido"] = "true", ["potencia"] = "12" },
        "clima" => new() { ["encendido"] = "false", ["objetivo"] = "24", ["modo"] = "auto" },
        "tv" => new() { ["encendido"] = "false", ["volumen"] = "20" },
        "altavoz" => new() { ["reproduciendo"] = "false", ["volumen"] = "30" },
        _ => [],
    };

    public Task<IReadOnlyDictionary<string, string>> Leer(Aparato a, CancellationToken ct)
    {
        var e = _estado.GetOrAdd(a.Id, _ => Inicial(a.Tipo));
        lock (e)
        {
            if (a.Tipo == "sensor")
            {
                // un vaivén suave según la hora, para que las gráficas tengan forma
                var h = reloj.GetLocalNow().TimeOfDay.TotalHours;
                e["temperatura"] = (22 + 3 * Math.Sin((h - 9) / 24 * 2 * Math.PI)).ToString("0.0", CultureInfo.InvariantCulture);
                e["humedad"] = (50 + 8 * Math.Cos(h / 24 * 2 * Math.PI)).ToString("0", CultureInfo.InvariantCulture);
            }
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(e));
        }
    }

    public Task<(bool Ok, string Mensaje)> Ejecutar(Aparato a, Campo campo, string valor, CancellationToken ct)
    {
        var e = _estado.GetOrAdd(a.Id, _ => Inicial(a.Tipo));
        lock (e) e[campo.Clave] = valor;
        return Task.FromResult((true, "Hecho (aparato de ejemplo)."));
    }

    public void Olvidar(int aparatoId) => _estado.TryRemove(aparatoId, out _);
}

// Los drivers de verdad (Tuya, Cast…) llegan en las siguientes tareas; mientras, un aparato con uno de ellos se queda «sin conexión»
public sealed class DriverPendiente(string id) : IDriverCasa
{
    public string Id => id;
    public Task<IReadOnlyDictionary<string, string>> Leer(Aparato a, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    public Task<(bool Ok, string Mensaje)> Ejecutar(Aparato a, Campo campo, string valor, CancellationToken ct) => Task.FromResult((false, $"El conector «{id}» aún no está activo."));
}
