# Guía para asistentes de IA

Proyecto personal de Mario: «zona privada», app de un solo usuario (Angular 22 + ASP.NET Core 10 + PostgreSQL, en Docker
en su PC de casa, publicada con Tailscale Funnel). Habla con él en **español**.

## Antes de tocar nada

- Cómo levantarlo: [README.md](README.md) (sección «Cómo levantarlo todo»).
- Documentación completa: [docs/](docs/README.md). Arquitectura → backend → API → frontend.

## Reglas

- **Nunca** subas ni escribas secretos en el repo: `.env` está ignorado; los tests usan `clave-de-prueba` y bases en memoria.
  No pegues tokens ni contraseñas en archivos, commits ni registros.
- No envíes correos, WhatsApp ni mensajes de Telegram de prueba sin que él lo pida: deja que use «Probar» en la app.
- No hagas `git push` ni publiques nada sin confirmación explícita.
- Código, comentarios, CSS y commits **en español**, imitando el estilo existente (comentarios cortos que explican el porqué).
- Comprueba los cambios: `dotnet test ZonaPrivada.slnx`, `cd web && npx ng build`; para cambios visuales, pruébalo en
  escritorio y en móvil (375 px) con la API en memoria.
- Desplegar = `docker compose up -d --build api` en el equipo de casa.
- Las pruebas de `PostgresTests` arrancan un PostgreSQL con Testcontainers (hace falta Docker; sin él se saltan). Las migraciones se hacen con
  `dotnet ef migrations add Nombre --output-dir Data/Migraciones` desde `api/Mda.Api`. `SemillaAdmin` tiene que ser el PRIMER servicio de fondo.

## Mapa rápido

| Quiero… | Mira |
|---|---|
| cambiar una ruta de la API | `api/Mda.Api/<Módulo>/*Endpoints.cs` |
| que el asistente/bot entienda una frase nueva | `api/Mda.Api/Asistente/Interprete.cs` + `AsistenteTests.cs` |
| tocar el bot de Telegram | `api/Mda.Api/Telegram/Conversacion.cs` |
| avisos periódicos | `api/Mda.Api/Avisos/Vigilante.cs`, `Agenda/Recordatorios.cs` |
| una pantalla | `web/src/app/<módulo>/` |
| el marco, menú, reloj, asistente, buscador | `web/src/app/shell/` |
| colores, fuentes, tokens | `web/src/styles.css` |
| contenedores | `compose.yaml`, `Dockerfile` |
| copias cifradas y simulacro | `herramientas/copias.sh` (+ `Avisos/Vigilante.cs` para el aviso) |
| el .exe que lo arranca todo | `lanzador/Mda.Lanzador` (lógica pura en `Logica/`, tests en `Mda.Lanzador.Tests`) y el agente en `agente/` |
| la pantalla de fotografía | `web/src/app/fotografia/` (escena en `cielo.ts`, datos en `modelo.ts`) y `api/Mda.Api/Luz/` |
| llaves de acceso | `api/Mda.Api/Auth/Passkeys.cs`, `PasskeyInfo.cs`, `Seguridad/Reautenticar.cs`; front `seguridad/llaves.ts` |
| lo que hace el PC desde la app (casa, concentración, música, red, portapapeles, avisos) | `api/Mda.Api/Pc/Casa.cs`, `Red.cs`, `Portapapeles.cs`; agente: `agente/Mda.Agente/Dispositivos.cs`, `Vigias.cs`, `Ordenes.cs`; front `web/src/app/pc/` |
| la versión de escritorio sin Docker (procesos, seguridad, copias, instalación) | `lanzador/Mda.Lanzador/{ServidorNativo,ProcesoHijo,SeguridadNativa,CopiasNativas,TailscaleNativa}.cs`, lógica en `Logica/`, `herramientas/empaquetar-nativo.ps1`, [docs/09-escritorio.md](docs/09-escritorio.md) |
| pánico, sondas y latido | `api/Mda.Api/Seguridad/Panico.cs` |
| medir la velocidad de la galería | `python herramientas/medir-galeria.py` |
