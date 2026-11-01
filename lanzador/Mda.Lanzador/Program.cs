using Mda.Lanzador;

// Zona privada.exe
//   (sin argumentos)  arranca todo y se queda en la bandeja
//   --silencioso      igual, pero sin avisar si ya estaba abierto (lo usa la tarea de inicio de Windows)
//   --instalar        crea la tarea de inicio (un aviso de administrador) y los accesos directos
//   --desinstalar     los quita
static class Programa
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--instalar")) return Instalador.Instalar();
        if (args.Contains("--desinstalar")) return Instalador.Desinstalar();

        using var unica = new Mutex(true, @"Local\ZonaPrivada.Lanzador", out var primera);
        if (!primera)
        {
            if (!args.Contains("--silencioso")) MessageBox.Show("«Zona privada» ya está en marcha: mira el icono junto al reloj.", "Zona privada", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        Application.Run(new Bandeja());
        return 0;
    }
}
