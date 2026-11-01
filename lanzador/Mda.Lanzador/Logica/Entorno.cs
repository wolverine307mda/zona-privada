namespace Mda.Lanzador;

// Lo que no depende de Windows ni de Docker: se prueba aparte (Mda.Lanzador.Tests).
public static class Entorno
{
    // KEY=VALUE por línea; sin comentarios (#) ni líneas vacías; quita comillas
    public static Dictionary<string, string> LeerEnv(IEnumerable<string> lineas)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var bruta in lineas)
        {
            var l = bruta.Trim();
            if (l.Length == 0 || l[0] == '#') continue;
            var i = l.IndexOf('=');
            if (i <= 0) continue;
            var v = l[(i + 1)..].Trim();
            if (v.Length >= 2 && (v[0] == '"' && v[^1] == '"' || v[0] == '\'' && v[^1] == '\'')) v = v[1..^1];
            d[l[..i].Trim()] = v;
        }
        return d;
    }

    // la carpeta de la zona privada: la primera que, subiendo desde cada punto de partida, tiene un compose.yaml
    public static string? BuscarCarpeta(IEnumerable<string?> inicios, Func<string, bool>? existeCompose = null)
    {
        existeCompose ??= d => File.Exists(Path.Combine(d, "compose.yaml"));
        foreach (var inicio in inicios)
        {
            if (string.IsNullOrWhiteSpace(inicio)) continue;
            for (var dir = new DirectoryInfo(inicio); dir is not null; dir = dir.Parent)
                if (existeCompose(dir.FullName)) return dir.FullName;
        }
        return null;
    }

    public static string? PrimeroQueExiste(IEnumerable<string?> candidatos, Func<string, bool> existe) =>
        candidatos.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && existe(c));

    // «docker compose ps --services --filter status=running» y «config --services»: un nombre por línea
    public static HashSet<string> Servicios(string salida) =>
        salida.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public enum Nivel { Verde, Ambar, Rojo }

public record Comprobaciones(bool Docker, HashSet<string> Esperados, HashSet<string> Corriendo, bool ApiLocal, bool? Publico, string Agente);

public static class Estados
{
    // Los servicios sin los que no hay app (base de datos y API); el resto (Seq, Tailscale, copias…) solo avisa
    public static readonly string[] Imprescindibles = ["db", "api"];

    public static (Nivel Nivel, string Texto) Resumir(Comprobaciones c)
    {
        if (!c.Docker) return (Nivel.Rojo, "Docker no responde");
        var faltan = c.Esperados.Where(s => !c.Corriendo.Contains(s)).OrderBy(s => s).ToList();
        if (Imprescindibles.Any(s => c.Esperados.Contains(s) && !c.Corriendo.Contains(s)) || (c.Corriendo.Count == 0))
            return (Nivel.Rojo, $"Parado: {string.Join(", ", faltan)}");
        if (!c.ApiLocal) return (Nivel.Rojo, "La API no responde");
        var avisos = new List<string>();
        if (faltan.Count > 0) avisos.Add($"parado: {string.Join(", ", faltan)}");
        if (c.Publico == false) avisos.Add("sin salida a Internet");
        if (c.Agente != "conectado") avisos.Add($"agente: {c.Agente}");
        return avisos.Count == 0 ? (Nivel.Verde, "Todo en marcha") : (Nivel.Ambar, string.Join(" · ", avisos));
    }
}

public static class TareaProgramada
{
    public const string Nombre = "Zona privada";

    // Al iniciar sesión, con los privilegios más altos (así no sale el aviso de administrador en cada arranque) y sin parar con batería
    public static string Xml(string exe, string usuario, TimeSpan retraso)
    {
        string E(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Arranca la zona privada (Docker, contenedores y agente) al iniciar sesión.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{E(usuario)}</UserId><Delay>PT{(int)retraso.TotalSeconds}S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{E(usuario)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <StartWhenAvailable>true</StartWhenAvailable>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>{E(exe)}</Command><Arguments>--silencioso</Arguments></Exec></Actions>
            </Task>
            """;
    }
}
