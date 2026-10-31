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
