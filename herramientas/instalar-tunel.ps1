# Instala (una sola vez) el tunel de Cloudflare como SERVICIO DE WINDOWS, sin Docker: arranca solo con el PC, se reinicia si se cae
# y saca la version de escritorio (http://localhost:8090) a https://app.wolvem.dev.
#
#   .\herramientas\instalar-tunel.ps1               instala cloudflared (con winget, si falta) y el servicio
#   .\herramientas\instalar-tunel.ps1 -Reinstalar   lo quita y lo vuelve a instalar (por ejemplo, si cambias el token)
#   .\herramientas\instalar-tunel.ps1 -Quitar       desinstala el servicio
#
# El token se lee de CLOUDFLARE_TUNNEL_TOKEN en el .env de la raiz (nunca se imprime ni se guarda en ningun archivo del repo).
# Pide permiso de administrador una vez. Despues, en Cloudflare (Zero Trust > Networks > Tunnels > zona-privada > Public hostname),
# la ruta app.wolvem.dev debe apuntar a  http://localhost:8090  (antes apuntaba a http://api:8080, que era el contenedor de Docker).
param([switch]$Reinstalar, [switch]$Quitar, [switch]$Admin)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot

if (-not $Admin) {
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Admin')
    if ($Reinstalar) { $a += '-Reinstalar' }; if ($Quitar) { $a += '-Quitar' }
    try { Start-Process powershell -Verb RunAs -ArgumentList $a -Wait } catch { throw 'Se cancelo el permiso de administrador: no se ha hecho nada.' }
    exit
}

try {
    function Buscar-Cloudflared {
        $c = Get-Command cloudflared -ErrorAction SilentlyContinue
        if ($c) { return $c.Source }
        foreach ($p in @("${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe", "$env:ProgramFiles\cloudflared\cloudflared.exe")) { if (Test-Path $p) { return $p } }
        return $null
    }

    $exe = Buscar-Cloudflared
    if (-not $exe -and -not $Quitar) {
        Write-Host 'cloudflared no esta instalado: lo instalo con winget (paquete oficial Cloudflare.cloudflared)...' -ForegroundColor Cyan
        winget install --id Cloudflare.cloudflared -e --accept-package-agreements --accept-source-agreements
        $exe = Buscar-Cloudflared
        if (-not $exe) { throw 'No encuentro cloudflared tras instalarlo. Cierra y abre la consola, o instalalo desde developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads' }
    }

    $existe = Get-Service -Name Cloudflared -ErrorAction SilentlyContinue
    if (($Quitar -or $Reinstalar) -and $existe) {
        Write-Host 'Quito el servicio actual...' -ForegroundColor Cyan
        & $exe service uninstall
        Start-Sleep -Seconds 3
        $existe = $null
    }
    if ($Quitar) { Write-Host 'Servicio quitado.' -ForegroundColor Green; return }

    if ($existe) {
        Write-Host "El servicio Cloudflared ya existe ($($existe.Status)). Usa -Reinstalar para rehacerlo." -ForegroundColor Yellow
    } else {
        $envRepo = Join-Path $raiz '.env'
        $linea = if (Test-Path $envRepo) { Get-Content $envRepo | Where-Object { $_ -match '^CLOUDFLARE_TUNNEL_TOKEN=.+' } | Select-Object -First 1 } else { $null }
        if (-not $linea) { throw 'Falta CLOUDFLARE_TUNNEL_TOKEN en el .env de la raiz.' }
        $token = ($linea -replace '^CLOUDFLARE_TUNNEL_TOKEN=', '').Trim().Trim('"')
        Write-Host 'Instalo el servicio Cloudflared (el token no se muestra)...' -ForegroundColor Cyan
        & $exe service install $token
        if ($LASTEXITCODE -ne 0) { throw "cloudflared service install fallo ($LASTEXITCODE)." }
        $token = $null
    }
    Start-Service -Name Cloudflared -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 5
    $s = Get-Service -Name Cloudflared -ErrorAction SilentlyContinue
    Write-Host "Servicio Cloudflared: $($s.Status), inicio $($s.StartType)." -ForegroundColor Green
    Write-Host ''
    Write-Host 'Ultimo paso (en Cloudflare, una vez): Zero Trust > Networks > Tunnels > zona-privada > Public hostname > app.wolvem.dev' -ForegroundColor Yellow
    Write-Host '   Service: HTTP  localhost:8090   (la ruta vieja era api:8080, el contenedor de Docker).' -ForegroundColor Yellow
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
}
Read-Host 'Pulsa Enter para cerrar'
