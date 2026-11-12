// API de la zona privada: inicio de sesión con cookie (ASP.NET Core Identity), PostgreSQL con EF Core,
// estado del servidor en tiempo real con SignalR y, en producción, el front de Angular servido desde wwwroot.
using System.Threading.RateLimiting;
using Mda.Api.Agenda;
using Mda.Api.Archivos;
using Mda.Api.Asistente;
using Mda.Api.Atajos;
using Mda.Api.Auth;
using Mda.Api.Avisos;
using Mda.Api.Conexiones;
using Mda.Api.Data;
using Mda.Api.Galeria;
using Mda.Api.Gastos;
using Mda.Api.Habitos;
using Mda.Api.Tareas;
using Mda.Api.Papeles;
using Mda.Api.Automatizaciones;
using Mda.Api.Servicios;
using Mda.Api.Compra;
using Mda.Api.Contexto;
using Mda.Api.Luz;
using Mda.Api.Pc;
using Mda.Api.Notas;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Mda.Api.Telegram;
using Mda.Api.Tiempo;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// sin la cabecera «Server: Kestrel»
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// ---- Registros: consola siempre y Seq si hay dirección configurada ----
builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console();
    var seq = ctx.Configuration["Seq:Url"];
    if (!string.IsNullOrWhiteSpace(seq)) cfg.WriteTo.Seq(seq);
    // en la versión de escritorio no hay Seq: se escribe a un fichero por día, y se guardan los de una semana
    var archivo = ctx.Configuration["Registro:Archivo"];
    if (!string.IsNullOrWhiteSpace(archivo)) cfg.WriteTo.File(archivo, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true);
});

// ---- Clave maestra que cifra notas, portapapeles y claves de servicios en la base (en producción es obligatoria) ----
Boveda.Iniciar(builder.Configuration["Boveda:Clave"], exigir: builder.Environment.IsProduction());

// ---- Claves de servicios (Telegram, WhatsApp…): se guardan cifradas en la base y ganan al .env, sin que el resto del código cambie ----
var secretosFuente = new SecretosFuente();
((Microsoft.Extensions.Configuration.IConfigurationBuilder)builder.Configuration).Add(secretosFuente);
builder.Services.AddSingleton(secretosFuente);
builder.Services.AddSingleton<Secretos>();

// ---- Base de datos: PostgreSQL; en desarrollo, sin Docker, se puede usar una en memoria (Datos:EnMemoria=true) ----
var enMemoria = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Datos:EnMemoria");
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (enMemoria) o.UseInMemoryDatabase("zonaprivada");
    else o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
});

