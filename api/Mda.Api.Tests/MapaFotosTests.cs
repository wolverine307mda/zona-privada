using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Mda.Api.Data;
using Mda.Api.Galeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class MapaFotosTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mda-mapa-" + Guid.NewGuid().ToString("N"));
    public MapaFotosTests() => Directory.CreateDirectory(_raiz);
    public void Dispose() { try { Directory.Delete(_raiz, true); } catch (IOException) { /* se limpia solo */ } }

    private static AppDbContext Db() => BaseDeDatos.Nueva();

    // Un JPEG mínimo que solo lleva el bloque EXIF con un GPS (hecho a mano: sin imagen, lo justo para que se lea el GPS)
    public static byte[] JpegConGps(double lat, double lon)
    {
        static void Racional(BinaryWriter w, uint n, uint d) { w.Write(n); w.Write(d); }
        static void Grados(BinaryWriter w, double v) { var a = Math.Abs(v); var g = (uint)a; var m = (uint)((a - g) * 60); Racional(w, g, 1); Racional(w, m, 1); Racional(w, (uint)Math.Round(((a - g) * 60 - m) * 60 * 100), 100); }
        using var tiff = new MemoryStream();
        using (var w = new BinaryWriter(tiff, Encoding.ASCII, true))
        {
            w.Write(Encoding.ASCII.GetBytes("II")); w.Write((ushort)42); w.Write(8u);       // cabecera TIFF (little endian) y IFD0 en el byte 8
            w.Write((ushort)1);                                                              // IFD0: una entrada, el puntero al GPS
            w.Write((ushort)0x8825); w.Write((ushort)4); w.Write(1u); w.Write(26u); w.Write(0u);
            w.Write((ushort)4);                                                              // IFD del GPS (byte 26): 4 entradas
            w.Write((ushort)1); w.Write((ushort)2); w.Write(2u); w.Write(Encoding.ASCII.GetBytes(lat >= 0 ? "N\0\0\0" : "S\0\0\0"));
            w.Write((ushort)2); w.Write((ushort)5); w.Write(3u); w.Write(80u);
            w.Write((ushort)3); w.Write((ushort)2); w.Write(2u); w.Write(Encoding.ASCII.GetBytes(lon >= 0 ? "E\0\0\0" : "W\0\0\0"));
            w.Write((ushort)4); w.Write((ushort)5); w.Write(3u); w.Write(104u);
            w.Write(0u);                                                                     // sin más IFD
            Grados(w, lat); Grados(w, lon);                                                  // datos en los bytes 80 y 104
        }
        var cuerpo = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff.ToArray()).ToArray();
        var longitud = (ushort)(cuerpo.Length + 2);
        return [0xFF, 0xD8, 0xFF, 0xE1, (byte)(longitud >> 8), (byte)(longitud & 0xFF), .. cuerpo, 0xFF, 0xD9];
    }

    [Fact]
    public void Lee_el_gps_de_un_jpeg_con_exif()
    {
        var ruta = Path.Combine(_raiz, "madrid.jpg");
        File.WriteAllBytes(ruta, JpegConGps(40.4, -3.7));
        var (lat, lon, _) = IndiceFotos.LeerMetadatos(ruta);
        Assert.Equal(40.4, lat!.Value, 3);
        Assert.Equal(-3.7, lon!.Value, 3);
    }

    [Fact]
    public void Un_archivo_sin_exif_o_roto_no_da_gps_ni_falla()
    {
        File.WriteAllBytes(Path.Combine(_raiz, "roto.jpg"), [1, 2, 3]);
        Assert.Equal((null, null, null), IndiceFotos.LeerMetadatos(Path.Combine(_raiz, "roto.jpg")));
    }

    [Fact]
    public async Task La_pasada_apunta_las_nuevas_con_o_sin_gps_y_no_repite_ni_olvida()
    {
        Directory.CreateDirectory(Path.Combine(_raiz, "viaje"));
        File.WriteAllBytes(Path.Combine(_raiz, "viaje", "a.jpg"), JpegConGps(41.38, 2.17));
        File.WriteAllBytes(Path.Combine(_raiz, "sin-gps.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
        File.WriteAllText(Path.Combine(_raiz, "notas.txt"), "no es una imagen");
        using var db = Db();

        var r1 = await IndiceFotos.Pasada(db, _raiz);
        Assert.Equal(2, r1.Nuevas);
        Assert.Equal(2, r1.Total);                                                           // el .txt no cuenta
        Assert.Equal(1, await db.FotosGeo.CountAsync(x => x.Lat != null));
        Assert.Equal("viaje/a.jpg", (await db.FotosGeo.SingleAsync(x => x.Lat != null)).Ruta);

        var r2 = await IndiceFotos.Pasada(db, _raiz);
        Assert.Equal(0, r2.Nuevas);                                                          // nada cambió: no se vuelve a leer

        File.Delete(Path.Combine(_raiz, "sin-gps.jpg"));
        var r3 = await IndiceFotos.Pasada(db, _raiz);
        Assert.Equal(1, r3.Quitadas);
        Assert.Equal(1, await db.FotosGeo.CountAsync());
    }

    [Fact]
    public async Task El_tope_por_pasada_deja_el_resto_pendiente_y_se_acaba_despues()
    {
        for (var i = 0; i < 5; i++) File.WriteAllBytes(Path.Combine(_raiz, $"f{i}.jpg"), [0xFF, 0xD8, 0xFF, 0xD9, (byte)i]);
        using var db = Db();
        var r1 = await IndiceFotos.Pasada(db, _raiz, maximo: 2);
        Assert.Equal((2, 3), (r1.Nuevas, r1.Pendientes));
        var r2 = await IndiceFotos.Pasada(db, _raiz, maximo: 10);
        Assert.Equal((3, 0), (r2.Nuevas, r2.Pendientes));
        Assert.Equal(5, await db.FotosGeo.CountAsync());
    }

    [Fact]
    public async Task Una_foto_cambiada_se_vuelve_a_leer()
    {
        var ruta = Path.Combine(_raiz, "x.jpg");
        File.WriteAllBytes(ruta, [0xFF, 0xD8, 0xFF, 0xD9]);
        using var db = Db();
        await IndiceFotos.Pasada(db, _raiz);
        File.WriteAllBytes(ruta, JpegConGps(40.0, -4.0));
        var r = await IndiceFotos.Pasada(db, _raiz);
        Assert.Equal(1, r.Nuevas);
        Assert.NotNull((await db.FotosGeo.SingleAsync()).Lat);
    }

    [Fact]
    public async Task El_mapa_exige_sesion_y_pin()
    {
        using var f = new ModulosFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/galeria/mapa")).StatusCode);
        var c = await f.Dentro();
        // sin PIN puesto, el filtro deja pasar (como el resto de «Imágenes»): la lista viene vacía
        var j = await c.GetFromJsonAsync<JsonElement>("/api/galeria/mapa");
        Assert.Equal(0, j.GetProperty("conGps").GetInt32());
    }
}
