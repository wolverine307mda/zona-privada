// API de la zona privada: inicio de sesión con cookie (ASP.NET Core Identity), PostgreSQL con EF Core,
// estado del servidor en tiempo real con SignalR y, en producción, el front de Angular servido desde wwwroot.
using System.Threading.RateLimiting;
using Mda.Api.Auth;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Mda.Api.Sistema;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

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
        o.Password.RequiredLength = 12;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "mda.sesion";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    // en producción la cookie solo viaja por HTTPS (Cloudflare Tunnel avisa con X-Forwarded-Proto)
    o.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    // una API no redirige a una página de login: responde 401 / 403
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});

// ---- Límite de intentos de inicio de sesión por IP ----
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(Limites.Acceso, ctx => RateLimitPartition.GetFixedWindowLimiter(
        IpCliente.De(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// ---- Detrás de Cloudflare Tunnel: respetar el esquema y la IP originales ----
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // la API solo es accesible desde la red interna de Docker (cloudflared), así que se confía en el proxy
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// ---- Estado del servidor en tiempo real ----
builder.Services.AddSignalR();
builder.Services.AddSingleton<ILectorSistema>(_ => LectorSistema.ParaEsteEquipo());
builder.Services.AddSingleton<EstadoSistema>();
builder.Services.AddHostedService<MonitorSistema>();

// ---- Al arrancar: migraciones y usuario administrador desde la configuración ----
builder.Services.AddHostedService<SemillaAdmin>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseCabecerasSeguras();
app.UseDefaultFiles();
app.UseStaticFiles();
// el enrutado va después de los archivos: si no, la ruta comodín del front se quedaría con los .js y .css
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/salud", () => Results.Ok(new { estado = "ok", hora = DateTimeOffset.UtcNow })).AllowAnonymous();
app.MapAuth();
app.MapSistema();

// el front de Angular: cualquier ruta que no sea de la API ni de SignalR devuelve index.html
app.MapFallbackToFile("{*ruta:regex(^(?!api/|hubs/).*$)}", "index.html");

app.Run();

// para WebApplicationFactory en los tests
public partial class Program;