// ---- Usuarios e inicio de sesión: solo cookie, sin registro público ----
builder.Services.AddAuthorization();
builder.Services
    .AddIdentityApiEndpoints<IdentityUser>(o =>
    {
        // contraseña a elección del dueño (la única cuenta): mínimo 12 caracteres, sin reglas de símbolos (mejor una frase larga).
        // La protegen el bloqueo tras 5 fallos, el límite por IP y el 2FA.
        o.Password.RequiredLength = 12;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
        // passkeys (Face ID, Windows Hello): necesitan el esquema 3 de Identity y ir atados al dominio público de la app
        o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>();

// el passkey queda atado al dominio público: registrado desde otra dirección (localhost) no vale y se avisa
if (Uri.TryCreate(builder.Configuration["App:UrlPublica"], UriKind.Absolute, out var publica))
    builder.Services.Configure<IdentityPasskeyOptions>(o => o.ServerDomain = publica.Host);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ClaimsPrincipalAccessor>();

builder.Services.ConfigureApplicationCookie(o =>
{
    // App:CookieNombre distingue una copia de pruebas que comparte dirección con la real (las cookies no distinguen puertos)
    o.Cookie.Name = LlamadasExternas.CookieSesion = builder.Configuration["App:CookieNombre"] is { Length: > 0 } nombreCookie ? nombreCookie : "mda.sesion";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    // en producción la cookie solo viaja por HTTPS (el proxy, el túnel de Cloudflare, avisa con X-Forwarded-Proto)
    // App:CookieSegura=peticion (versión de escritorio por http://localhost): Secure solo cuando la petición llega por HTTPS
    o.Cookie.SecurePolicy = builder.Environment.IsProduction() && builder.Configuration["App:CookieSegura"] != "peticion" ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    // la cookie dura lo mismo que el tope de la sesión apuntada (Sesiones.Maxima): antes caducaba a las 8 h y pedía entrar de nuevo.
    // La seguridad la dan «Face ID al volver», el PIN de Seguridad y poder cerrar sesiones desde otro equipo
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
    // una API no redirige a una página de login: responde 401 / 403
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    // cada cookie es una sesión apuntada en la base (Seguridad): se puede ver y cerrar desde otro equipo
    o.Events.OnSigningIn = Sesiones.AlEntrar;
    var sello = o.Events.OnValidatePrincipal;
    o.Events.OnValidatePrincipal = async ctx =>
    {
        await sello(ctx);
        if (ctx.Principal is not null) await Sesiones.AlValidar(ctx);
    };
});
// al renovar la cookie por el sello de seguridad, se conserva el id de la sesión
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.OnRefreshingPrincipal = ctx =>
{
    if (ctx.CurrentPrincipal?.FindFirst(Sesiones.Claim) is { } c && ctx.NewPrincipal?.Identity is System.Security.Claims.ClaimsIdentity id)
        id.AddClaim(new System.Security.Claims.Claim(c.Type, c.Value));
    return Task.CompletedTask;
});

// ---- Claves que firman la cookie: en un volumen, para que reiniciar el contenedor no cierre la sesión ----
var claves = builder.Configuration["DataProtection:Ruta"];
var proteccion = builder.Services.AddDataProtection().SetApplicationName("zona-privada");
if (!string.IsNullOrWhiteSpace(claves)) proteccion.PersistKeysToFileSystem(new DirectoryInfo(claves));

// ---- Límites por IP: intentos de entrar, acciones sensibles y un tope general ----
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(Limites.Acceso, ctx => RateLimitPartition.GetFixedWindowLimiter(
        IpCliente.De(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // avisos de prueba (mandan correo, WhatsApp, Telegram) y cambio de contraseña
    o.AddPolicy(Limites.Sensible, ctx => RateLimitPartition.GetFixedWindowLimiter(
        IpCliente.De(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    o.AddPolicy(Limites.Publico, ctx => RateLimitPartition.GetFixedWindowLimiter(
        IpCliente.De(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy(Limites.Atajos, ctx => RateLimitPartition.GetFixedWindowLimiter(
        IpCliente.De(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy(Limites.Reauth, ctx => RateLimitPartition.GetFixedWindowLimiter(
        IpCliente.De(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy(Limites.Agente, ctx => RateLimitPartition.GetFixedWindowLimiter(
        IpCliente.De(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // tope general por IP; los archivos (una cuadrícula de fotos pide muchas a la vez) y SignalR quedan fuera
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        ctx.Request.Path.StartsWithSegments("/api/archivos/archivo") || ctx.Request.Path.StartsWithSegments("/hubs")
            ? RateLimitPartition.GetNoLimiter("libre")
            : RateLimitPartition.GetFixedWindowLimiter(IpCliente.De(ctx),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// ---- La web pública (GitHub Pages) solo puede preguntar si el equipo está encendido: GET /api/salud ----
const string WebPublica = "web-publica";
builder.Services.AddCors(o => o.AddPolicy(WebPublica, p => p
    .WithOrigins(builder.Configuration.GetSection("WebPublica:Origenes").Get<string[]>() ?? ["https://wolvem.dev", "https://wolverine307mda.github.io"])
    .WithMethods("GET")));

// ---- Detrás del túnel de Cloudflare: respetar el esquema y la IP originales ----
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // la API solo es accesible desde la red interna de Docker (cloudflared) y desde 127.0.0.1, así que se confía en el proxy.
    // ForwardLimit=1 (por defecto): se toma la IP que añadió el último proxy, no la que escriba el visitante
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// ---- JSON: enums como texto («Gasto», «Mensual») ----
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ---- Estado del servidor en tiempo real ----
builder.Services.AddSignalR();
builder.Services.AddSingleton<ILectorSistema>(_ => LectorSistema.ParaEsteEquipo());
builder.Services.AddSingleton<EstadoSistema>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EstadoAgente>();
// ---- Al arrancar: migraciones y usuario administrador desde la configuración ----
// (tiene que ir ANTES que cualquier servicio de fondo: el host los arranca en orden y así ya tienen las tablas, también en una base nueva)
builder.Services.AddHostedService<SemillaAdmin>();

// salud del PC y órdenes al agente
builder.Services.AddSingleton<EstadoSalud>();
builder.Services.AddSingleton<AvisosSalud>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AvisosSalud>());
builder.Services.AddHostedService<MonitorSistema>();
builder.Services.AddHostedService<AvisosBateria>();
builder.Services.AddHostedService<VigilanteRed>();

// ---- Documentos: las carpetas del equipo montadas en Archivos:Base ----
builder.Services.AddSingleton<Carpetas>();
// miniaturas WebP de la galería pública (sin EXIF)
builder.Services.AddSingleton<Derivados>();
builder.Services.AddSingleton<EstadoDireccion>();
builder.Services.AddHostedService<VigilanteDireccion>();
builder.Services.AddSingleton<Sondas>();
builder.Services.AddSingleton<LlamadasEnVivo>();
builder.Services.AddSingleton<GeoIp>();
builder.Services.AddHostedService<ActualizadorGeoIp>();
builder.Services.AddSingleton<RegistroSondas>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RegistroSondas>());
builder.Services.AddSingleton(sp => new Tarpit(sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IComprobadorRed, ComprobadorTcp>();
builder.Services.AddHostedService<ResumenSondas>();
builder.Services.AddHostedService<VelocidadNocturna>();
// Casa propia: drivers (los de ejemplo ya; Tuya y Cast llegan después), servicio y vigilante que refresca los aparatos cada minuto
builder.Services.AddSingleton<Mda.Api.Domotica.DriverDemo>();
builder.Services.AddSingleton<Mda.Api.Domotica.IDriverCasa>(sp => sp.GetRequiredService<Mda.Api.Domotica.DriverDemo>());
builder.Services.AddSingleton<Mda.Api.Domotica.ClienteTuya>();
builder.Services.AddSingleton<Mda.Api.Domotica.IDriverCasa, Mda.Api.Domotica.DriverTuya>();
foreach (var pendiente in new[] { "cast", "hogar" }) builder.Services.AddSingleton<Mda.Api.Domotica.IDriverCasa>(new Mda.Api.Domotica.DriverPendiente(pendiente));
builder.Services.AddSingleton<Mda.Api.Domotica.ServicioCasa>();
builder.Services.AddHostedService<Mda.Api.Domotica.VigilanteCasa>();
builder.Services.AddSingleton<Mda.Api.Domotica.MotorReglas>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Mda.Api.Domotica.MotorReglas>());
builder.Services.AddHostedService<LatidoExterno>();
builder.Services.AddSingleton<Ocr>();
builder.Services.AddSingleton<IndiceDocumentos>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IndiceDocumentos>());
builder.Services.AddSingleton<IndicadorFotos>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IndicadorFotos>());
builder.Services.AddSingleton<Pregenerar>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Pregenerar>());
// Brotli y gzip para JSON y para el front (JS, CSS, HTML, SVG): antes solo se comprimía el JSON y los bundles salían enteros.
// Al comprimir en cada petición, Brotli al nivel más rápido: casi igual de pequeño y sin gastar CPU.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(["application/json", "image/svg+xml", "application/manifest+json"]);
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);


// ---- Avisos (correo, WhatsApp), cuentas enlazadas (Spotify, Google) y agenda con recordatorios ----
builder.Services.AddMemoryCache();
// sin registros de cada petición: la URL de CallMeBot lleva la apikey
builder.Services.AddHttpClient("hogar", c => c.Timeout = TimeSpan.FromSeconds(8)); // Home Assistant y AdGuard, de la red de casa
builder.Services.AddSingleton<ClienteHogar>();
builder.Services.AddSingleton<ClienteAdGuard>();
builder.Services.AddHttpClient("avisos", c => c.Timeout = TimeSpan.FromSeconds(20)).RemoveAllLoggers();
builder.Services.AddHttpClient("conexiones", c => c.Timeout = TimeSpan.FromSeconds(15));
// sin registros de cada petición: el token del bot va en la propia URL y acabaría en los logs y en Seq
builder.Services.AddHttpClient("telegram", c => c.Timeout = TimeSpan.FromSeconds(20)).RemoveAllLoggers();
builder.Services.AddHttpClient("telegram-largo", c => c.Timeout = TimeSpan.FromSeconds(75)).RemoveAllLoggers(); // getUpdates espera hasta 50 s
builder.Services.AddHttpClient("tiempo", c => c.Timeout = TimeSpan.FromSeconds(10));
// precios de las gasolineras (el servicio del Ministerio manda unos 8 MB de JSON, con gzip): una consulta cada 3 horas como mucho
builder.Services.AddHttpClient("carburantes", c => { c.Timeout = TimeSpan.FromSeconds(45); c.DefaultRequestHeaders.UserAgent.ParseAdd("zona-privada/1.0"); c.DefaultRequestHeaders.Accept.ParseAdd("application/json"); })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }).RemoveAllLoggers();
builder.Services.AddSingleton<Mda.Api.Gastos.ICarburantes, Mda.Api.Gastos.ServicioCarburantes>();
builder.Services.AddSingleton<Push>();
// bot de Telegram: avisos y hablar con el asistente desde el chat
builder.Services.AddSingleton<Bot>();
builder.Services.AddScoped<Conversacion>();
builder.Services.AddHostedService<Sondeo>();
// el tiempo (Open-Meteo)
builder.Services.AddSingleton<ServicioTiempo>();
builder.Services.AddSingleton<Mda.Api.Tiempo.ILluvia, Mda.Api.Tiempo.ServicioLluvia>();
// letras de lo que suena (LRCLIB, comunitaria): con un User-Agent educado, como piden sus normas
builder.Services.AddHttpClient("lrclib", c => { c.Timeout = TimeSpan.FromSeconds(10); c.DefaultRequestHeaders.UserAgent.ParseAdd("zona-privada/1.0 (uso personal)"); });
builder.Services.AddSingleton<Mda.Api.Conexiones.ServicioLetras>();
// leer recetas de páginas web: sin seguir redirecciones solas (se miran una a una) y con tope de tiempo
builder.Services.AddHttpClient("recetas", c => { c.Timeout = TimeSpan.FromSeconds(15); c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; zona-privada/1.0)"); })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, AutomaticDecompression = System.Net.DecompressionMethods.All }).RemoveAllLoggers();
builder.Services.AddSingleton<Mda.Api.Cocina.ServicioRecetas>();
// series: TVMaze (gratis y sin clave)
builder.Services.AddHttpClient("tvmaze", c => { c.Timeout = TimeSpan.FromSeconds(10); c.DefaultRequestHeaders.UserAgent.ParseAdd("zona-privada/1.0 (uso personal)"); });
builder.Services.AddSingleton<Mda.Api.Series.ServicioTVMaze>();
// avisos de luz para fotografiar: hora dorada y noches de Vía Láctea
builder.Services.AddSingleton<ServicioLuz>();
builder.Services.AddSingleton<ServicioAurora>();
builder.Services.AddSingleton<ServicioIss>();
builder.Services.AddSingleton<ServicioNiebla>();
builder.Services.AddSingleton<Mda.Api.Gastos.ServicioDivisas>();
builder.Services.AddSingleton<Mda.Api.Gastos.InformeMensual>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Mda.Api.Gastos.InformeMensual>());
builder.Services.AddSingleton<AvisosLuz>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AvisosLuz>());
builder.Services.AddSingleton<IAvisos, Avisos>();
builder.Services.AddSingleton<ServicioConexiones>();
builder.Services.AddSingleton(Zona.Desde(builder.Configuration));
builder.Services.AddSingleton<GoogleCalendar>();
builder.Services.AddSingleton<Recordatorios>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Recordatorios>());
// resumen semanal, mantenimiento del coche y copias de seguridad
builder.Services.AddSingleton<Vigilante>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Vigilante>());
// qué hizo cada vigilancia por última vez y aviso si la API da muchos errores 5xx
builder.Services.AddSingleton<RegistroVigilancia>();
builder.Services.AddHostedService<AlertaErrores>();
// «si pasa X, haz Y»: reglas generales (disco, gasto del mes, tareas vencidas…) que avisan o crean una tarea
builder.Services.AddSingleton<MotorAutomatizaciones>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MotorAutomatizaciones>());
// páginas y servicios que vigilas (tu web, el router, el NAS): avisa si caen y cuando vuelven
builder.Services.AddSingleton<IComprobadorServicios, ComprobadorReal>();
builder.Services.AddSingleton<MotorServicios>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MotorServicios>());
// escenas que se ponen solas según el contexto (llegar o salir de casa, la hora, el tiempo)
builder.Services.AddHostedService<Mda.Api.Conexiones.RecolectorMusica>();
builder.Services.AddSingleton<MotorContexto>();
// lo que va cayendo en la carpeta «Bandeja» del PC (la vigila el agente)
builder.Services.AddSingleton<Mda.Api.Pc.BandejaPc>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MotorContexto>());


var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
// cuenta los errores del servidor (5xx, también las excepciones sin atrapar) para avisar si se acumulan
app.Use(async (ctx, next) =>
{
    var registro = ctx.RequestServices.GetRequiredService<RegistroVigilancia>();
    try { await next(); }
    catch { registro.Error5xx(); throw; }
    if (ctx.Response.StatusCode >= 500) registro.Error5xx();
});
app.UseCabecerasSeguras();
app.UseSondas();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = CacheFront.Poner });
// el index.html que sirve la ruta comodín del front tampoco se guarda en caché (así un despliegue nuevo se ve al recargar)
app.Use((ctx, next) =>
{
    var ruta = ctx.Request.Path.Value ?? "";
    if (!ruta.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !ruta.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase) && !Path.HasExtension(ruta))
        ctx.Response.OnStarting(() => { if (!ctx.Response.Headers.ContainsKey("Cache-Control")) ctx.Response.Headers.CacheControl = "no-cache"; return Task.CompletedTask; });
    return next();
});
// el enrutado va después de los archivos: si no, la ruta comodín del front se quedaría con los .js y .css
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UsePanico();
app.UseRegistroAccesos();
app.UseRegistroLlamadas();
app.UseMensajeAtajos();
app.UseAuthentication();
app.UseAuthorization();
app.UseBloqueoSesion();

app.MapGet("/api/salud", () => Results.Ok(new { estado = "ok", hora = DateTimeOffset.UtcNow })).AllowAnonymous().RequireCors(WebPublica);
app.MapAuth();
app.MapPasskeys();
app.MapReautenticar();
app.MapPanico();
app.MapBloqueo();
app.MapSistema();
app.MapGastos();
app.MapArchivos();
app.MapGaleria();
app.MapMapaFotos();
app.MapSenuelos();
app.MapAtajos();
app.MapPc();
app.MapCasa();
app.MapRed();
app.MapPortapapeles();
app.MapEstadoPublico();
app.MapDeudas();
app.MapNotas();
app.MapAgenda();
app.MapConexiones();
app.MapMusica();
app.MapSeguridad();
app.MapLlamadasExternas();
app.MapSecretos();
app.MapPin();
app.MapBateria();
app.MapEscenas();
Mda.Api.Domotica.CasaEndpoints.MapDomotica(app);
Mda.Api.Domotica.ReglasEndpoints.MapReglasCasa(app);
Mda.Api.Domotica.EscenasCasa.MapEscenasCasa(app);
app.MapVelocidad();
app.MapEspacio();
Mda.Api.Pc.AppsPcEndpoints.MapAppsPc(app);
Mda.Api.Pc.Transferencias.MapTransferencias(app);
Mda.Api.Sistema.SaludApp.MapSaludApp(app);
Mda.Api.Conexiones.HistorialMusicaEndpoints.MapHistorialMusica(app);
Mda.Api.Conexiones.LetrasEndpoints.MapLetras(app);
Mda.Api.Conexiones.Momentos.MapMomentos(app);
Mda.Api.Personas.PersonasEndpoints.MapPersonas(app);
Mda.Api.Cocina.CocinaEndpoints.MapCocina(app);
Mda.Api.Series.SeriesEndpoints.MapSeries(app);
Mda.Api.Bienestar.BienestarEndpoints.MapBienestar(app);
Mda.Api.Inicio.DisenoInicioEndpoints.MapDisenoInicio(app);
app.MapHogar();
app.MapCartera();
app.MapCobro();
app.MapCarburantes();
app.MapCoche();
app.MapPush();
app.MapPerfil();
app.MapAsistente();
app.MapBuscar();
app.MapBuscarTexto();
app.MapHabitos();
app.MapTareas();
app.MapPapeles();
app.MapGarantias();
Mda.Api.Domotica.ClimaCasa.MapClima(app);
Mda.Api.Contexto.SalidaCasa.MapSalidaCasa(app);
Mda.Api.Tiempo.Ropa.MapRopa(app);
Mda.Api.Contexto.Dormir.MapDormir(app);
Mda.Api.Seguridad.EquiposConfianza.MapEquiposConfianza(app);
app.MapAutomatizaciones();
app.MapServicios();
app.MapCompra();
app.MapContexto();
app.MapBandeja();
app.MapVigilancia();
app.MapHoy();
app.MapCapturas();
app.MapUbicacionIphone();
app.MapTelegram();
app.MapTiempo();
app.MapLuz();
app.MapPuntos();
app.MapLugares();
app.MapReglas();
app.MapExtractos();
app.MapDivisas();
app.MapInicioResumen();
app.MapLinea();
app.MapSondas();
app.MapInforme();

// el front de Angular: cualquier ruta que no sea de la API ni de SignalR devuelve index.html
app.MapFallbackToFile("{*ruta:regex(^(?!api/|hubs/).*$)}", "index.html");

app.Run();

// para WebApplicationFactory en los tests
public partial class Program;
