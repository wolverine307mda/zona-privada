# Actualiza la versión de escritorio ya instalada (C:\Program Files\ZonaPrivada) con lo que haya en el repo, por partes.
# No descarga nada ni toca Docker: reutiliza el PostgreSQL y el age que ya hay instalados.
#
#   .\herramientas\actualizar-escritorio.ps1 -Web          solo la web (no para nada: se nota al recargar la página)
#   .\herramientas\actualizar-escritorio.ps1 -Api          la API (compila también la web, que va dentro)
#   .\herramientas\actualizar-escritorio.ps1 -Lanzador     el ZonaPrivada.exe y el agente del PC que lleva dentro
#   .\herramientas\actualizar-escritorio.ps1 -Todo         las tres cosas (es lo que hace si no pones nada)
#   .\herramientas\actualizar-escritorio.ps1 -Api -SoloCompilar    prepara dist\actualizacion y no instala
#   .\herramientas\actualizar-escritorio.ps1 -Volver       restaura la API y el lanzador que había antes del último despliegue
#
# Para llamarlo sin nadie delante (el gestor de servicios, tareas programadas):
#   -RevertirSiFalla   si tras instalar la API no contesta con la versión nueva, vuelve sola a la anterior
#   -NoReabrir         no abre el lanzador al terminar ni comprueba la salud (quien llama se encarga: el lanzador es un icono de
#                      bandeja y necesita la sesión del usuario, que un servicio no tiene)
# Si ya se ejecuta con permisos de administrador no pide el aviso de Windows; si no, lo pide una vez. Nunca hace preguntas.
#
# Códigos de salida: 0 bien · 1 falló al compilar (no se instala nada) · 2 falló la instalación · 3 instalado pero no contesta (o con otra versión)
#
# Al instalar la API o el lanzador: pide al lanzador que salga con calma (para la API y PostgreSQL como el menú «Salir»); si no puede
# (versión antigua o no responde), lo cierra a la fuerza, pero solo lo SUYO: no toca otros postgres ni otras copias de Mda.Api.
# Antes de sobrescribir guarda lo instalado en C:\ProgramData\ZonaPrivada\anterior para poder volver con -Volver.
# Al arrancar, si la API cambió, hace sola una copia previa de la base (copias\antes-de-actualizar-*.dump) y aplica las migraciones nuevas:
# esas migraciones no se deshacen al volver atrás (docs/11-recuperacion.md).
param([switch]$Web, [switch]$Api, [switch]$Lanzador, [switch]$Todo, [switch]$SoloCompilar, [switch]$Instalar,
      [switch]$NoReabrir, [switch]$RevertirSiFalla, [switch]$Volver)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$paq = Join-Path $raiz 'dist\actualizacion'
. "$PSScriptRoot\zona-comun.ps1"   # $destino, $urlSalud, $esAdmin y las funciones de abrir, parar y comprobar
$anterior = Join-Path $env:ProgramData 'ZonaPrivada\anterior'
if (-not ($Web -or $Api -or $Lanzador -or $Volver)) { $Todo = $true }
if ($Todo) { $Web = $true; $Api = $true; $Lanzador = $true }
if ($Api) { $Web = $true }   # la API lleva la web dentro: si se cambia una, se compila la otra para que no se mezclen versiones

# ---- utilidades ----
function Copiar-Carpeta([string]$de, [string]$a, [string]$que) {
    robocopy $de $a /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy de $que falló ($LASTEXITCODE)" }
}

# El commit (y «-cambios.<hora>» si hay cambios sin confirmar: así cada compilación distinta tiene su propia versión)
function Version-Del-Repo {
    try {
        $hash = git -C $raiz rev-parse --short HEAD | Select-Object -First 1
        if (-not $hash) { return 'desconocida' }
        if (git -C $raiz status --porcelain) { return "$hash-cambios.$(Get-Date -Format 'yyyyMMddHHmm')" }
        return $hash
    } catch { return 'desconocida' }
}

# Lo instalado ahora pasa a ser «anterior» (una sola copia: la del despliegue de antes)
function Guardar-Anterior {
    if (-not (Test-Path "$destino\componentes\api\Mda.Api.exe")) { return }
    New-Item -ItemType Directory -Force -Path $anterior | Out-Null
    Copiar-Carpeta "$destino\componentes\api" (Join-Path $anterior 'api') 'guardar la API anterior'
    if (Test-Path "$destino\ZonaPrivada.exe") { Copy-Item "$destino\ZonaPrivada.exe" (Join-Path $anterior 'ZonaPrivada.exe') -Force }
    $v = (Get-Item "$destino\componentes\api\Mda.Api.dll" -ErrorAction SilentlyContinue).VersionInfo.ProductVersion
    Set-Content -Path (Join-Path $anterior 'version.txt') -Value $v
    Write-Host "Versión anterior guardada ($v)."
}

