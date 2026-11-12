# Guía para asistentes de IA

Proyecto personal de Mario: «zona privada», app de un solo usuario (Angular 22 + ASP.NET Core 10 + PostgreSQL, en Docker
en su PC de casa, publicada con Cloudflare Tunnel en https://app.wolvem.dev). Habla con él en **español**.

## Antes de tocar nada

- Cómo levantarlo: [README.md](README.md) (sección «Cómo levantarlo todo»).
- Documentación completa: [docs/](docs/README.md). Arquitectura → backend → API → frontend.
- Lo último que se ha añadido (y qué está sin verificar): [CAMBIOS-RECIENTES.md](CAMBIOS-RECIENTES.md).
- Plan por hacer (30 ideas elegidas, con lo que ya existe y lo que falta de cada una): [docs/checklist-plan7.md](docs/checklist-plan7.md).

## Reglas

- **Nunca** subas ni escribas secretos en el repo: `.env` está ignorado; los tests usan `clave-de-prueba` y bases en memoria.
  No pegues tokens ni contraseñas en archivos, commits ni registros.
- No envíes correos, WhatsApp ni mensajes de Telegram de prueba sin que él lo pida: deja que use «Probar» en la app.
- No hagas `git push` ni publiques nada sin confirmación explícita.
- Código, comentarios, CSS y commits **en español**, imitando el estilo existente (comentarios cortos que explican el porqué).
- Comprueba los cambios: `dotnet test ZonaPrivada.slnx`, `cd web && npx ng build`; para cambios visuales, pruébalo en
  escritorio y en móvil (375 px) con la API en memoria.
- Desplegar = `actualizar.cmd` en el equipo de casa (recompila front + API, levanta todo y vuelve atrás si la API no queda sana).
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
| tareas «por hacer» | `api/Mda.Api/Tareas/Tareas.cs`, `web/src/app/tareas/`, `/tarea` y `/listo` en `Telegram/Conversacion.cs` |
| papeles que caducan · «si pasa X, haz Y» | `api/Mda.Api/Papeles/`, `api/Mda.Api/Automatizaciones/` (nuevo dato a vigilar: `Disparadores`), front `web/src/app/papeles/`, `automatizaciones/` |
| la compra con memoria · webs/servicios vigilados | `api/Mda.Api/Compra/`, `api/Mda.Api/Servicios/`, front `web/src/app/compra/`, `servicios/` |
| descargar un archivo en el PC desde la app | `agente/Mda.Agente/Descargas.cs` + `DescargaLogica.cs` (orden `descargar` en `Pc/Pc.cs`) |
| el cielo de la pantalla de entrar (y su ajuste) · el radar del globo | `web/src/app/core/astro.ts`, `cielo-config.ts`, `entrar/cielo-vivo.ts`, `shell/ajustes-entrada.ts`; radar en `seguridad/globo-sondas.ts` |
| escenas que se ponen solas (llegar a casa, hora, tiempo) | `api/Mda.Api/Contexto/Contexto.cs`, front `web/src/app/contexto/`, y el hook en `Atajos/Atajos.cs` (`/casa`) |
| modo pared · bandeja de entrada del PC · recetas de Siri | `web/src/app/pared/`; `agente/Mda.Agente/Vigias.cs` + `api/Mda.Api/Pc/Bandeja.cs`; [docs/12-siri-y-atajos.md](docs/12-siri-y-atajos.md) |
| última vez de cada vigilancia y alerta de errores 5xx | `api/Mda.Api/Avisos/Vigilancia.cs` |
| recuperar tras perder el equipo / desplegar con vuelta atrás | [docs/11-recuperacion.md](docs/11-recuperacion.md), `herramientas/desplegar.ps1` |
| ubicación del iPhone (simular GPS desde el PC) | `agente/Mda.Agente/Ubicacion*.cs`, `api/Mda.Api/Pc/UbicacionIphone.cs`, front `web/src/app/pc/ubicacion*.ts`, `mapa-ubicacion.*` |
| pánico, sondas y latido | `api/Mda.Api/Seguridad/Panico.cs` |
| medir la velocidad de la galería | `python herramientas/medir-galeria.py` |
