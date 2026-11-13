# Lo que comparten actualizar-escritorio.ps1 y control-zona.ps1 (se carga con «. "$PSScriptRoot\zona-comun.ps1"»):
# dónde está instalada la zona privada de escritorio, cómo se sabe si responde y cómo se para sin matar nada que no sea suyo.
$destino = 'C:\Program Files\ZonaPrivada'
$urlSalud = 'http://127.0.0.1:8090/api/salud'
$esAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# El lanzador pide permisos de administrador en su manifiesto (requireAdministrator): abrirlo con explorer, o lanzarlo desde aquí, pone el aviso de Windows (UAC)
# en pantalla cada vez, o lo deja pegado a quien lo lanza. La tarea programada «Zona privada» (la de «Iniciar con Windows», que registra el propio lanzador)
# lo abre con los privilegios más altos SIN aviso y desacoplado: es la forma de abrirlo cuando no hay nadie delante.
function Abrir-Lanzador([string]$como = '') {
    if (Get-ScheduledTask -TaskName 'Zona privada' -ErrorAction SilentlyContinue) {
        Start-ScheduledTask -TaskName 'Zona privada'
        Write-Host "Lanzador abierto$(if ($como) { " $como" }) (tarea «Zona privada», sin aviso de permisos)."
    } else {
        # sin la tarea (no se activó «Iniciar con Windows»): se abre con explorer y Windows pedirá permiso en pantalla
        Write-Host 'Aviso: no existe la tarea «Zona privada»; se abre con explorer y Windows pedirá permiso. Actívala en el menú del lanzador: «Iniciar con Windows».' -ForegroundColor Yellow
        Start-Process explorer.exe -ArgumentList "`"$destino\ZonaPrivada.exe`""
        Write-Host "Lanzador abierto$(if ($como) { " $como" })."
    }
}

# Contesta la API local? y con qué versión (las APIs antiguas no la dicen)
function Leer-Salud {
    try {
        $r = Invoke-WebRequest -UseBasicParsing $urlSalud -TimeoutSec 3
        $v = $null
        try { $v = ($r.Content | ConvertFrom-Json).version } catch { }
        return @{ Ok = ($r.StatusCode -eq 200); Version = $v }
    } catch { return @{ Ok = $false; Version = $null } }
}

function Esperar-Salud([int]$intentos = 40) {
    $s = @{ Ok = $false; Version = $null }
    for ($i = 0; $i -lt $intentos -and -not $s.Ok; $i++) { $s = Leer-Salud; if (-not $s.Ok) { Start-Sleep -Seconds 3 } }
    return $s
}

# Para la zona privada y SOLO la zona privada: nada de matar «postgres» o «Mda.Api» a ciegas (otro PostgreSQL, una API de desarrollo…)
function Parar-Zona {
    $lanz = Get-Process ZonaPrivada -ErrorAction SilentlyContinue
    if ($lanz) {
        $h = $null
        if ([System.Threading.EventWaitHandle]::TryOpenExisting('Global\ZonaPrivada.Lanzador.Salir', [ref]$h)) {
            Write-Host 'Pidiendo al lanzador que salga con calma (para la API y PostgreSQL bien)...'
            [void]$h.Set(); $h.Dispose()
            $limite = (Get-Date).AddSeconds(90)
            while ((Get-Process ZonaPrivada -ErrorAction SilentlyContinue) -and (Get-Date) -lt $limite) { Start-Sleep -Milliseconds 500 }
            if (Get-Process ZonaPrivada -ErrorAction SilentlyContinue) { Write-Host 'No salió en 90 s: se cierra a la fuerza.'; Get-Process ZonaPrivada -ErrorAction SilentlyContinue | Stop-Process -Force }
        } else {
            Write-Host 'El lanzador instalado es de una versión sin salida con calma: se cierra a la fuerza (solo esta vez).'
            $lanz | Stop-Process -Force
        }
        Start-Sleep -Seconds 3
    }
    # lo que quede de lo nuestro (por ruta: solo lo que vive en Archivos de programa\ZonaPrivada)
    $prefijo = "$destino\"
    Get-CimInstance Win32_Process -Filter "Name='Mda.Api.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefijo, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Write-Host "Cerrando Mda.Api ($($_.ProcessId))"; Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    # solo el postgres de la zona privada, por su carpeta de datos (PostgreSQL la escribe con «/»). Otros que usen los mismos binarios (el gestor de servicios) no se tocan.
    $datosPg = Join-Path $env:ProgramData 'ZonaPrivada\pg'
    Get-CimInstance Win32_Process -Filter "Name='postgres.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Replace('/', '\').IndexOf($datosPg, [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
        ForEach-Object { Write-Host "Cerrando postgres de la zona privada ($($_.ProcessId))"; & taskkill /PID $_.ProcessId /T /F | Out-Null }
    Start-Sleep -Seconds 2
}