# Vuelve a lanzar este mismo script con permisos de administrador (sin aviso si ya los tiene) y devuelve su código de salida
function Lanzar-Instalacion([string[]]$extra) {
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Instalar') + $extra
    if ($NoReabrir) { $a += '-NoReabrir' }
    try {
        if ($esAdmin) { $p = Start-Process powershell -ArgumentList $a -Wait -PassThru -NoNewWindow }
        else { $p = Start-Process powershell -Verb RunAs -ArgumentList $a -Wait -PassThru }
    } catch { throw 'Se canceló el permiso de administrador: no se ha instalado nada.' }
    return $p.ExitCode
}

# ---- fase 2 (con permisos de administrador): parar, copiar y volver a abrir ----
if ($Instalar) {
    New-Item -ItemType Directory -Force -Path $paq | Out-Null
    Start-Transcript -Path (Join-Path $paq 'actualizar.log') -Force | Out-Null
    try {
        if ($Volver) {
            if (-not (Test-Path (Join-Path $anterior 'api\Mda.Api.exe'))) { throw 'No hay una versión anterior guardada.' }
            Parar-Zona
            Copiar-Carpeta (Join-Path $anterior 'api') "$destino\componentes\api" 'la API anterior'
            if (Test-Path (Join-Path $anterior 'ZonaPrivada.exe')) { Copy-Item (Join-Path $anterior 'ZonaPrivada.exe') "$destino\ZonaPrivada.exe" -Force }
            Write-Host 'Versión anterior restaurada.'
            if (-not $NoReabrir) { Abrir-Lanzador }
        } else {
            $parar = $Api -or $Lanzador
            if ($parar) { Parar-Zona; Guardar-Anterior }
            if ($Api) { Copiar-Carpeta "$paq\api" "$destino\componentes\api" 'la API' }
            elseif ($Web) { Copiar-Carpeta "$paq\wwwroot" "$destino\componentes\api\wwwroot" 'la web' }
            if ($Lanzador) { Copy-Item "$paq\lanz\ZonaPrivada.exe" "$destino\ZonaPrivada.exe" -Force }
            Write-Host 'Copiado.'
            if ($parar -and -not $NoReabrir) { Abrir-Lanzador }
        }
    } catch { Write-Host "ERROR: $_"; Stop-Transcript | Out-Null; exit 1 }
    Stop-Transcript | Out-Null
    exit 0
}

# ---- fase 1 (sin permisos): compilar ----
New-Item -ItemType Directory -Force -Path $paq | Out-Null
$version = 'desconocida'
if (-not $Volver) {
    $version = Version-Del-Repo
    Write-Host "Versión que se compila: $version" -ForegroundColor Cyan
    $sello = if ($version -ne 'desconocida') { "-p:SourceRevisionId=$version" } else { '-p:Nada=1' }
    $salidaWeb = Join-Path $raiz 'web\dist\web\browser'
    if ($Web) {
        Write-Host '== Web (ng build) ==' -ForegroundColor Cyan
        Push-Location (Join-Path $raiz 'web'); npx ng build; $c = $LASTEXITCODE; Pop-Location
        if ($c -ne 0) { Write-Host 'ng build falló: no se instala nada.' -ForegroundColor Red; exit 1 }
    }
    if ($Api) {
        Write-Host '== API (dotnet publish) ==' -ForegroundColor Cyan
        if (Test-Path "$paq\api") { Remove-Item -Recurse -Force "$paq\api" }
        dotnet publish (Join-Path $raiz 'api\Mda.Api\Mda.Api.csproj') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true $sello -o "$paq\api"
        if ($LASTEXITCODE -ne 0) { Write-Host 'dotnet publish de la API falló: no se instala nada.' -ForegroundColor Red; exit 1 }
        if (Test-Path "$paq\api\wwwroot") { Remove-Item -Recurse -Force "$paq\api\wwwroot" }
        Copy-Item -Recurse $salidaWeb "$paq\api\wwwroot"
        $sellada = (Get-Item "$paq\api\Mda.Api.dll").VersionInfo.ProductVersion
        Write-Host "API compilada: $sellada"
        if ($version -ne 'desconocida' -and $sellada -notlike "*+$version") { Write-Host 'Aviso: la versión de la API no lleva el commit; no se podrá comprobar que arranca la nueva.' -ForegroundColor Yellow; $version = 'desconocida' }
    } elseif ($Web) {
        if (Test-Path "$paq\wwwroot") { Remove-Item -Recurse -Force "$paq\wwwroot" }
        Copy-Item -Recurse $salidaWeb "$paq\wwwroot"
    }
    if ($Lanzador) {
        Write-Host '== Lanzador (dotnet publish) ==' -ForegroundColor Cyan
        if (Test-Path "$paq\lanz") { Remove-Item -Recurse -Force "$paq\lanz" }
        dotnet publish (Join-Path $raiz 'lanzador\Mda.Lanzador\Mda.Lanzador.csproj') -c Release -o "$paq\lanz"
        if ($LASTEXITCODE -ne 0) { Write-Host 'dotnet publish del lanzador falló: no se instala nada.' -ForegroundColor Red; exit 1 }
    }
    if ($SoloCompilar) { Write-Host "Listo en $paq (no se ha instalado nada)." -ForegroundColor Green; exit 0 }
}

