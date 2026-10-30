@echo off
rem Arranca el agente de la zona privada en segundo plano (lee el token del .env de la carpeta de arriba).
rem La primera vez lo compila; después arranca al momento.
cd /d "%~dp0.."
if not exist "agente\publicado\mda-agente.exe" (
  echo Compilando el agente...
  dotnet publish agente\Mda.Agente -c Release -o agente\publicado --nologo -v q || exit /b 1
)
start "" /min "agente\publicado\mda-agente.exe"
echo Agente arrancado: el panel mostrara los datos reales del PC.
