// Los planes de energía de Windows que se pueden elegir desde la app (los identificadores son los de siempre en todos los Windows).
// En su propio archivo y sin nada de Windows para poder probarlo.
static class Energia
{
    public const string Equilibrado = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string Ahorro = "a1841308-3541-4fab-bc81-f71556f20b4a";
    public const string AltoRendimiento = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    // «cine» no es un plan de Windows: es el equilibrado con el PC (y la pantalla) despiertos mientras dura
    public static string? Plan(string? perfil) => perfil switch
    {
        "equilibrado" or "cine" => Equilibrado,
        "ahorro" => Ahorro,
        "rendimiento" => AltoRendimiento,
        _ => null,
    };
}

// Apagar con un temporizador: sin parámetro, los 30 s de siempre; con minutos (1 a 720), ese tiempo. Cancelable con la orden «cancelar».
static class Apagado
{
    public static int? Segundos(string? parametro)
    {
        if (string.IsNullOrWhiteSpace(parametro)) return 30;
        return int.TryParse(parametro, out var min) && min is >= 1 and <= 720 ? min * 60 : null;
    }

    public static string Texto(int segundos) =>
        segundos < 60 ? $"{segundos} s" : segundos < 3600 ? $"{segundos / 60} min" : segundos % 3600 == 0 ? $"{segundos / 3600} h" : $"{segundos / 3600} h {segundos % 3600 / 60} min";
}
