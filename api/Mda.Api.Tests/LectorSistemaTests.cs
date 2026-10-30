using Mda.Api.Sistema;

namespace Mda.Api.Tests;

public class LectorSistemaTests
{
    [Fact]
    public void ParsearStat_suma_los_tiempos_y_cuenta_iowait_como_inactivo()
    {
        const string stat = "cpu  100 5 50 800 20 3 2 0 0 0\ncpu0 50 2 25 400 10 1 1 0 0 0\n";

        var (inactivo, total) = LectorLinux.ParsearStat(stat);

        Assert.Equal(820UL, inactivo);   // idle + iowait
        Assert.Equal(980UL, total);      // las ocho primeras columnas
    }

    [Fact]
    public void ParsearMeminfo_devuelve_bytes()
    {
        const string meminfo = "MemTotal:       16000000 kB\nMemFree:         2000000 kB\nMemAvailable:    6000000 kB\n";

        var (total, disponible) = LectorLinux.ParsearMeminfo(meminfo);

        Assert.Equal(16000000L * 1024, total);
        Assert.Equal(6000000L * 1024, disponible);
    }

    [Theory]
    [InlineData(0UL, 0UL, 80UL, 100UL, 0)]        // sin lectura anterior: 0 %
    [InlineData(800UL, 1000UL, 850UL, 1100UL, 50)] // 50 de 100 ticks ocupados
    [InlineData(800UL, 1000UL, 800UL, 1100UL, 100)]
    public void Porcentaje_de_cpu_entre_dos_lecturas(ulong ia, ulong ta, ulong i, ulong t, double esperado)
    {
        Assert.Equal(esperado, LectorSistema.Porcentaje(ia, ta, i, t));
    }
}
