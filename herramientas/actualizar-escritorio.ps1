# Actualiza la versión de escritorio ya instalada (C:\Program Files\ZonaPrivada) con lo que haya en el repo, por partes.
# No descarga nada ni toca Docker: reutiliza el PostgreSQL y el age que ya hay instalados.
#
#   .\herramientas\actualizar-escritorio.ps1 -Web          solo la web (no para nada: se nota al recargar la página)
#   .\herramientas\actualizar-escritorio.ps1 -Api          la API (compila también la web, que va dentro)
#   .\herramientas\actualizar-escritorio.ps1 -Lanzador     el ZonaPrivada.exe y el agente del PC que lleva dentro
#   .\herramientas\actualizar-escritorio.ps1 -Todo         las tres cosas (es lo que hace si no pones nada)
#   .\herramientas\actualizar-escritorio.ps1 -Api -SoloCompilar    prepara dist\actualizacion y no instala
#
# Compilar no pide permisos; instalar sí (Windows pregunta una vez). Al instalar la API o el lanzador se cierra y se vuelve a abrir el lanzador:
# al arrancar, si la API cambió, hace sola una copia previa de la base (copias\antes-de-actualizar-*.dump) y aplica las migraciones nuevas.
param([switch]$Web, [switch]$Api, [switch]$Lanzador, [switch]$Todo, [switch]$SoloCompilar, [switch]$Instalar)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$paq = Join-Path $raiz 'dist\actualizacion'
$destino = 'C:\Program Files\ZonaPrivada'
if (-not ($Web -or $Api -or $Lanzador)) { $Todo = $true }
if ($Todo) { $Web = $true; $Api = $true; $Lanzador = $true }
if ($Api) { $Web = $true }   # la API lleva la web dentro: si se cambia una, se compila la otra para que no se mezclen versiones

# ---- fase 2 (con permisos de administrador): parar, copiar y volver a abrir ----
if ($Instalar) {
    New-Item -ItemType Directory -Force -Path $paq | Out-Null
    Start-Transcript -Path (Join-Path $paq 'actualizar.log') -Force | Out-Null
    try {
        $parar = $Api -or $Lanzador
        if ($parar) {
            Get-Process ZonaPrivada -ErrorAction SilentlyContinue | Stop-Process -Force
            Start-Sleep -Seconds 4
            Get-Process Mda.Api, postgres -ErrorAction SilentlyContinue | Stop-Process -Force
            Start-Sleep -Seconds 3
        }
        if ($Api) {
            robocopy "$paq\api" "$destino\componentes\api" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy de la API falló ($LASTEXITCODE)" }
        } elseif ($Web) {
            robocopy "$paq\wwwroot" "$destino\componentes\api\wwwroot" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy de la web falló ($LASTEXITCODE)" }
        }
        if ($Lanzador) { Copy-Item "$paq\lanz\ZonaPrivada.exe" "$destino\ZonaPrivada.exe" -Force }
        Write-Host 'Copiado.'
        # se abre con explorer para que el lanzador no herede los permisos de administrador
        if ($parar) { Start-Process explorer.exe -ArgumentList "`"$destino\ZonaPrivada.exe`""; Write-Host 'Lanzador abierto.' }
    } catch { Write-Host "ERROR: $_"; Stop-Transcript | Out-Null; exit 1 }
    Stop-Transcript | Out-Null
    return
}

# ---- fase 1 (sin permisos): compilar ----
New-Item -ItemType Directory -Force -Path $paq | Out-Null
$salidaWeb = Join-Path $raiz 'web\dist\web\browser'
if ($Web) {
    Write-Host '== Web (ng build) ==' -ForegroundColor Cyan
    Push-Location (Join-Path $raiz 'web'); npx ng build; $c = $LASTEXITCODE; Pop-Location
    if ($c -ne 0) { throw 'ng build falló: no se instala nada.' }
}
if ($Api) {
    Write-Host '== API (dotnet publish) ==' -ForegroundColor Cyan
    if (Test-Path "$paq\api") { Remove-Item -Recurse -Force "$paq\api" }
    dotnet publish (Join-Path $raiz 'api\Mda.Api\Mda.Api.csproj') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o "$paq\api"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish de la API falló: no se instala nada.' }
    if (Test-Path "$paq\api\wwwroot") { Remove-Item -Recurse -Force "$paq\api\wwwroot" }
    Copy-Item -Recurse $salidaWeb "$paq\api\wwwroot"
} elseif ($Web) {
    if (Test-Path "$paq\wwwroot") { Remove-Item -Recurse -Force "$paq\wwwroot" }
    Copy-Item -Recurse $salidaWeb "$paq\wwwroot"
}
if ($Lanzador) {
    Write-Host '== Lanzador (dotnet publish) ==' -ForegroundColor Cyan
    if (Test-Path "$paq\lanz") { Remove-Item -Recurse -Force "$paq\lanz" }
    dotnet publish (Join-Path $raiz 'lanzador\Mda.Lanzador\Mda.Lanzador.csproj') -c Release -o "$paq\lanz"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish del lanzador falló: no se instala nada.' }
}
if ($SoloCompilar) { Write-Host "Listo en $paq (no se ha instalado nada)." -ForegroundColor Green; return }

# ---- instalar: se vuelve a lanzar este mismo script con permisos de administrador ----
Write-Host '== Instalando (Windows pedirá permiso) ==' -ForegroundColor Cyan
$a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Instalar')
if ($Web) { $a += '-Web' }; if ($Api) { $a += '-Api' }; if ($Lanzador) { $a += '-Lanzador' }
try { Start-Process powershell -Verb RunAs -ArgumentList $a -Wait }
catch { throw 'Se canceló el permiso de administrador: no se ha instalado nada.' }
Get-Content (Join-Path $paq 'actualizar.log') -ErrorAction SilentlyContinue | Select-Object -Last 6

# ---- comprobar que responde ----
Start-Sleep -Seconds 5
if (($Api -or $Lanzador) -and -not (Get-Process ZonaPrivada -ErrorAction SilentlyContinue)) { Start-Process "$destino\ZonaPrivada.exe"; Write-Host 'Lanzador abierto a mano.' }
$ok = $false
for ($i = 0; $i -lt 40 -and -not $ok; $i++) {
    try { $ok = (Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:8090/api/salud' -TimeoutSec 3).StatusCode -eq 200 } catch { Start-Sleep -Seconds 3 }
}
if ($ok) { Write-Host 'Listo: /api/salud responde 200.' -ForegroundColor Green } else { Write-Host 'La API no ha contestado en 2 minutos: mira el icono de la bandeja y los registros.' -ForegroundColor Yellow }

# ---- y desde fuera: la direccion publica (URL_PUBLICA del .env) por el tunel de Cloudflare ----
$envRepo = Join-Path $raiz '.env'
$url = if (Test-Path $envRepo) { (Get-Content $envRepo | Where-Object { $_ -match '^URL_PUBLICA=' } | Select-Object -First 1) -replace '^URL_PUBLICA=', '' } else { '' }
if ($ok -and $url) {
    try { $c = (Invoke-WebRequest -UseBasicParsing ($url.TrimEnd('/') + '/api/salud') -TimeoutSec 10).StatusCode; Write-Host "Publica: $url responde $c." -ForegroundColor Green }
    catch { Write-Host "Publica: $url no responde. Si es la primera vez, instala el tunel con herramientas\instalar-tunel.ps1 y apunta la ruta a http://localhost:8090." -ForegroundColor Yellow }
}
