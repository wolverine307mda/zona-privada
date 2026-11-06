# Despliega la API con red de seguridad: guarda la imagen actual, reconstruye, espera a que /api/salud conteste
# y, si no arranca, vuelve sola a la versión anterior. Uso (desde la raíz del repo):  .\herramientas\desplegar.ps1
#   -Volver   : no reconstruye, solo vuelve a la imagen guardada como «anterior»
param([switch]$Volver)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Esperar-Salud {
    # el contenedor trae su propio healthcheck (/api/salud): lo usamos en vez de abrir el puerto a mano
    for ($i = 0; $i -lt 40; $i++) {
        $id = docker compose ps -q api
        if ($id) {
            $estado = docker inspect --format '{{.State.Health.Status}}' $id 2>$null
            if ($estado -eq 'healthy') { return $true }
            if ($estado -eq 'unhealthy') { return $false }
        }
        Start-Sleep -Seconds 3
    }
    return $false
}

function Volver-Atras($imagen) {
    Write-Host "Volviendo a la versión anterior ($imagen`:anterior)..." -ForegroundColor Yellow
    docker tag "$imagen`:anterior" "$imagen`:latest"
    docker compose up -d --no-build api
    if (Esperar-Salud) { Write-Host 'Versión anterior en marcha.' -ForegroundColor Green } else { Write-Host 'La anterior tampoco arranca: mira «docker compose logs api».' -ForegroundColor Red }
}

$contenedor = docker compose ps -q api
if ($contenedor) {
    $imagen = (docker inspect --format '{{.Config.Image}}' $contenedor).Split(':')[0]
    $id = docker inspect --format '{{.Image}}' $contenedor
    if ($Volver) { Volver-Atras $imagen; exit }
    docker tag $id "$imagen`:anterior"   # la versión que funciona ahora, por si la nueva falla
    Write-Host "Guardada la versión actual como $imagen`:anterior" -ForegroundColor Cyan
} elseif ($Volver) { Write-Host 'No hay contenedor de la API.' -ForegroundColor Red; exit 1 }

docker compose up -d --build api
if (-not (Esperar-Salud)) {
    Write-Host 'La API nueva no está sana tras el despliegue.' -ForegroundColor Red
    docker compose logs --tail 40 api
    if ($contenedor) { Volver-Atras $imagen } else { Write-Host 'No había versión anterior a la que volver.' -ForegroundColor Red }
    exit 1
}
Write-Host 'Desplegado y sano.' -ForegroundColor Green
# Nota: las migraciones de la base se aplican solas al arrancar. Si la nueva versión añadió tablas, la anterior las ignora sin problema;
# si cambió o borró columnas, restaura también la copia (docs/recuperacion.md).
