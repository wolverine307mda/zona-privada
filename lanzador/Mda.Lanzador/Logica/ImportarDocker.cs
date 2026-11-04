namespace Mda.Lanzador;

// Traer los datos de Docker a la versión de escritorio SIN tocar Docker (solo se lee): lo que hay que comprobar antes y las órdenes que se usan.
public static class ImportarDocker
{
    // Los datos cifrados (notas, claves de servicios…) solo se leen con la misma clave maestra: si no coincide, no se importa nada
    public static List<string> Problemas(IReadOnlyDictionary<string, string> envDocker, IReadOnlyDictionary<string, string> envNativo)
    {
        var l = new List<string>();
        var d = envDocker.GetValueOrDefault("MDA_CLAVE_MAESTRA", "");
        var n = envNativo.GetValueOrDefault("MDA_CLAVE_MAESTRA", "");
        if (d.Length == 0) l.Add("El .env de Docker no tiene MDA_CLAVE_MAESTRA.");
        else if (n != d) l.Add("La clave maestra de la versión de escritorio no es la de Docker: lo cifrado (notas, claves de servicios) no se podría leer. Copia el .env de Docker a la versión de escritorio.");
        return l;
    }

    // «docker compose exec -T db …»: lee de la base de Docker (nada se escribe en ella)
    public const string VolcarDocker = "compose exec -T db pg_dump -U mda -d zonaprivada -Fc";
    public const string IdApiDocker = "compose ps -q api";
    public static string Contar(string sql) => $"compose exec -T db psql -U mda -d zonaprivada -tAc \"{sql}\"";
    public const string SqlUsuarios = "select count(*) from \\\"AspNetUsers\\\"";
    public const string SqlTablas = "select count(*) from information_schema.tables where table_schema='public'";

    public static string CopiarClaves(string idContenedor, string destino) => $"cp {idContenedor}:/claves/. \"{destino}\"";

    // Tras restaurar: mismos usuarios y las mismas tablas que en Docker
    public static bool Coincide(int usuariosDocker, int tablasDocker, int usuariosNativo, int tablasNativo) =>
        usuariosDocker >= 1 && usuariosDocker == usuariosNativo && tablasDocker == tablasNativo;
}
