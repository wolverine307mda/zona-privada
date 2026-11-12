# Cambios recientes (resumen rápido para asistentes y para Mario)

> Tanda del 6 de noviembre de 2026, rama `claude/private-area-project-review-zc1bju`. Detalle de cada cosa: [docs/10-nuevas-funciones.md](docs/10-nuevas-funciones.md).
> Mapa de dónde está cada módulo: la tabla de [CLAUDE.md](CLAUDE.md).

## 12 de noviembre de 2026 · Cloudflare en lugar de Tailscale

- La app sale a Internet por un túnel de Cloudflare en **https://app.wolvem.dev** (servicio `tunel`, ya sin perfil: `docker compose up -d --build` lo arranca; `CLOUDFLARE_TUNNEL_TOKEN` es obligatorio). Sin Cloudflare Access.
- Quitado de Docker: servicio y volumen `tailscale`, `tailscale-pruebas` (entorno de pruebas, ahora sin salida a Internet), carpeta `tailscale/` y `TS_AUTHKEY`. El cliente de Tailscale de Windows no se ha tocado.
- CORS de `/api/salud` y `/api/publico/*`: ahora `https://wolvem.dev` (portafolio) y `https://wolverine307mda.github.io`.
- Vigilante de la dirección pública renombrado (`VigilanteFunnel` → `VigilanteDireccion`, `EstadoDireccion`, `DireccionResponde`) y sus avisos ahora hablan del túnel de Cloudflare. **Pendiente**: el lanzador de escritorio (`Bandeja.cs`, `TailscaleNativa.cs`, `Logica/Tailscale.cs`) sigue con Tailscale y su menú «Salida a Internet» ya no apaga el túnel. Los volúmenes `zona-privada_tailscale` y `zona-privada_tailscale-pruebas` siguen en Docker hasta que se confirme su borrado.
- Al cambiar de dominio: registrar de nuevo las llaves de acceso, reinstalar la PWA del iPhone (y aceptar las notificaciones) y poner las URL de vuelta de Spotify y Google en sus consolas.

## ⚠️ Lo primero: esto se escribió SIN poder compilar C#

El entorno donde se hizo no tenía .NET. El **front sí se compiló, se probó (`ng build`, 82 tests Vitest) y se vio en un navegador**. El **C# (API y agente), las migraciones y sus tests están sin compilar ni ejecutar**.
Antes de desplegar, en el equipo de casa:

1. `git pull` de la rama y `dotnet test ZonaPrivada.slnx` (+ `cd lanzador && dotnet test` para los tests del agente). Arregla lo que salga rojo: lo más probable son erratas de compilación, no de diseño.
2. **Migraciones escritas a mano** (+ su snapshot): `Tareas` (ya con la columna `Repite`), `Papeles`, `Automatizaciones`, `Servicios`, `Compra`, `Contexto`. Si alguna falla al aplicarse, bórrala (y su `.Designer.cs`) y genérala bien con
   `dotnet ef migrations add Nombre --output-dir Data/Migraciones` desde `api/Mda.Api`. Se aplican solas al arrancar la API.
3. Desplegar con red de seguridad: `.\herramientas\desplegar.ps1` (si la API nueva no está sana, vuelve sola a la anterior). **Reinicia también el agente de Windows** (hay código nuevo: descargas y bandeja).
4. Opcional en `.env`: `COPIAS_DRIVE` (carpeta de Google Drive para escritorio) y `AGENTE_BANDEJA` (otra carpeta para la bandeja). Ver `.env.example`.

## Qué se ha añadido