# ---- instalar: se vuelve a lanzar este mismo script con permisos de administrador ----
Write-Host $(if ($esAdmin) { '== Instalando ==' } else { '== Instalando (Windows pedirá permiso) ==' }) -ForegroundColor Cyan
$extra = @()
if ($Volver) { $extra += '-Volver' } else { if ($Web) { $extra += '-Web' }; if ($Api) { $extra += '-Api' }; if ($Lanzador) { $extra += '-Lanzador' } }
try { $codigo = Lanzar-Instalacion $extra } catch { Write-Host "$_" -ForegroundColor Red; exit 2 }
Get-Content (Join-Path $paq 'actualizar.log') -ErrorAction SilentlyContinue | Select-Object -Last 6
if ($codigo -ne 0) { Write-Host "La instalación falló (código $codigo): mira $paq\actualizar.log" -ForegroundColor Red; exit 2 }
if ($NoReabrir) { Write-Host 'Instalado. No se ha reabierto el lanzador ni comprobado la salud (-NoReabrir): hazlo quien llama.' -ForegroundColor Green; exit 0 }

# ---- comprobar que responde (y que es la versión nueva) ----
Start-Sleep -Seconds 5
if (($Api -or $Lanzador -or $Volver) -and -not (Get-Process ZonaPrivada -ErrorAction SilentlyContinue)) { Abrir-Lanzador 'a mano' }
$s = Esperar-Salud
$ok = $s.Ok
if ($ok -and $Api -and $version -ne 'desconocida' -and $s.Version -and $s.Version -ne $version) {
    Write-Host "La API contesta, pero con otra versión ($($s.Version)) y se esperaba $version." -ForegroundColor Yellow; $ok = $false
}
if ($ok) { Write-Host "Listo: /api/salud responde 200$(if ($s.Version) { " (versión $($s.Version))" })." -ForegroundColor Green }
else {
    Write-Host 'La API no ha contestado bien en 2 minutos: mira el icono de la bandeja y los registros.' -ForegroundColor Yellow
    if ($RevertirSiFalla -and ($Api -or $Lanzador) -and (Test-Path (Join-Path $anterior 'api\Mda.Api.exe'))) {
        Write-Host 'Volviendo a la versión anterior (-RevertirSiFalla)...' -ForegroundColor Yellow
        Write-Host 'Ojo: si la nueva aplicó migraciones, la base ya está migrada; si algo no va, restaura copias\antes-de-actualizar-*.dump (docs/11).'
        [void](Lanzar-Instalacion @('-Volver'))
        Start-Sleep -Seconds 5
        $r = Esperar-Salud
        if ($r.Ok) { Write-Host 'La versión anterior responde.' -ForegroundColor Green } else { Write-Host 'Tampoco responde la anterior: hay que mirarlo a mano.' -ForegroundColor Red }
    }
    exit 3
}

# ---- y desde fuera: la direccion publica (URL_PUBLICA del .env) por el tunel de Cloudflare ----
$envRepo = Join-Path $raiz '.env'
$url = if (Test-Path $envRepo) { (Get-Content $envRepo | Where-Object { $_ -match '^URL_PUBLICA=' } | Select-Object -First 1) -replace '^URL_PUBLICA=', '' } else { '' }
if ($url) {
    try { $c = (Invoke-WebRequest -UseBasicParsing ($url.TrimEnd('/') + '/api/salud') -TimeoutSec 10).StatusCode; Write-Host "Publica: $url responde $c." -ForegroundColor Green }
    catch { Write-Host "Publica: $url no responde. Si es la primera vez, instala el tunel con herramientas\instalar-tunel.ps1 y apunta la ruta a http://localhost:8090." -ForegroundColor Yellow }
}
exit 0
