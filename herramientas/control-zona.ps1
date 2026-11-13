# Parar, iniciar o reiniciar la zona privada de escritorio sin tocar el código ni compilar nada. Lo usa el gestor de servicios; también vale a mano.
#   .\herramientas\control-zona.ps1 parar
#   .\herramientas\control-zona.ps1 iniciar
#   .\herramientas\control-zona.ps1 reiniciar
# Parar y reiniciar necesitan permisos de administrador (la API y PostgreSQL corren con otra cuenta). Nunca hace preguntas.
# Códigos de salida: 0 bien · 2 faltan permisos · 3 no contesta tras iniciar.
param([Parameter(Mandatory = $true, Position = 0)][ValidateSet('parar', 'iniciar', 'reiniciar')][string]$Accion)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\zona-comun.ps1"

if ($Accion -ne 'iniciar' -and -not $esAdmin) { Write-Host 'Para parar hace falta ejecutarlo como administrador.' -ForegroundColor Red; exit 2 }

if ($Accion -in 'parar', 'reiniciar') {
    Parar-Zona
    Write-Host 'Parada.'
}
if ($Accion -in 'iniciar', 'reiniciar') {
    if (Get-Process ZonaPrivada -ErrorAction SilentlyContinue) { Write-Host 'El lanzador ya estaba abierto.' } else { Abrir-Lanzador }
    $s = Esperar-Salud
    if (-not $s.Ok) { Write-Host 'La API no ha contestado en 2 minutos: mira el icono de la bandeja y los registros.' -ForegroundColor Yellow; exit 3 }
    Write-Host "Listo: /api/salud responde 200$(if ($s.Version) { " (versión $($s.Version))" })." -ForegroundColor Green
}
exit 0