| Qué | Dónde se usa | Código |
|---|---|---|
| **Tareas** «por hacer» (con repetición diaria/semanal/mensual/anual, aviso diario, `/tarea` `/tareas` `/listo` en Telegram) | `/tareas` | `api/Mda.Api/Tareas/`, `web/src/app/tareas/` |
| **Papeles que caducan** (DNI, ITV, seguros…; avisos escalonados; `/caducidades`) | `/papeles` | `api/Mda.Api/Papeles/` |
| **Automatizaciones** «si pasa X, haz Y» (disco, CPU, RAM, gasto del mes, tareas vencidas, papeles → avisar o crear tarea) | `/automatizaciones` | `api/Mda.Api/Automatizaciones/` (nuevo dato: `Disparadores`) |
| **Servicios vigilados** (webs y puertos cada 2 min; aviso de caída y de vuelta) | `/servicios` | `api/Mda.Api/Servicios/` |
| **Compra con memoria** (aprende cada cuánto compras cada cosa; «ya toca»; `/compra` `/comprado`) | `/compra` | `api/Mda.Api/Compra/` |
| **Descargar en el PC** (orden `descargar`, con Face ID; sin red local ni programas; 4 GB) | Mi PC | `agente/Mda.Agente/Descargas.cs`, `DescargaLogica.cs`, `Pc/Pc.cs` |
| **Escenas por contexto** (llegar/salir de casa, hora, tiempo → escena de casa + del PC + aviso; las que apagan el PC nunca solas) | `/contexto` («Escenas solas») | `api/Mda.Api/Contexto/`, hook en `Atajos/Atajos.cs` (`/casa`) |
| **Cielo en vivo** en la pantalla de entrar y de bloqueo (Sol y Luna reales; configurable por dispositivo en Ajustes) | `/entrar`, bloqueo, Ajustes | `web/src/app/core/astro.ts`, `cielo-config.ts`, `entrar/cielo-vivo.ts`, `shell/ajustes-entrada.ts` |
| **Radar en vivo** en el globo de sondas (un rayo hasta tu casa por cada intento; **en directo** por SignalR al entrar) | Seguridad › Vigilancia | `seguridad/globo-sondas.ts`, `Seguridad/AccesoVivo.cs` |
| **Modo pared** (sala de control para una tablet: reloj, tiempo, PC, pendientes, servicios) | `/pared` | `web/src/app/pared/` |
| **Bandeja de entrada del PC** (carpeta `Zona privada\Bandeja` vigilada por el agente) | Mi PC | `agente/Mda.Agente/Vigias.cs`, `api/Mda.Api/Pc/Bandeja.cs` |
| **Vigilancia visible** (última vez de cada comprobación) y **alerta de errores 5xx** (≥ 5 en 10 min → Telegram) | Seguridad | `api/Mda.Api/Avisos/Vigilancia.cs` |
| **Copia cifrada también en Google Drive** y **el `.env` cifrado** junto a la base | copias | `herramientas/copias.sh`, `compose.yaml` |
| **Despliegue con vuelta atrás** | línea de comandos | `herramientas/desplegar.ps1` / `.sh` |
| **Plan de recuperación** (qué hacer si muere el PC) | docs | [docs/11-recuperacion.md](docs/11-recuperacion.md) |
| **Recetas de Siri/Atajos** del iPhone | docs | [docs/12-siri-y-atajos.md](docs/12-siri-y-atajos.md) |
| Plantilla/estilos del asistente y de Mi PC sacados a `.html`/`.css`; arreglado el punto duplicado de `docs/07-seguridad.md` | — | `web/src/app/shell/asistente.*`, `pc/pc.html` |

Menú: «Vida» = Cartera, Compra, Agenda, Tareas, Papeles, Hábitos, Notas · «Mi equipo» = Mi PC, Casa, Modo pared, Escenas solas, Servicios, Automatizaciones, Documentos, Seguridad.

## Rutas nuevas de la API (todas con sesión salvo lo del agente y los atajos)

`/api/tareas` · `/api/papeles` · `/api/automatizaciones` · `/api/servicios` · `/api/compra` · `/api/contexto` · `/api/vigilancia` · `/api/pc/bandeja` ·
orden `descargar` en `POST /api/pc/ordenes` · evento `bandeja` en `POST /api/sistema/agente/eventos` · evento SignalR `acceso` en `/hubs/sistema` ·
`/api/seguridad/accesos` ahora devuelve `lat`/`lon` · `/api/atajos/casa` ahora también dispara las escenas por contexto.

## Cosas a tener presentes

- **Telegram nuevo**: `/tarea`, `/tareas`, `/listo`, `/caducidades`, `/compra`, `/comprado` (ya están en `/ayuda`).
- **Cielo en vivo** es un ajuste **de cada dispositivo** (`localStorage`), no del servidor: la pantalla de entrar se ve antes de tener sesión. Por defecto sigue el candado 3D.
- **La bandeja** guarda los 50 últimos en memoria: se vacía al reiniciar la API (los archivos están en el PC).
- **«Enciende el PC» desde Siri no se puede** si el PC es el que corre la zona privada (ver [docs/12-siri-y-atajos.md](docs/12-siri-y-atajos.md)); sí despertar otros aparatos.
- **Salida a Internet**: ya no es Tailscale Funnel sino un túnel de Cloudflare en `https://app.wolvem.dev` (ver abajo, «Cloudflare en lugar de Tailscale»).
- No se envió ningún mensaje de prueba real (Telegram, correo, WhatsApp) en esta tanda.

## Ideas que quedaron sobre la mesa (no hechas)

Modo coche con Bluetooth · cielo en vivo también en Inicio · planetas visibles · «Wrapped» anual · gemelo 3D de la casa · gasto por foto de ticket (OCR) · exportar la cartera a CSV · detectar suscripciones ·
copia de tus Documentos con `restic` · interruptor de «hombre muerto» · horas baratas de luz.
