using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mda.Api.Tests;

public class CookieSesionTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public void La_cookie_de_sesion_dura_30_dias_y_se_renueva()
    {
        var o = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        Assert.Equal(TimeSpan.FromDays(30), o.ExpireTimeSpan);
        Assert.True(o.SlidingExpiration);
        // no puede durar más que el tope de la sesión apuntada en la base
        Assert.True(o.ExpireTimeSpan <= Sesiones.Maxima);
    }
}
