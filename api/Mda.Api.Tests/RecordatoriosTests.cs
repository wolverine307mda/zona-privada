using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

// un reloj que se mueve a mano
public class RelojPrueba(DateTimeOffset inicio) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = inicio;
    public override DateTimeOffset GetUtcNow() => Ahora;
}

// avisos que se apuntan y que fallan por canal cuando se les dice
public class AvisosControlados : IAvisos
{
    public List<string> Enviados { get; } = [];
    public HashSet<string> Rotos { get; } = [];
    public Canales Disponibles() => new(true, true, true, true);
    private Task<bool> Va(string canal, string texto) { if (Rotos.Contains(canal)) return Task.FromResult(false); lock (Enviados) Enviados.Add($"{canal}:{texto}"); return Task.FromResult(true); }
    public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Va("correo", asunto);
    public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) => Va("whatsapp", texto);
    public Task<bool> PorTelegram(string texto, CancellationToken ct = default) => Va("telegram", texto);
    public Task<bool> PorPush(string titulo, string texto, string? url = null, CancellationToken ct = default) => Va("push", titulo);
    public int Veces(string canal) { lock (Enviados) return Enviados.Count(x => x.StartsWith(canal + ":")); }
}

public class RecordatoriosTests(ModulosFactory f) : IClassFixture<ModulosFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // el aviso toca «minutosHastaAviso» minutos después de ahora (el evento empieza 10 minutos más tarde)
    private async Task<(HttpClient C, int Id, DateTimeOffset Ahora)> Crear(string titulo, int minutosHastaAviso, bool correo, bool whatsapp)
    {
        var c = await f.Dentro();
        var ahora = DateTimeOffset.UtcNow;
        var r = await c.PostAsJsonAsync("/api/agenda", new
        {
            tipo = "Recordatorio", titulo, inicio = ahora.AddMinutes(minutosHastaAviso + 10), todoElDia = false, repetir = "No", avisoMinutos = 10,
            porCorreo = correo, porWhatsApp = whatsapp, porTelegram = false, enGoogle = false,
        }, Web);
        r.EnsureSuccessStatusCode();
        return (c, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32(), ahora);
    }

    private (Recordatorios Rec, RelojPrueba Reloj, AvisosControlados Avisos) Servicio(DateTimeOffset ahora)
    {
        var reloj = new RelojPrueba(ahora);
        var avisos = new AvisosControlados();
        return (new Recordatorios(f.Services.GetRequiredService<IServiceScopeFactory>(), avisos, f.Services.GetRequiredService<Zona>(), reloj, NullLogger<Recordatorios>.Instance), reloj, avisos);
    }

    private async Task<Evento> Leer(int id)
    {
        using var scope = f.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Eventos.FindAsync(id))!;
    }

    [Fact]
    public async Task El_whatsapp_sale_cinco_minutos_antes_y_el_resto_a_la_hora()
    {
        var (_, id, ahora) = await Crear("Dentista", 3, correo: true, whatsapp: true); // el aviso toca dentro de 3 minutos
        var (rec, reloj, avisos) = Servicio(ahora);

        await rec.Revisar(default);
        Assert.Equal(1, avisos.Veces("whatsapp"));   // ya: falta menos de 5 minutos
        Assert.Equal(0, avisos.Veces("correo"));     // el correo espera a la hora

        reloj.Ahora = ahora.AddMinutes(4);           // pasó la hora del aviso
        await rec.Revisar(default);
        Assert.Equal(1, avisos.Veces("correo"));
        Assert.Equal(1, avisos.Veces("whatsapp"));   // el WhatsApp no se repite
        Assert.Null((await Leer(id)).ProximoAviso);  // nada más que avisar

        await rec.Revisar(default);
        Assert.Equal(2, avisos.Enviados.Count);
    }

    [Fact]
    public async Task Un_canal_que_falla_se_reintenta_con_espera_y_los_que_llegaron_no_se_repiten()
    {
        var (_, id, ahora) = await Crear("Pagar luz", -1, correo: true, whatsapp: true); // ya tocaba
        var (rec, reloj, avisos) = Servicio(ahora.AddSeconds(30));
        avisos.Rotos.Add("whatsapp");

        await rec.Revisar(default);
        Assert.Equal(1, avisos.Veces("correo"));
        Assert.Equal(1, (await Leer(id)).Intentos);

        await rec.Revisar(default);                  // sin esperar: no hay reintento
        Assert.Equal(1, avisos.Veces("correo"));
        Assert.Equal(1, (await Leer(id)).Intentos);

        avisos.Rotos.Clear();
        reloj.Ahora = ahora.AddMinutes(2);           // pasó la espera de 1 minuto: ahora el WhatsApp sí llega
        await rec.Revisar(default);
        Assert.Equal(1, avisos.Veces("whatsapp"));
        Assert.Equal(1, avisos.Veces("correo"));     // el correo, una sola vez
        Assert.Null((await Leer(id)).ProximoAviso);
    }

    [Fact]
    public async Task Si_no_llega_por_ningun_canal_te_lo_cuenta_por_telegram_tras_los_reintentos()
    {
        var (_, id, ahora) = await Crear("Cita", -1, correo: true, whatsapp: false);
        var (rec, reloj, avisos) = Servicio(ahora.AddSeconds(30));
        avisos.Rotos.Add("correo");

        for (var i = 0; i < 4; i++) { await rec.Revisar(default); reloj.Ahora = reloj.Ahora.AddMinutes(20); }

        Assert.Contains(avisos.Enviados, x => x.StartsWith("telegram:⚠️ No pude avisarte de «Cita»"));
        Assert.Null((await Leer(id)).ProximoAviso);
    }

    [Fact]
    public async Task Editar_un_recordatorio_ya_avisado_no_vuelve_a_avisar()
    {
        var (c, id, ahora) = await Crear("Llamar", -1, correo: true, whatsapp: false);
        var (rec, _, avisos) = Servicio(ahora.AddSeconds(30));
        await rec.Revisar(default);
        Assert.Equal(1, avisos.Veces("correo"));

        // misma hora de inicio que antes: solo cambia el título
        var original = await Leer(id);
        var cuerpo = new
        {
            tipo = "Recordatorio", titulo = "Llamar al banco", inicio = original.Inicio, todoElDia = false, repetir = "No", avisoMinutos = 10,
            porCorreo = true, porWhatsApp = false, porTelegram = false, enGoogle = false,
        };
        Assert.True((await c.PutAsJsonAsync($"/api/agenda/{id}", cuerpo, Web)).IsSuccessStatusCode);
        Assert.Null((await Leer(id)).ProximoAviso);
        await rec.Revisar(default);
        Assert.Equal(1, avisos.Veces("correo"));
    }

    [Fact]
    public async Task Un_aviso_con_mas_de_doce_horas_de_retraso_se_salta_y_se_cuenta()
    {
        var (_, id, ahora) = await Crear("Vieja", 60, correo: true, whatsapp: false);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Eventos.FindAsync(id))!.ProximoAviso = ahora.AddHours(-13);
            await db.SaveChangesAsync();
        }
        var (rec, _, avisos) = Servicio(ahora);
        await rec.Revisar(default);
        Assert.Equal(0, avisos.Veces("correo"));
        Assert.Contains(avisos.Enviados, x => x.StartsWith("telegram:⚠️ Te perdiste el aviso de «Vieja»"));
    }
}
