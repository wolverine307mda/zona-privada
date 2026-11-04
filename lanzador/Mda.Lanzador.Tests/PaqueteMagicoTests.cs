namespace Mda.Lanzador.Tests;

public class PaqueteMagicoTests
{
    [Fact]
    public void El_paquete_son_seis_ff_y_la_mac_dieciseis_veces()
    {
        var p = PaqueteMagico.De("AA:BB:CC:00:11:22")!;
        Assert.Equal(102, p.Length);
        Assert.All(p[..6], b => Assert.Equal(0xFF, b));
        for (var i = 0; i < 16; i++) Assert.Equal([0xAA, 0xBB, 0xCC, 0x00, 0x11, 0x22], p[(6 + i * 6)..(12 + i * 6)]);
    }

    [Theory]
    [InlineData("")] [InlineData("AA:BB:CC:00:11")] [InlineData("AA:BB:CC:00:11:2")] [InlineData("AA:BB:CC:00:11:ZZ")] [InlineData("AA-BB-CC-00-11-22")] [InlineData("AA:BB:CC:00:11:22:33")]
    public void Una_mac_mal_escrita_no_da_paquete(string mac) => Assert.Null(PaqueteMagico.De(mac));

    [Fact]
    public void La_difusion_pone_a_uno_los_bits_de_host()
    {
        Assert.Equal([192, 168, 1, 255], PaqueteMagico.Difusion([192, 168, 1, 37], [255, 255, 255, 0]));
        Assert.Equal([10, 255, 255, 255], PaqueteMagico.Difusion([10, 3, 4, 5], [255, 0, 0, 0]));
        Assert.Equal([172, 16, 7, 255], PaqueteMagico.Difusion([172, 16, 7, 9], [255, 255, 255, 0]));
    }
}
