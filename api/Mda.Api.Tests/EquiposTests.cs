using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Tests;

public class EquiposTests
{
    private const string Iphone15 = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";
    private const string Iphone16 = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1";
    private const string Windows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private static AppDbContext Db() => BaseDeDatos.Nueva();

    [Fact]
    public void Una_actualizacion_del_sistema_no_cambia_la_huella_pero_si_el_pais()
    {
        Assert.Equal(Equipos.Huella(Iphone15, "ES"), Equipos.Huella(Iphone16, "es"));
        Assert.NotEqual(Equipos.Huella(Iphone15, "ES"), Equipos.Huella(Iphone15, "FR"));
        Assert.NotEqual(Equipos.Huella(Iphone15, "ES"), Equipos.Huella(Windows, "ES"));
        Assert.Equal("iPhone · Safari|?", Equipos.Huella(Iphone15, null));
    }

    [Fact]
    public void Pais_nuevo_solo_si_se_conoce_y_no_se_habia_visto()
    {
        Assert.True(Equipos.EsPaisNuevo(["ES"], "FR"));
        Assert.False(Equipos.EsPaisNuevo(["ES", "FR"], "fr"));
        Assert.False(Equipos.EsPaisNuevo(["ES"], null));
        Assert.False(Equipos.EsPaisNuevo(["ES"], "?"));
    }

    [Fact]
    public async Task La_primera_vez_aprende_en_silencio()
    {
        using var db = Db();
        var r = await Equipos.Registrar(db, Iphone15, "1.1.1.1", new Geo("ES", "Madrid", 40, -3), Ahora);
        Assert.True(r.Aprendiendo);
        Assert.False(r.Nuevo);
        Assert.Equal(1, await db.Equipos.CountAsync());
    }

    [Fact]
    public async Task Un_equipo_conocido_no_avisa_y_suma_entradas()
    {
        using var db = Db();
        await Equipos.Registrar(db, Iphone15, "1.1.1.1", new Geo("ES", "Madrid", 40, -3), Ahora);
        var r = await Equipos.Registrar(db, Iphone16, "2.2.2.2", new Geo("ES", "Getafe", 40, -3), Ahora.AddDays(1));
        Assert.False(r.Nuevo);
        Assert.Equal(2, r.Equipo.Entradas);
        Assert.Equal("Getafe", r.Equipo.Ciudad);
        Assert.Equal("2.2.2.2", r.Equipo.UltimaIp);
        Assert.Equal(1, await db.Equipos.CountAsync());
    }

    [Fact]
    public async Task Un_equipo_nuevo_avisa_y_en_otro_pais_lo_dice()
    {
        using var db = Db();
        await Equipos.Registrar(db, Iphone15, "1.1.1.1", new Geo("ES", "Madrid", 40, -3), Ahora);
        var otroEquipo = await Equipos.Registrar(db, Windows, "3.3.3.3", new Geo("ES", "Madrid", 40, -3), Ahora);
        Assert.True(otroEquipo.Nuevo);
        Assert.False(otroEquipo.PaisNuevo);
        var otroPais = await Equipos.Registrar(db, Iphone15, "4.4.4.4", new Geo("RU", "Moscú", 55, 37), Ahora);
        Assert.True(otroPais.Nuevo);
        Assert.True(otroPais.PaisNuevo);
    }

    [Fact]
    public async Task Sin_geolocalizacion_se_apunta_con_pais_desconocido_y_sin_falsa_alarma_de_pais()
    {
        using var db = Db();
        await Equipos.Registrar(db, Iphone15, "1.1.1.1", new Geo("ES", "Madrid", 40, -3), Ahora);
        var r = await Equipos.Registrar(db, Windows, "10.0.0.5", null, Ahora);
        Assert.True(r.Nuevo);
        Assert.False(r.PaisNuevo);
        Assert.Equal("?", r.Equipo.Pais);
    }
}
