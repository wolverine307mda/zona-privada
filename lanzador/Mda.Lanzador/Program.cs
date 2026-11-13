using Mda.Lanzador;

// Zona privada.exe
//   (sin argumentos)  arranca todo y se queda en la bandeja
//   --silencioso      igual, pero sin avisar si ya estaba abierto (lo usa la tarea de inicio de Windows)
//   --instalar        crea la tarea de inicio (un aviso de administrador) y los accesos directos
//   --instalar-escritorio  instala el paquete de la versión de escritorio (sin Docker) en Archivos de programa
//   --desinstalar     los quita
//   --salir           pide al lanzador que está abierto que salga con calma (como «Salir» del menú: para la API y PostgreSQL bien)
static class Programa
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--salir")) return Bandeja.PedirSalida();
        if (args.Contains("--instalar-escritorio")) return Instalador.InstalarEscritorio();
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
