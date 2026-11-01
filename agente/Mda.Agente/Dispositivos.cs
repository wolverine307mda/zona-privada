using System.Management;
using System.Runtime.InteropServices;

// Lo que el portátil sabe de sí mismo y deja cambiar: batería, volumen y brillo de la pantalla.
// Todo es opcional: un PC de sobremesa no tiene batería ni brillo propio, y eso no es un error (devuelve null).
static class Dispositivos
{
    // ---- batería (kernel32, sin dependencias) ----
    [StructLayout(LayoutKind.Sequential)]
    struct EstadoEnergia { public byte AcLine, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out EstadoEnergia s);

    // (porcentaje, enchufado) o null si no hay batería
    public static (int Porcentaje, bool Cargando)? Bateria()
    {
        if (!GetSystemPowerStatus(out var s) || s.BatteryFlag == 128 || s.BatteryLifePercent > 100) return null; // 128 = sin batería, 255 = desconocido
        return (s.BatteryLifePercent, s.AcLine == 1);
    }

    // ---- brillo de la pantalla interna (WMI; solo portátiles) ----
    public static int? Brillo()
    {
        try
        {
            using var b = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementBaseObject o in b.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    public static bool PonerBrillo(int porcentaje)
    {
        porcentaje = Math.Clamp(porcentaje, 0, 100);
        try
        {
            using var m = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in m.Get().Cast<ManagementObject>())
            {
                o.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)porcentaje]);
                return true;
            }
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    // ---- volumen general (Core Audio: IAudioEndpointVolume del altavoz por defecto) ----
    public static int? Volumen()
    {
        var v = Endpoint();
        if (v is null) return null;
        try { return v.GetMasterVolumeLevelScalar(out var n) == 0 ? (int?)Math.Round(n * 100) : null; }
        finally { Marshal.ReleaseComObject(v); }
    }

    public static bool PonerVolumen(int porcentaje)
    {
        var v = Endpoint();
        if (v is null) return false;
        try
        {
            var g = Guid.Empty;
            porcentaje = Math.Clamp(porcentaje, 0, 100);
            if (porcentaje > 0) v.SetMute(false, ref g); // subir el volumen también quita el silencio
            return v.SetMasterVolumeLevelScalar(porcentaje / 100f, ref g) == 0;
        }
        finally { Marshal.ReleaseComObject(v); }
    }

    static IAudioEndpointVolume? Endpoint()
    {
        object? enumerador = null, dispositivo = null;
        try
        {
            enumerador = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            if (((IMMDeviceEnumerator)enumerador!).GetDefaultAudioEndpoint(0, 1, out var d) != 0) return null; // eRender, eMultimedia
            dispositivo = d;
            var iid = typeof(IAudioEndpointVolume).GUID;
            return d.Activate(ref iid, 23, IntPtr.Zero, out var v) == 0 ? (IAudioEndpointVolume)v : null;
        }
        catch (COMException) { return null; }
        catch (InvalidCastException) { return null; }
        finally
        {
            if (dispositivo is not null) Marshal.ReleaseComObject(dispositivo);
            if (enumerador is not null) Marshal.ReleaseComObject(enumerador);
        }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int flujo, int estado, out object dispositivos);
        [PreserveSig] int GetDefaultAudioEndpoint(int flujo, int rol, out IMMDevice dispositivo);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int contexto, IntPtr parametros, [MarshalAs(UnmanagedType.IUnknown)] out object interfaz);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr n);
        int UnregisterControlChangeNotify(IntPtr n);
        int GetChannelCount(out uint canales);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid contexto);
        [PreserveSig] int SetMasterVolumeLevelScalar(float nivel, ref Guid contexto);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float nivel);
        [PreserveSig] int SetChannelVolumeLevel(uint canal, float db, ref Guid contexto);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint canal, float nivel, ref Guid contexto);
        [PreserveSig] int GetChannelVolumeLevel(uint canal, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint canal, out float nivel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool silencio, ref Guid contexto);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool silencio);
    }
}
