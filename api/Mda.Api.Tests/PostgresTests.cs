using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Auth;
using Mda.Api.Data;
using Mda.Api.Gastos;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Mda.Api.Tests;

// La API contra un PostgreSQL de verdad (un contenedor que arranca y se borra solo). Lo que la base en memoria no puede comprobar:
// que las migraciones se aplican de cero, que los límites de columna se cumplen, el redondeo del dinero y los passkeys de Identity.
// Hace falta Docker; sin él estas pruebas se saltan (en la integración continua siempre lo hay).
public class PostgresFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public bool Disponible { get; private set; }

    public async Task InitializeAsync()
    {
        try { await _db.StartAsync(); Disponible = true; }
        catch (Exception e) when (e is not OperationCanceledException) { Console.WriteLine("Sin Docker: se saltan las pruebas con PostgreSQL (" + e.GetType().Name + ")"); }
    }

    Task IAsyncLifetime.DisposeAsync() => _db.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Admin:Usuario", ApiFactory.Usuario);
        builder.UseSetting("Admin:Email", ApiFactory.Email);
        builder.UseSetting("Admin:Password", ApiFactory.Password);
        builder.UseSetting("Seq:Url", "");
        builder.UseSetting("App:Zona", "Europe/Madrid");
        if (Disponible) builder.UseSetting("ConnectionStrings:Postgres", _db.GetConnectionString());
    }

    public async Task<HttpClient> Dentro(string agente = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1")
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        c.DefaultRequestHeaders.UserAgent.ParseAdd(agente);
        (await c.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = ApiFactory.Usuario, password = ApiFactory.Password })).EnsureSuccessStatusCode();
        return c;
    }
}

public class PostgresTests(PostgresFactory f) : IClassFixture<PostgresFactory>
{
    [Fact]
    public async Task Las_migraciones_se_aplican_de_cero_y_el_modelo_no_tiene_cambios_pendientes()
    {
        if (!f.Disponible) return;
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        // si alguien cambia una entidad y se olvida de crear la migración, esto falla
        Assert.False(db.Database.HasPendingModelChanges(), "Hay cambios en el modelo sin migración: ejecuta «dotnet ef migrations add».");
    }

    [Fact]
    public async Task Se_entra_y_el_acceso_con_ip_ipv6_y_agente_largo_se_guarda()
    {
        if (!f.Disponible) return;
        var largo = "Mozilla/5.0 (X11; Linux x86_64) " + new string('x', 600);
        var c = await f.Dentro(largo);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sistema")).StatusCode);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Accesos.Add(new Acceso { Ip = "2001:0db8:85a3:0000:0000:8a2e:0370:7334", Agente = new string('a', 400), Exito = true, Motivo = "ok" });
        await db.SaveChangesAsync();
        Assert.True(await db.Accesos.AnyAsync(a => a.Ip.StartsWith("2001:0db8")));
    }

    [Fact]
    public async Task El_dinero_se_guarda_con_dos_decimales_y_el_total_cuadra()
    {
        if (!f.Disponible) return;
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Movimientos.AddRange(
            new Movimiento { Tipo = TipoMovimiento.Gasto, Fecha = new DateOnly(2026, 11, 1), Importe = 10.005m, Categoria = "comida", Concepto = "uno" },
            new Movimiento { Tipo = TipoMovimiento.Gasto, Fecha = new DateOnly(2026, 11, 1), Importe = 0.1m + 0.2m, Categoria = "comida", Concepto = "dos" });
        await db.SaveChangesAsync();
        var guardados = await db.Movimientos.AsNoTracking().Where(m => m.Categoria == "comida").OrderBy(m => m.Concepto).Select(m => m.Importe).ToListAsync();
        Assert.Equal([0.30m, 10.01m], guardados); // numeric(12,2) redondea, la memoria no
    }

