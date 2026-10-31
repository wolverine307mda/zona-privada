using System.Management;
using Microsoft.Win32;

// La salud del PC que ve la zona privada: espacio de cada unidad, estado de cada disco (Windows lo sabe por WMI: «Healthy»,
// «Warning»…), temperatura y desgaste cuando el disco los da, y si Windows está esperando un reinicio.
// Lo que el equipo no sepa dar (casi siempre la temperatura de un disco SATA antiguo) va a null y no es un error.
record DiscoSalud(string Nombre, string Modelo, string Salud, string Estado, double? TemperaturaC, int? DesgastePct, long? ErroresLectura);
record VolumenSalud(string Nombre, long LibreBytes, long TotalBytes);
record SaludPc(DateTimeOffset Hora, List<DiscoSalud> Discos, List<VolumenSalud> Volumenes, bool ReinicioPendiente);

static class Salud
{
    static readonly string[] Estados = ["Healthy", "Warning", "Unhealthy"];

    public static SaludPc Leer() => new(DateTimeOffset.UtcNow, Discos(), Volumenes(), ReinicioPendiente());

    static List<VolumenSalud> Volumenes()
    {
        var l = new List<VolumenSalud>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.DriveType == DriveType.Fixed && d.IsReady) l.Add(new VolumenSalud(d.Name.TrimEnd('\\'), d.AvailableFreeSpace, d.TotalSize)); }
            catch (IOException) { /* una unidad que no responde no tumba el resto */ }
        }
        return l;
    }

    static List<DiscoSalud> Discos()
    {
        var l = new List<DiscoSalud>();
        try
        {
            var ambito = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            ambito.Connect();
            // temperatura y desgaste, por disco (si el disco los da)
            var fiabilidad = new Dictionary<string, (double? Temp, int? Desgaste, long? Errores)>();
            try
            {
                using var f = new ManagementObjectSearcher(ambito, new ObjectQuery("SELECT DeviceId, Temperature, Wear, ReadErrorsTotal FROM MSFT_StorageReliabilityCounter"));
                foreach (ManagementObject o in f.Get())
                    fiabilidad[Convert.ToString(o["DeviceId"]) ?? ""] = (Numero(o["Temperature"]), (int?)Numero(o["Wear"]), (long?)Numero(o["ReadErrorsTotal"]));
            }
            catch (ManagementException) { /* sin contadores de fiabilidad */ }

            using var s = new ManagementObjectSearcher(ambito, new ObjectQuery("SELECT DeviceId, FriendlyName, HealthStatus, OperationalStatus FROM MSFT_PhysicalDisk"));
            foreach (ManagementObject o in s.Get())
            {
                var id = Convert.ToString(o["DeviceId"]) ?? "";
                var salud = o["HealthStatus"] is ushort h && h < Estados.Length ? Estados[h] : "Unknown";
                var estado = o["OperationalStatus"] is ushort[] { Length: > 0 } ops ? ops[0] switch { 2 => "OK", 3 => "Degraded", 5 => "Error", var x => x.ToString() } : "OK";
                var (temp, desgaste, errores) = fiabilidad.GetValueOrDefault(id);
                l.Add(new DiscoSalud(id, Convert.ToString(o["FriendlyName"]) ?? "Disco", salud, estado, temp is > 0 and < 150 ? temp : null, desgaste is >= 0 and <= 100 ? desgaste : null, errores));
            }
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // sin acceso a WMI de almacenamiento: se manda solo el espacio de las unidades
        }
        return l;
    }

    static double? Numero(object? v) => v is null ? null : Convert.ToDouble(v);

    static bool ReinicioPendiente()
    {
        try
        {
            using var a = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            using var b = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            return a is not null || b is not null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
}
