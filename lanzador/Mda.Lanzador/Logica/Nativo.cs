namespace Mda.Lanzador;

// La versión de escritorio (sin Docker): dónde vive cada cosa y cómo se configuran PostgreSQL y la API como procesos de Windows.
// Todo aquí es lógica pura (sin Windows ni procesos): se prueba aparte (Mda.Lanzador.Tests).

public record RutasNativas(string Instalacion, string Datos)
{
    public string Componentes => Path.Combine(Instalacion, "componentes");
    public string ApiDir => Path.Combine(Componentes, "api");
    public string ApiExe => Path.Combine(ApiDir, "Mda.Api.exe");
    public string PgBin => Path.Combine(Componentes, "postgres", "bin");
    public string AgeExe => Path.Combine(Componentes, "age", "age.exe");

    // lo que cambia y se guarda en ProgramData (los programas, en Program Files, quedan de solo lectura)
    public string Config => Path.Combine(Datos, "config");          // el .env de esta instalación
    public string PgDatos => Path.Combine(Datos, "pg");
    public string Claves => Path.Combine(Datos, "claves");          // claves de DataProtection (cookies, tokens)
    public string Derivados => Path.Combine(Datos, "derivados");
    public string Copias => Path.Combine(Datos, "copias");
    public string Registros => Path.Combine(Datos, "registros");
    public string Carpetas => Path.Combine(Datos, "carpetas");      // si no se indica una carpeta real, una vacía por cada raíz
    public string Temporal => Path.Combine(Datos, "temporal");

    public IEnumerable<string> DirectoriosDeDatos => [Config, PgDatos, Claves, Derivados, Copias, Registros, Carpetas, Temporal];
}

public static class ConfigNativa
{
    // otros puertos que Docker (8080 y 5432): las dos versiones pueden estar encendidas a la vez
    public const int PuertoApi = 8090;
    public const int PuertoPostgres = 5433;
    public static string UrlApi => $"http://127.0.0.1:{PuertoApi}";

    public static readonly string[] Raices = ["documentos", "escritorio", "descargas", "imagenes", "musica", "videos"];

    // postgresql.conf: solo escucha en el bucle local y gasta poco (es una base para una persona)
    public static string PostgresConf(int puerto = PuertoPostgres) => string.Join('\n',
    [
        "listen_addresses = '127.0.0.1'",
        $"port = {puerto}",
        "max_connections = 30",
        "shared_buffers = 64MB",
        "work_mem = 4MB",
        "maintenance_work_mem = 32MB",
        "effective_cache_size = 192MB",
        "password_encryption = scram-sha-256",
        "ssl = off",
        "timezone = 'UTC'",
        "log_timezone = 'UTC'",
        "logging_collector = off",
        "log_min_messages = warning",
        "log_connections = off",
        "",
    ]);

    // pg_hba.conf: solo el bucle local y siempre con contraseña
    public static string PgHba() => "host all all 127.0.0.1/32 scram-sha-256\nhost all all ::1/128 scram-sha-256\n";

    public static string CadenaPostgres(string contrasena, int puerto = PuertoPostgres) =>
        $"Host=127.0.0.1;Port={puerto};Database=zonaprivada;Username=mda;Password={contrasena}";

