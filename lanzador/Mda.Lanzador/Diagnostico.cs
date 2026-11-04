using System.IO.Compression;
using System.Text;

namespace Mda.Lanzador;

// «Exportar diagnóstico»: un zip en el escritorio para enseñar a quien ayude, sin ninguna clave dentro
// (del .env solo van los nombres y si tienen valor; los registros de la API no llevan claves).
static class Diagnostico
{
    public static async Task<string> Crear(string carpeta, string datos, string estado, string versionExe)
    {
        var zip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"diagnostico-zona-privada-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var resumen = new StringBuilder()
            .AppendLine($"Zona privada (lanzador {versionExe})").AppendLine($"Fecha: {DateTime.Now:O}").AppendLine($"Sistema: {Environment.OSVersion} · {Environment.ProcessorCount} núcleos")
            .AppendLine($"Carpeta: {carpeta}").AppendLine($"Estado: {estado}").AppendLine($"Docker: {(Docker.Instalado ? Docker.Exe : "no instalado")}").ToString();
        var ps = await Docker.Cmd("compose ps -a", carpeta, 30);
        var registros = await Docker.Cmd("compose logs --no-color --tail 300 api", carpeta, 45);
        var envPath = Path.Combine(carpeta, ".env");

        using var fs = File.Create(zip);
        using var z = new ZipArchive(fs, ZipArchiveMode.Create);
        void Texto(string nombre, string contenido) { var e = z.CreateEntry(nombre); using var w = new StreamWriter(e.Open(), Encoding.UTF8); w.Write(contenido); }
        Texto("resumen.txt", resumen);
        Texto("docker-ps.txt", ps.Salida);
        Texto("api-ultimos-registros.txt", registros.Salida);
        Texto("env-sin-valores.txt", File.Exists(envPath) ? Primera.EnvSinValores(File.ReadAllLines(envPath)) : "No hay .env");
        foreach (var f in new[] { "lanzador.log", "lanzador.log.1" })
        {
            var ruta = Path.Combine(datos, f);
            if (!File.Exists(ruta)) continue;
            using var origen = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var destino = z.CreateEntry(f).Open();
            await origen.CopyToAsync(destino);
        }
        return zip;
    }
}
