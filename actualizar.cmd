@echo off
rem UN SOLO COMANDO para la zona privada (version de escritorio, sin Docker): compila lo que haya cambiado, para la app,
rem copia lo nuevo, la vuelve a abrir y comprueba que responde. Windows pide permiso de administrador una vez.
rem   actualizar.cmd              todo (web + API + lanzador)
rem   actualizar.cmd -Web         solo el front (no para nada)
rem   actualizar.cmd -Api         API + web
rem   actualizar.cmd -Lanzador    el .exe y el agente del PC
rem   actualizar.cmd -Api -SoloCompilar   compila y no instala
rem   actualizar.cmd -Volver         vuelve a la version anterior (la de antes del ultimo despliegue)
rem Sin nadie delante, llama a herramientas\actualizar-escritorio.ps1 (este .cmd termina con pause); mas en docs\13-compilar-y-actualizar.md
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File herramientas\actualizar-escritorio.ps1 %*
pause
