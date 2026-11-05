using Xunit;

namespace Mda.Lanzador.Tests;

public class CapturaTests
{
    [Theory]
    [InlineData("pantalla", true, "pantalla", "manual")]
    [InlineData("camara:intruso", true, "camara", "intruso")]
    [InlineData("camara:panico", true, "camara", "panico")]
    [InlineData("camara-ip", false, "camara-ip", "manual")]
    [InlineData("pantalla:otro", false, "pantalla", "otro")]
    [InlineData("pantalla:manual:mas", false, "pantalla", "manual")]
    [InlineData("", false, "", "manual")]
    public void El_parametro_de_la_orden_se_comprueba(string p, bool valido, string origen, string motivo)
    {
        Assert.Equal(valido, CapturaLogica.Valido(p, out var o, out var m));
        Assert.Equal((origen, motivo), (o, m));
    }

    [Fact]
    public void Lee_las_camaras_de_la_lista_de_ffmpeg_y_no_los_microfonos()
    {
        const string salida = """
            [dshow @ 000001] DirectShow video devices (some may be both video and audio devices)
            [dshow @ 000001]  "Integrated Camera" (video)
            [dshow @ 000001]     Alternative name "@device_pnp_\?\usb#vid_0c45"
            [dshow @ 000001]  "OBS Virtual Camera" (video)
            [dshow @ 000001] DirectShow audio devices
            [dshow @ 000001]  "Microphone (Realtek Audio)" (audio)
            """;
        Assert.Equal(["Integrated Camera", "OBS Virtual Camera"], CapturaLogica.CamarasDe(salida));
        Assert.Empty(CapturaLogica.CamarasDe("Unknown input format: 'dshow'"));
        Assert.Equal(["Cámara HD"], CapturaLogica.CamarasDe("\"Cámara HD\" (video)\n\"Cámara HD\" (video)"));   // sin repetidas
    }

    [Theory]
    [InlineData(3840, 2160, 1280, 720)]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(1280, 720, 1280, 720)]
    [InlineData(800, 600, 800, 600)]
    [InlineData(2160, 3840, 720, 1280)]
    [InlineData(0, 10, 1, 1)]
    public void Reduce_sin_agrandar_y_con_la_misma_proporcion(int w, int h, int ew, int eh) => Assert.Equal((ew, eh), CapturaLogica.Reducir(w, h));
}
