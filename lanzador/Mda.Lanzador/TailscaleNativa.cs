namespace Mda.Lanzador;

// Maneja el Tailscale de Windows (si está instalado). No instala nada solo. No toca el nodo de Tailscale de Docker.
static class TailscaleNativa
{
    public static string? Exe() => Entorno.PrimeroQueExiste(
        TailscaleOrdenes.Candidatos(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)), File.Exists);

    public static async Task<EstadoTailscale?> Estado()
    {
        if (Exe() is not { } exe) return null;
        var (c, salida) = await Docker.Ejecutar(exe, TailscaleOrdenes.Estado, null, TimeSpan.FromSeconds(15));
        return c == 0 ? TailscaleOrdenes.LeerEstado(salida) : new EstadoTailscale(false, null);
    }

    public static async Task<(bool Ok, string Texto)> Ejecutar(string orden)
    {
        if (Exe() is not { } exe) return (false, "Tailscale no está instalado en Windows (tailscale.com/download).");
        var (c, salida) = await Docker.Ejecutar(exe, orden, null, TimeSpan.FromSeconds(30));
        return (c == 0, salida.Trim());
    }
}
