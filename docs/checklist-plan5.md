# Checklist · Plan 5 (versión de escritorio nativa, sin tocar Docker)

Estados: ⬜ pendiente · ⏭️ próximo · 🔄 en proceso · ✅ hecho (con el commit).
Regla: Docker sigue funcionando igual en todo momento (no se ejecuta `compose down` ni se tocan sus puertos, volúmenes ni `.env`); un commit por tarea; sin `git push`.

- ✅ F1 Cambios mínimos en la API (rutas por carpeta, cookie, registro a fichero, mensajes de copias)
- ✅ F2 Orquestador nativo en el lanzador (procesos hijos, Postgres portable, API, agente)
- ✅ F3 Blindaje (usuario sin privilegios, ACL, firewall, Job Object, DPAPI, hashes)
- ✅ F4 Copias y simulacro en C#
- ✅ F5 Tailscale nativo
- ✅ F6 Empaquetado e instalación
- ✅ F7 Importar desde Docker (solo lectura) y documentación
