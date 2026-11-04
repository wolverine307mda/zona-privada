using System.Globalization;

// Limpieza de carpetas de temporales: solo borra archivos más viejos que el límite, nunca entra en enlaces ni uniones (así no se sale de la carpeta),
// se salta lo que está en uso o sin permiso y quita las subcarpetas que se quedan vacías. Sin nada de Windows para poder probarla.
static class Limpieza
{
    // (bytes liberados, archivos borrados)
    public static (long Bytes, int Archivos) Barrer(string carpeta, DateTime masViejosQue)
    {
        long bytes = 0; var archivos = 0;
        if (!Directory.Exists(carpeta)) return (0, 0);
        Recorrer(new DirectoryInfo(carpeta), masViejosQue, ref bytes, ref archivos, raiz: true);
        return (bytes, archivos);
    }

    private static void Recorrer(DirectoryInfo dir, DateTime limite, ref long bytes, ref int archivos, bool raiz)
    {
        try
        {
            foreach (var f in dir.EnumerateFiles())
            {
                try
                {
                    if ((f.Attributes & FileAttributes.ReparsePoint) != 0 || f.LastWriteTimeUtc > limite) continue;
                    var tam = f.Length;
                    f.Delete();
                    bytes += tam; archivos++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* en uso o sin permiso: se deja */ }
            }
            foreach (var sub in dir.EnumerateDirectories())
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue; // un enlace o una unión: no se sigue
                Recorrer(sub, limite, ref bytes, ref archivos, raiz: false);
                try { if (!sub.EnumerateFileSystemInfos().Any()) sub.Delete(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* carpeta que no se puede leer */ }
    }

    public static string Tam(long bytes) =>
        bytes >= 1_073_741_824 ? $"{(bytes / 1_073_741_824.0).ToString("0.0", CultureInfo.GetCultureInfo("es-ES"))} GB" : $"{(bytes / 1_048_576.0).ToString("0", CultureInfo.GetCultureInfo("es-ES"))} MB";
}
