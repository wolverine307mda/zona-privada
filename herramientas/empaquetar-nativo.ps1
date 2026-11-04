# Empaqueta la version de escritorio (sin Docker): dist\ZonaPrivada\ZonaPrivada.exe + componentes\{api,postgres,age}
#
#   powershell -ExecutionPolicy Bypass -File herramientas\empaquetar-nativo.ps1 -Descargas C:\ruta\con\los\zips
#
# Lo que NO hace: bajar nada de Internet. PostgreSQL y age los bajas tu de su web oficial (herramientas\componentes.json) y los dejas en
# la carpeta -Descargas. El script comprueba su SHA-256 contra componentes.json y se niega a usarlos si no coincide o si el hash esta vacio.
# Tampoco toca Docker ni nada instalado: todo se genera dentro de dist\.

param(
    [Parameter(Mandatory = $true)][string]$Descargas,
    [string]$Salida = "",
    [switch]$SinWeb
)
$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot
if ($Salida -eq "") { $Salida = Join-Path $raiz "dist\ZonaPrivada" }
$manifiesto = Get-Content (Join-Path $PSScriptRoot "componentes.json") -Raw | ConvertFrom-Json

function Comprobar-Archivo($nombre, $entrada) {
    $zip = Get-ChildItem -Path $Descargas -Filter $entrada.archivo -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $zip) { throw "Falta el archivo de $nombre ($($entrada.archivo)) en $Descargas. Bajalo de $($entrada.web_oficial)" }
    $hash = (Get-FileHash -Algorithm SHA256 -Path $zip.FullName).Hash.ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($entrada.sha256)) {
        throw "El hash de $nombre no esta fijado en componentes.json. SHA-256 de $($zip.Name): $hash`nCompruebalo contra la web oficial ($($entrada.web_oficial)) y, si coincide, ponlo en componentes.json."
    }
    if ($hash -ne $entrada.sha256.ToLowerInvariant()) { throw "El SHA-256 de $($zip.Name) NO coincide con componentes.json (esperado $($entrada.sha256), real $hash). No se usa." }
    Write-Host "OK  $nombre  $($zip.Name)  $hash"
    return $zip
}

$zipPg = Comprobar-Archivo "PostgreSQL" $manifiesto.postgres
$zipAge = Comprobar-Archivo "age" $manifiesto.age

if (Test-Path $Salida) { Remove-Item -Recurse -Force $Salida }
$componentes = Join-Path $Salida "componentes"
New-Item -ItemType Directory -Force -Path $componentes | Out-Null

# 1) API autocontenida para Windows, con el front de Angular dentro de wwwroot
if (-not $SinWeb) {
    Push-Location (Join-Path $raiz "web"); npm ci --ignore-scripts; npx ng build --configuration production; Pop-Location
}
dotnet publish (Join-Path $raiz "api\Mda.Api\Mda.Api.csproj") -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o (Join-Path $componentes "api")
$web = Join-Path $raiz "web\dist\web\browser"
if (Test-Path $web) {
    $destinoWeb = Join-Path $componentes "api\wwwroot"
    if (Test-Path $destinoWeb) { Remove-Item -Recurse -Force $destinoWeb }
    Copy-Item -Recurse $web $destinoWeb
} else { Write-Warning "No hay build del front en $web: la API no tendra web." }

# 2) El lanzador (un solo exe)
dotnet publish (Join-Path $raiz "lanzador\Mda.Lanzador\Mda.Lanzador.csproj") -c Release -o $Salida

# 3) PostgreSQL: solo bin, lib y share (sin pgAdmin, documentacion ni simbolos)
$tmp = Join-Path $env:TEMP ("zp-pg-" + [guid]::NewGuid().ToString("N"))
Expand-Archive -Path $zipPg.FullName -DestinationPath $tmp
$pgsql = Get-ChildItem $tmp -Directory | Select-Object -First 1
foreach ($d in "bin", "lib", "share") { Copy-Item -Recurse (Join-Path $pgsql.FullName $d) (Join-Path $componentes "postgres\$d") }
Remove-Item -Recurse -Force $tmp

# 4) age
$tmp = Join-Path $env:TEMP ("zp-age-" + [guid]::NewGuid().ToString("N"))
Expand-Archive -Path $zipAge.FullName -DestinationPath $tmp
$ageExe = Get-ChildItem $tmp -Recurse -Filter "age.exe" | Select-Object -First 1
New-Item -ItemType Directory -Force -Path (Join-Path $componentes "age") | Out-Null
Copy-Item $ageExe.FullName (Join-Path $componentes "age\age.exe")
Remove-Item -Recurse -Force $tmp

# 5) Quien es quien
@"
ZonaPrivada.exe        lanzador (bandeja, agente del PC, orquestador)
componentes\api        API autocontenida + web
componentes\postgres   $($zipPg.Name)  sha256 $($manifiesto.postgres.sha256)
componentes\age        $($zipAge.Name)  sha256 $($manifiesto.age.sha256)
"@ | Set-Content -Encoding UTF8 (Join-Path $componentes "VERSIONES.txt")

Write-Host ""
Write-Host "Listo: $Salida"
Write-Host "Para instalar: abre una consola de administrador y ejecuta  `"$Salida\ZonaPrivada.exe`" --instalar-escritorio"