    [Fact]
    public async Task Las_notas_y_el_portapapeles_se_guardan_cifrados_y_la_api_los_devuelve_en_claro()
    {
        if (!f.Disponible) return;
        var c = await f.Dentro();
        var nota = await (await c.PostAsJsonAsync("/api/notas", new { titulo = "Claves del wifi", contenido = "contraseña-muy-secreta-123", color = "", etiquetas = new[] { "casa" } })).Content.ReadFromJsonAsync<JsonElement>();
        var id = nota.GetProperty("id").GetInt32();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var crudo = await db.Database.SqlQueryRaw<string>("SELECT \"Contenido\" AS \"Value\" FROM notas WHERE \"Id\" = {0}", id).SingleAsync();
        Assert.StartsWith(Boveda.Prefijo, crudo);
        Assert.DoesNotContain("secreta", crudo);
        var leida = (await c.GetFromJsonAsync<JsonElement>("/api/notas")).EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal("contraseña-muy-secreta-123", leida.GetProperty("contenido").GetString());
        // la búsqueda sigue funcionando aunque el texto esté cifrado en la base
        var hallada = await c.GetFromJsonAsync<JsonElement>("/api/notas?q=SECRETA");
        Assert.Contains(hallada.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);

        Assert.True((await c.PostAsJsonAsync("/api/portapapeles", new { texto = "token-que-no-debe-verse" })).IsSuccessStatusCode);
        var clip = await db.Database.SqlQueryRaw<string>("SELECT \"Texto\" AS \"Value\" FROM portapapeles ORDER BY \"Id\" DESC LIMIT 1").SingleAsync();
        Assert.StartsWith(Boveda.Prefijo, clip);
        Assert.DoesNotContain("token-que", clip);
    }

    [Fact]
    public async Task Los_passkeys_se_guardan_se_renombran_y_se_quitan()
    {
        if (!f.Disponible) return;
        string id;
        using (var scope = f.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var u = (await users.FindByNameAsync(ApiFactory.Usuario))!;
            var cred = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var pk = new UserPasskeyInfo(cred, [1, 2, 3], DateTimeOffset.UtcNow, 0, ["internal"], true, true, true, [4], [5]) { Name = "iPhone de Mario" };
            Assert.True((await users.AddOrUpdatePasskeyAsync(u, pk)).Succeeded);
            id = PasskeyInfos.IdDe(cred);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PasskeysInfo.Add(new PasskeyInfo { Id = id, UsuarioId = u.Id, AltaEn = DateTimeOffset.UtcNow, AltaIp = "203.0.113.7", Dispositivo = "iPhone · Safari", Tipo = "movil" });
            await db.SaveChangesAsync();
        }
        var c = await f.Dentro();
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/passkeys");
        var p = lista.EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        Assert.Equal("iPhone de Mario", p.GetProperty("nombre").GetString());
        Assert.True(p.GetProperty("sincronizada").GetBoolean());
        Assert.Equal("203.0.113.7", p.GetProperty("alta").GetProperty("ip").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync($"/api/passkeys/{id}", new { nombre = "Portátil del salón" })).StatusCode);
        var otra = await c.GetFromJsonAsync<JsonElement>("/api/passkeys");
        Assert.Contains(otra.EnumerateArray(), x => x.GetProperty("nombre").GetString() == "Portátil del salón");

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/passkeys/{id}")).StatusCode);
        using var scope2 = f.Services.CreateScope();
        Assert.Null(await scope2.ServiceProvider.GetRequiredService<AppDbContext>().PasskeysInfo.FindAsync(id));
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/passkeys/{id}")).StatusCode);
    }

    [Fact]
    public async Task Dos_aportaciones_a_la_vez_a_una_hucha_no_se_pisan()
    {
        if (!f.Disponible) return;
        var c = await f.Dentro();
        var hucha = await (await c.PostAsJsonAsync("/api/gastos/huchas", new { nombre = "Viaje", objetivo = 1000m, color = "#fff", emoji = "✈️" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = hucha.GetProperty("id").GetInt32();
        var tareas = Enumerable.Range(0, 10).Select(_ => c.PostAsJsonAsync($"/api/gastos/huchas/{id}/aportar", new { importe = 10m })).ToArray();
        await Task.WhenAll(tareas);
        var ok = tareas.Count(t => t.Result.IsSuccessStatusCode);
        using var scope = f.Services.CreateScope();
        var ahorrado = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Huchas.AsNoTracking().SingleAsync(h => h.Id == id)).Ahorrado;
        Assert.Equal(ok * 10m, ahorrado); // cada aportación que dijo «bien» está sumada, ninguna se perdió
    }
}
