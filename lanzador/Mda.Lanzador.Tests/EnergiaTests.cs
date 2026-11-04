namespace Mda.Lanzador.Tests;

public class EnergiaTests
{
    [Theory]
    [InlineData("equilibrado", "381b4222-f694-41f0-9685-ff5bb260df2e")]
    [InlineData("cine", "381b4222-f694-41f0-9685-ff5bb260df2e")]   // el cine es el equilibrado con el PC despierto
    [InlineData("ahorro", "a1841308-3541-4fab-bc81-f71556f20b4a")]
    [InlineData("rendimiento", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")]
    public void Cada_perfil_tiene_su_plan_de_windows(string perfil, string guid) => Assert.Equal(guid, Energia.Plan(perfil));

    [Theory]
    [InlineData("")] [InlineData("turbo")] [InlineData("381b4222-f694-41f0-9685-ff5bb260df2e")] [InlineData("; shutdown /s")]
    public void Lo_que_no_es_un_perfil_conocido_no_llega_a_powercfg(string perfil) => Assert.Null(Energia.Plan(perfil));

    [Fact]
    public void Los_planes_son_guids_validos() =>
        Assert.All(new[] { Energia.Equilibrado, Energia.Ahorro, Energia.AltoRendimiento }, g => Assert.True(Guid.TryParse(g, out _)));
}
