using System.Reflection;

namespace Mda.Api.Sistema;

// La versión que está corriendo: el commit con el que se compiló (actualizar-escritorio.ps1 lo estampa con -p:SourceRevisionId).
// Así quien despliega puede comprobar que contesta la versión nueva y no la anterior.
public static class VersionApp
{
    public const string Desconocida = "desconocida";

    public static readonly string Actual = Leer(typeof(VersionApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    // «1.0.0+abc1234» → «abc1234»; sin «+» (compilado sin git) no hay versión que decir
    public static string Leer(string? informacional)
    {
        var i = informacional?.IndexOf('+') ?? -1;
        return i >= 0 && i < informacional!.Length - 1 ? informacional[(i + 1)..] : Desconocida;
    }
}
