using Mda.Api.Avisos;

namespace Mda.Api.Tests;

public class TiposAvisoTests
{
    [Fact]
    public void Sin_preferencia_guardada_salen_los_canales_de_siempre()
    {
        var t = TiposAviso.Buscar("cartera")!;
        Assert.Equal(t.PorDefecto, TiposAviso.Interpretar(null, t));
    }

    [Fact]
    public void Una_preferencia_vacia_apaga_ese_tipo_de_aviso()
    {
        Assert.Empty(TiposAviso.Interpretar("", TiposAviso.Buscar("pc")!));
    }

    [Fact]
    public void Se_ignoran_canales_desconocidos_y_repetidos()
    {
        Assert.Equal(["telegram", "correo"], TiposAviso.Interpretar("telegram, correo,telegram,sms", TiposAviso.Buscar("pc")!));
        Assert.Equal("push,correo", TiposAviso.Guardar(["push", "radio", "correo", "push"]));
    }

    [Fact]
    public void Cada_tipo_lleva_a_una_pantalla_de_la_app_y_los_ids_no_se_repiten()
    {
        Assert.All(TiposAviso.Todos, t => Assert.StartsWith("/", t.Ruta));
        Assert.Equal(TiposAviso.Todos.Length, TiposAviso.Todos.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public async Task El_respaldo_de_Avisar_reparte_por_los_canales_de_siempre_con_la_ruta_del_tipo()
    {
        var a = new Registro();
        await ((IAvisos)a).Avisar("coche", "🚗 ITV", "Toca el 3 de enero");
        Assert.Equal(["push:/gastos/deudas", "telegram", "correo:/gastos/deudas"], a.Llamadas);
    }

    private sealed class Registro : IAvisos
    {
        public List<string> Llamadas { get; } = [];
        public Canales Disponibles() => new(true, true);
        public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) { Llamadas.Add("correo"); return Task.FromResult(true); }
        public Task<bool> PorCorreo(string asunto, string texto, string ruta, CancellationToken ct = default) { Llamadas.Add("correo:" + ruta); return Task.FromResult(true); }
        public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) { Llamadas.Add("whatsapp"); return Task.FromResult(true); }
        public Task<bool> PorTelegram(string texto, CancellationToken ct = default) { Llamadas.Add("telegram"); return Task.FromResult(true); }
        public Task<bool> PorPush(string titulo, string texto, string? url = null, CancellationToken ct = default) { Llamadas.Add("push:" + url); return Task.FromResult(true); }
    }
}
