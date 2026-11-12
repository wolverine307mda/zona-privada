@echo off
rem Alternativa con Docker (no es la version de escritorio): recompila todo (front + API) y vuelve a levantar la zona privada con red de seguridad.
rem Si la API nueva no arranca, vuelve sola a la anterior. Uso:  actualizar.cmd   (o  actualizar.cmd -Volver)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File herramientas\desplegar.ps1 %*
