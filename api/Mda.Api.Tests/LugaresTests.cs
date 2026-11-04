using Mda.Api.Gastos;

namespace Mda.Api.Tests;

public class LugaresTests
{
    private static Movimiento M(string comercio, decimal importe, double? lat, double? lon, TipoMovimiento tipo = TipoMovimiento.Gasto) =>
        new() { Tipo = tipo, Concepto = comercio, Importe = importe, Lat = lat, Lon = lon, Fecha = new DateOnly(2026, 11, 3) };

    [Fact]
    public void Junta_los_pagos_cercanos_en_una_zona_y_separa_los_lejanos()
    {
        var (zonas, comercios) = Lugares.Agrupar([
            M("Mercadona", 40, 40.4168, -3.7038), M("Mercadona", 25, 40.4172, -3.7041), M("Bar Pepe", 12, 40.4170, -3.7036),
            M("Gasolinera", 60, 40.5000, -3.8000)]);
        Assert.Equal(2, zonas.Count);
        Assert.Equal(77, zonas.First(z => z.Pagos == 3).Total);
        Assert.Equal("Mercadona", zonas.First(z => z.Pagos == 3).Comercios[0].Nombre);
        Assert.Equal(65, comercios[0].Total);          // Mercadona 40 + 25
        Assert.Equal("Mercadona", comercios[0].Nombre);
    }

    [Fact]
    public void Ignora_ingresos_y_pagos_sin_ubicacion()
    {
        var (zonas, comercios) = Lugares.Agrupar([M("Nómina", 1500, 40.4, -3.7, TipoMovimiento.Ingreso), M("Online", 20, null, null)]);
        Assert.Empty(zonas);
        Assert.Empty(comercios);
    }

    [Fact]
    public void Valida_y_redondea_las_coordenadas()
    {
        Assert.True(Lugares.Valida(40.4, -3.7));
        Assert.False(Lugares.Valida(95, 0));
        Assert.False(Lugares.Valida(null, 3));
        Assert.False(Lugares.Valida(0, 0));                 // un 0,0 suele ser «sin señal»
        Assert.Equal(40.417, Lugares.Redondear(40.41684));
    }
}
