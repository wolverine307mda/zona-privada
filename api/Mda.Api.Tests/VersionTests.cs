using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Sistema;

namespace Mda.Api.Tests;

public class VersionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("1.0.0+abc1234", "abc1234")]
    [InlineData("1.0.0+abc1234-cambios.202611131200", "abc1234-cambios.202611131200")]
    [InlineData("1.0.0", "desconocida")]
    [InlineData("1.0.0+", "desconocida")]
    [InlineData(null, "desconocida")]
    public void Lee_el_commit_de_la_version(string? informacional, string esperada) =>
        Assert.Equal(esperada, VersionApp.Leer(informacional));

    [Fact]
    public async Task Salud_dice_que_version_corre()
    {
        var j = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/salud");
        Assert.Equal("ok", j.GetProperty("estado").GetString());
        Assert.False(string.IsNullOrWhiteSpace(j.GetProperty("version").GetString()));
    }
}
