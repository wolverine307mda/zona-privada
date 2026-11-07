using Xunit;

namespace Mda.Lanzador.Tests;

public class UbicacionTests
{
    [Theory]
    [InlineData("estado", "estado", 0, 0, 0, "")]
    [InlineData("parar", "parar", 0, 0, 0, "")]
    [InlineData("fijar:40.41683,-3.70379:120", "fijar", 40.41683, -3.70379, 120, "")]
    [InlineData("fijar:-33.8688,151.2093:1", "fijar", -33.8688, 151.2093, 1, "")]
    [InlineData("ir:48.8584,2.2945:60:bici", "ir", 48.8584, 2.2945, 60, "bici")]
    [InlineData("fijar:0,0:720", "fijar", 0, 0, 720, "")]
    public void Lee_las_ordenes_bien_formadas(string p, string accion, double lat, double lon, int min, string modo)
    {
        var r = UbicacionLogica.Leer(p);
        Assert.NotNull(r);
        Assert.Equal((accion, lat, lon, min, modo), (r!.Accion, r.Lat, r.Lon, r.Minutos, r.Modo));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("fijar:91,0:60")]            // latitud fuera de rango
    [InlineData("fijar:0,181:60")]           // longitud fuera de rango
    [InlineData("fijar:40,-3:0")]            // sin tiempo
    [InlineData("fijar:40,-3:721")]          // más de 12 h
    [InlineData("fijar:40,-3")]              // falta el tiempo
    [InlineData("fijar:40,-3:60:extra")]     // sobra algo
    [InlineData("ir:40,-3:60")]              // falta el modo
    [InlineData("ir:40,-3:60:avion")]        // modo que no existe
    [InlineData("fijar:NaN,0:60")]
    [InlineData("fijar:1e2,0:60")]           // exponentes no
    [InlineData("fijar:40.1234567,0:60")]    // más de 6 decimales
    [InlineData("fijar:40, -3:60")]          // espacios
    [InlineData("fijar:40,-3:60; calc")]     // intento de colar otra cosa
    [InlineData("borrar")]
    public void Rechaza_todo_lo_demas(string? p) => Assert.Null(UbicacionLogica.Leer(p));

    [Fact]
    public void Distancia_entre_madrid_y_barcelona_ronda_los_500_km()
    {
        var m = UbicacionLogica.Distancia(40.4168, -3.7038, 41.3874, 2.1686);
        Assert.InRange(m / 1000, 495, 510);
    }

    [Fact]
    public void Interpolar_llega_a_los_extremos_y_no_se_pasa()
    {
        Assert.Equal((10.0, 20.0), UbicacionLogica.Interpolar(10, 20, 30, 40, 0));
        Assert.Equal((30.0, 40.0), UbicacionLogica.Interpolar(10, 20, 30, 40, 1));
        Assert.Equal((30.0, 40.0), UbicacionLogica.Interpolar(10, 20, 30, 40, 7));
        var (la, lo) = UbicacionLogica.Interpolar(10, 20, 30, 40, 0.5);
        Assert.Equal((20.0, 30.0), (la, lo));
    }

    [Fact]
    public void Interpolar_cruza_la_linea_del_cambio_de_fecha_por_el_lado_corto()
    {
        var (_, lo) = UbicacionLogica.Interpolar(0, 179, 0, -179, 0.5);
        Assert.True(Math.Abs(Math.Abs(lo) - 180) < 1e-9);
    }

    [Theory]
    [InlineData(5000, "andar", 3600)]   // 5 km a 5 km/h
    [InlineData(15000, "bici", 3600)]
    [InlineData(50000, "coche", 3600)]
    [InlineData(1, "coche", 1)]         // nunca cero
    public void El_tiempo_sale_de_la_velocidad_del_modo(double metros, string modo, int segundos) =>
        Assert.Equal(segundos, UbicacionLogica.Segundos(metros, modo));

    [Fact]
    public void El_gpx_empieza_y_acaba_donde_debe_y_no_pasa_de_unos_3600_puntos()
    {
        var gpx = UbicacionLogica.Gpx(40.0, -3.0, 40.1, -3.1, 36_000, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        Assert.Contains("lat=\"40.000000\" lon=\"-3.000000\"", gpx);
        Assert.Contains("lat=\"40.100000\" lon=\"-3.100000\"", gpx);
        Assert.Contains("<time>2026-01-01T10:00:00Z</time>", gpx);
        Assert.Contains("<time>2026-01-01T20:00:00Z</time>", gpx);
        Assert.InRange(gpx.Split("<trkpt").Length - 1, 2, 3700);
    }

    [Theory]
    [InlineData("ModuleNotFoundError: No module named 'pymobiledevice3'", "pip install")]
    [InlineData("pymobiledevice3.exceptions.NoDeviceConnectedError", "conéctalo por cable")]
    [InlineData("DeveloperModeIsNotEnabledError: Developer Mode is not enabled", "modo desarrollador")]
    [InlineData("PermissionError: [WinError 5] Access is denied", "administrador")]
    public void Traduce_lo_que_falla_a_algo_que_se_puede_hacer(string salida, string contiene) =>
        Assert.Contains(contiene, UbicacionLogica.Motivo(salida));

    [Fact]
    public void Un_error_desconocido_se_resume_en_su_ultima_linea()
    {
        var t = UbicacionLogica.Motivo("traza\nmás traza\nValueError: algo raro");
        Assert.Contains("ValueError: algo raro", t);
        Assert.DoesNotContain("traza", t.Replace("ValueError", ""));
    }

    [Fact]
    public void Lee_el_nombre_del_iphone_de_la_lista_de_usbmux()
    {
        const string salida = "[\n  {\"Identifier\": \"00008110-0001\", \"DeviceName\": \"iPhone de Mario\", \"ProductVersion\": \"18.1\"}\n]";
        Assert.Equal("iPhone de Mario (iOS 18.1)", UbicacionLogica.Aparato(salida));
        Assert.Null(UbicacionLogica.Aparato("no hay nada"));
        Assert.Null(UbicacionLogica.Aparato("[]"));
    }
}
