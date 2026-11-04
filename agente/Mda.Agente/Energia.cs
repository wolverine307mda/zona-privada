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
