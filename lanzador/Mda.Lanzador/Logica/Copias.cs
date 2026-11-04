using System.Globalization;

namespace Mda.Lanzador;

// Las copias de seguridad de la versión de escritorio: mismas reglas, nombres y formatos que herramientas/copias.sh (el contenedor «copias» de Docker),
// para que la API las vigile igual. Aquí solo la lógica (qué toca hacer y cómo se llaman las cosas); ejecutar pg_dump y age está en CopiasNativas.cs.
public static class Copia
{
    public const string Prefijo = "zonaprivada-";
    public const int Locales = 14, Diarias = 14, Semanales = 8;

    public static string Nombre(DateTime local) => $"{Prefijo}{local:yyyyMMdd}-{local:HHmm}.dump";

    // Toca el dump del día si ya pasaron las 3 de la madrugada y todavía no hay uno de hoy (si el equipo estaba apagado, se hace al encenderse)
    public static bool TocaDump(DateTime local, IEnumerable<string> existentes) =>
        local.Hour >= 3 && !existentes.Any(n => n.StartsWith($"{Prefijo}{local:yyyyMMdd}-", StringComparison.Ordinal) && n.EndsWith(".dump", StringComparison.Ordinal));

    // Los nombres llevan la fecha y la hora, así que por orden alfabético son por orden de antigüedad: se guardan los últimos «guardar»
    public static List<string> Sobrantes(IEnumerable<string> nombres, int guardar) =>
        nombres.OrderByDescending(n => n, StringComparer.Ordinal).Skip(guardar).ToList();

    public static string CifradoDump(DateTime local) => $"{Prefijo}{local:yyyyMMdd}.dump.age";
    public static string CifradoClaves(DateTime local) => $"{Prefijo}{local:yyyyMMdd}-claves.tar.age";
    public static bool EsDomingo(DateTime local) => local.DayOfWeek == DayOfWeek.Sunday;
}

public static class SimulacroCopia
{
    // Cada semana (ISO) a partir de las 4 de la madrugada, una vez
    public static string Semana(DateTime local) => $"{ISOWeek.GetYear(local)}{ISOWeek.GetWeekOfYear(local):00}";
    public static string Marca(DateTime local) => $".simulacro-{Semana(local)}";
    public static bool Toca(DateTime local, Func<string, bool> existeMarca) => local.Hour >= 4 && !existeMarca(Marca(local));

    // Una copia vale si se restaura con al menos un usuario y diez tablas
    public static bool Vale(int usuarios, int tablas) => usuarios >= 1 && tablas >= 10;

    // «ok|usuarios|tablas|archivo|fecha» (o «fallo|0|0|archivo|fecha»): lo que lee Vigilante.LeerSimulacro en la API
    public static string Linea(bool ok, int usuarios, int tablas, string archivo, DateTime utc) =>
        $"{(ok ? "ok" : "fallo")}|{usuarios}|{tablas}|{archivo}|{utc.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}";
}
