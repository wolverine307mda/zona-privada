using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Http;

namespace Mda.Api.Tests;

public class TarpitTests
{
    private static DefaultHttpContext Contexto(CancellationToken ct = default)
    {
        var ctx = new DefaultHttpContext { RequestAborted = ct };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task Contesta_404_y_va_soltando_bytes_hasta_que_se_acaba_el_tiempo()
    {
        var tarpit = new Tarpit(TimeProvider.System);
        var ctx = Contexto();
        Assert.True(await tarpit.Gotear(ctx, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(200)));
        Assert.Equal(404, ctx.Response.StatusCode);
        Assert.True(ctx.Response.Body.Length >= 3);
        Assert.Equal(0, tarpit.Activas);
    }

    [Fact]
    public async Task Si_el_escaner_cuelga_termina_sin_esperar_y_libera_el_hueco()
    {
        var tarpit = new Tarpit(TimeProvider.System);
        using var cts = new CancellationTokenSource(80);
        var t = tarpit.Gotear(Contexto(cts.Token), TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(30));
        Assert.True(await t.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, tarpit.Activas);
    }

    [Fact]
    public async Task Con_el_tope_lleno_no_atiende_y_deja_el_404_normal()
    {
        var tarpit = new Tarpit(TimeProvider.System, maximo: 1);
        var primera = tarpit.Gotear(Contexto(), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(400));
        await Task.Delay(100);
        Assert.Equal(1, tarpit.Activas);
        Assert.False(await tarpit.Gotear(Contexto()));
        Assert.True(await primera);
        Assert.Equal(0, tarpit.Activas);
    }
}
