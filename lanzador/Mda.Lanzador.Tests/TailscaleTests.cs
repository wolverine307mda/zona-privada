using Mda.Lanzador;

namespace Mda.Lanzador.Tests;

public class TailscaleTests
{
    [Fact]
    public void Las_ordenes_apuntan_al_bucle_local_de_la_version_de_escritorio()
    {
        Assert.Equal("serve --bg --https=443 http://127.0.0.1:8090", TailscaleOrdenes.Servir(8090));
        Assert.Equal("funnel --bg --https=443 http://127.0.0.1:8090", TailscaleOrdenes.AbrirAInternet(8090));
        Assert.Equal("funnel --https=443 off", TailscaleOrdenes.CerrarAInternet());
        Assert.Equal("serve --https=443 off", TailscaleOrdenes.PararServir());
    }

    [Fact]
    public void Se_lee_el_nombre_del_equipo_y_si_esta_conectado()
    {
        var e = TailscaleOrdenes.LeerEstado("""{"BackendState":"Running","Self":{"DNSName":"mi-pc.tail1234.ts.net.","Online":true}}""");
        Assert.True(e.Conectado);
        Assert.Equal("mi-pc.tail1234.ts.net", e.Dns);
        Assert.Equal("https://mi-pc.tail1234.ts.net", e.Url);
        Assert.False(TailscaleOrdenes.LeerEstado("""{"BackendState":"NeedsLogin","Self":{"DNSName":""}}""").Conectado);
        var roto = TailscaleOrdenes.LeerEstado("esto no es json");
        Assert.False(roto.Conectado);
        Assert.Null(roto.Url);
    }
}
