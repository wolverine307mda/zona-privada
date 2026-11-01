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
});

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
    o.Cookie.Name = "mda.sesion";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    // en producción la cookie solo viaja por HTTPS (el proxy, Tailscale Funnel, avisa con X-Forwarded-Proto)
    o.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
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
    .WithOrigins(builder.Configuration.GetSection("WebPublica:Origenes").Get<string[]>() ?? ["https://wolverine307mda.github.io"])
    .WithMethods("GET")));

// ---- Detrás de Tailscale Funnel: respetar el esquema y la IP originales ----
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // la API solo es accesible desde la red interna de Docker (tailscale) y desde 127.0.0.1, así que se confía en el proxy.
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
builder.Services.AddSingleton<Sondas>();
builder.Services.AddSingleton<IComprobadorRed, ComprobadorTcp>();
builder.Services.AddHostedService<ResumenSondas>();
builder.Services.AddHostedService<LatidoExterno>();
builder.Services.AddSingleton<Pregenerar>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Pregenerar>());
builder.Services.AddResponseCompression(o => { o.EnableForHttps = true; o.MimeTypes = ["application/json"]; });


// ---- Avisos (correo, WhatsApp), cuentas enlazadas (Spotify, Google) y agenda con recordatorios ----
builder.Services.AddMemoryCache();
// sin registros de cada petición: la URL de CallMeBot lleva la apikey
builder.Services.AddHttpClient("avisos", c => c.Timeout = TimeSpan.FromSeconds(20)).RemoveAllLoggers();
builder.Services.AddHttpClient("conexiones", c => c.Timeout = TimeSpan.FromSeconds(15));
// sin registros de cada petición: el token del bot va en la propia URL y acabaría en los logs y en Seq
builder.Services.AddHttpClient("telegram", c => c.Timeout = TimeSpan.FromSeconds(20)).RemoveAllLoggers();
builder.Services.AddHttpClient("telegram-largo", c => c.Timeout = TimeSpan.FromSeconds(75)).RemoveAllLoggers(); // getUpdates espera hasta 50 s
builder.Services.AddHttpClient("tiempo", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<Push>();
// bot de Telegram: avisos y hablar con el asistente desde el chat
builder.Services.AddSingleton<Bot>();
builder.Services.AddScoped<Conversacion>();
builder.Services.AddHostedService<Sondeo>();
// el tiempo (Open-Meteo)
builder.Services.AddSingleton<ServicioTiempo>();
// avisos de luz para fotografiar: hora dorada y noches de Vía Láctea
builder.Services.AddSingleton<ServicioLuz>();
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


var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseCabecerasSeguras();
app.UseSondas();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();
// el enrutado va después de los archivos: si no, la ruta comodín del front se quedaría con los .js y .css
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UsePanico();
app.UseRegistroAccesos();
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
app.MapCartera();
app.MapCoche();
app.MapPush();
app.MapPerfil();
app.MapAsistente();
app.MapTelegram();
app.MapTiempo();
app.MapLuz();

// el front de Angular: cualquier ruta que no sea de la API ni de SignalR devuelve index.html
app.MapFallbackToFile("{*ruta:regex(^(?!api/|hubs/).*$)}", "index.html");

app.Run();

// para WebApplicationFactory en los tests
public partial class Program;
