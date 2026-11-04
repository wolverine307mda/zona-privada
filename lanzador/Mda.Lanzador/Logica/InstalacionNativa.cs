namespace Mda.Lanzador;

// Cómo se instala la versión de escritorio: dónde y con qué permisos. Lógica pura; ejecutar robocopy e icacls está en Instalador.cs.
public static class InstalacionNativa
{
    public const string Carpeta = "ZonaPrivada";

    public static string Destino(string archivosDePrograma) => Path.Combine(archivosDePrograma, Carpeta);

    // robocopy copia de forma espejo; los códigos 0 a 7 son éxito (8 o más, error)
    public static string Robocopy(string origen, string destino) => $"\"{origen.TrimEnd('\\')}\" \"{destino.TrimEnd('\\')}\" /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP";
    public static bool RobocopyBien(int codigo) => codigo is >= 0 and < 8;

    // Los programas, solo lectura para todos menos administradores: nadie sin ser administrador puede cambiar lo que se ejecuta
    public static string IcaclsPrograma(string destino) =>
        $"\"{destino}\" /inheritance:r /grant:r {Permisos.Sistema}:(OI)(CI)F {Permisos.Administradores}:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX";

    // ¿hay lo mínimo para instalar desde esta carpeta? (el exe y los tres componentes)
    public static List<string> Faltan(string origen) =>
        new[] { "ZonaPrivada.exe", Path.Combine("componentes", "api", "Mda.Api.exe"), Path.Combine("componentes", "postgres", "bin", "postgres.exe"), Path.Combine("componentes", "age", "age.exe") }
            .Where(r => !File.Exists(Path.Combine(origen, r))).ToList();
}