    // Las variables de entorno con las que arranca la API. Los nombres del .env son los mismos que usa compose.yaml, así que se puede
    // partir del .env de Docker. En modo ensayo no se pasan las claves de lo que habla con el exterior (Telegram, correo, Google…).
    public static Dictionary<string, string> EntornoApi(IReadOnlyDictionary<string, string> env, RutasNativas r, string contrasenaPostgres, bool ensayo)
    {
        string V(string clave, string defecto = "") => env.TryGetValue(clave, out var v) && v.Length > 0 ? v : defecto;
        var e = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_URLS"] = UrlApi,
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["ASPNETCORE_CONTENTROOT"] = r.ApiDir,
            ["DOTNET_gcServer"] = "0",
            ["DOTNET_TieredPGO"] = "0",
            ["AllowedHosts"] = V("ALLOWED_HOSTS", "*.ts.net;localhost;127.0.0.1"),
            ["Archivos__PermitirEjecutables"] = V("PERMITIR_EJECUTABLES", "false"),
            ["ConnectionStrings__Postgres"] = CadenaPostgres(contrasenaPostgres),
            ["Admin__Usuario"] = V("ADMIN_USUARIO"),
            ["Admin__Email"] = V("ADMIN_EMAIL"),
            ["Admin__Password"] = V("ADMIN_PASSWORD"),
            ["Seq__Url"] = "",
            ["Registro__Archivo"] = Path.Combine(r.Registros, "api-.log"),
            ["DataProtection__Ruta"] = r.Claves,
            ["Galeria__Derivados"] = r.Derivados,
            ["Boveda__Clave"] = V("MDA_CLAVE_MAESTRA"),
            ["Documentos__PinInicial"] = V("DOCUMENTOS_PIN_INICIAL"),
            ["Agente__Token"] = V("AGENTE_TOKEN"),
            // la dirección pública de ESTA instalación (los passkeys van atados a ella): la de Docker no se reutiliza
            ["App__UrlPublica"] = V("NATIVO_URL_PUBLICA"),
            ["App__Zona"] = V("ZONA_HORARIA", "Europe/Madrid"),
            ["App__CookieSegura"] = "peticion",
            ["Copias__Ruta"] = r.Copias,
            ["Copias__Origen"] = "nativo",
            ["Escritorio__Ensayo"] = ensayo ? "true" : "false",
        };
        foreach (var id in Raices) e[$"Archivos__Rutas__{id}"] = V($"CARPETA_{id.ToUpperInvariant()}", Path.Combine(r.Carpetas, id));
        if (!ensayo)
        {
            foreach (var (clave, destino) in new[]
            {
                ("CORREO_SERVIDOR", "Correo__Servidor"), ("CORREO_PUERTO", "Correo__Puerto"), ("CORREO_USUARIO", "Correo__Usuario"), ("CORREO_CLAVE", "Correo__Clave"), ("CORREO_PARA", "Correo__Para"),
                ("WHATSAPP_TELEFONO", "WhatsApp__Telefono"), ("WHATSAPP_APIKEY", "WhatsApp__ApiKey"), ("TELEGRAM_TOKEN", "Telegram__Token"),
                ("SPOTIFY_CLIENT_ID", "Spotify__ClientId"), ("SPOTIFY_CLIENT_SECRET", "Spotify__ClientSecret"),
                ("GOOGLE_CLIENT_ID", "Google__ClientId"), ("GOOGLE_CLIENT_SECRET", "Google__ClientSecret"), ("HEALTHCHECK_URL", "Healthcheck__Url"),
            })
                if (V(clave).Length > 0) e[destino] = V(clave);
        }
        return e;
    }

    // el agente habla con la API de producción: la de Docker mientras la de escritorio esté en ensayo
    public static string UrlAgente(bool nativo, bool ensayo) => nativo && !ensayo ? UrlApi : "http://localhost:8080";
}

// Reintentos de un proceso que se cae: 1, 2, 4… segundos hasta un tope de 5 minutos; si aguanta un minuto vivo, se empieza de cero
public static class Retroceso
{
    public static readonly TimeSpan Tope = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Estable = TimeSpan.FromMinutes(1);

    public static TimeSpan Espera(int fallosSeguidos)
    {
        if (fallosSeguidos <= 0) return TimeSpan.Zero;
        var s = Math.Pow(2, Math.Min(fallosSeguidos - 1, 9));
        return TimeSpan.FromSeconds(Math.Min(s, Tope.TotalSeconds));
    }

    public static int Siguiente(int fallosSeguidos, TimeSpan vivo) => vivo >= Estable ? 1 : fallosSeguidos + 1;
}

public record ComprobacionesNativas(bool Postgres, bool Api, string Agente, bool Ensayo);

public static class EstadosNativos
{
    public static (Nivel Nivel, string Texto) Resumir(ComprobacionesNativas c)
    {
        if (!c.Postgres) return (Nivel.Rojo, "PostgreSQL parado");
        if (!c.Api) return (Nivel.Rojo, "La API no responde");
        var avisos = new List<string>();
        if (c.Agente != "conectado") avisos.Add($"agente: {c.Agente}");
        if (avisos.Count > 0) return (Nivel.Ambar, string.Join(" · ", avisos) + (c.Ensayo ? " · ensayo" : ""));
        return (Nivel.Verde, c.Ensayo ? "Todo en marcha (ensayo: Docker sigue de producción)" : "Todo en marcha");
    }
}
