# Cambios recientes (resumen rápido para asistentes y para Mario)

> Tanda del 6 de noviembre de 2026, rama `claude/private-area-project-review-zc1bju`. Detalle de cada cosa: [docs/10-nuevas-funciones.md](docs/10-nuevas-funciones.md).
> Mapa de dónde está cada módulo: la tabla de [CLAUDE.md](CLAUDE.md).

## 14 de noviembre de 2026 · Mover la app al mini PC (guía y script)

- Nueva guía [docs/14-mover-al-mini-pc.md](docs/14-mover-al-mini-pc.md) y script `herramientas/mover-equipo.ps1`: **`-Exportar`** (en el portátil) deja la base, las claves, la configuración (con `MDA_CLAVE_MAESTRA`) y los ajustes del lanzador **cifrados con age**; **`-Restaurar`** (en el mini PC) lo coloca en dos pasadas (1.ª: configuración y claves, antes del primer arranque; 2.ª: la base, con copia de lo que hubiera y comprobación de usuarios y tablas). Pide administrador, no escribe secretos en claro fuera de una carpeta temporal que borra, y rechaza una clave privada equivocada o una clave pública de age vacía.
- **Hueco cerrado:** las copias cifradas diarias de la versión de escritorio **no incluían la configuración**, o sea, sin `MDA_CLAVE_MAESTRA` las notas y las claves guardadas no se leían tras perder el equipo. Ahora suben también `zonaprivada-AAAAMMDD-entorno.age` (diarias y semanales), como ya hacía `copias.sh`. Hace falta **actualizar el lanzador** (`actualizar-escritorio.ps1 -Lanzador`) para que lo haga.
- `instalar-tunel.ps1` también busca `CLOUDFLARE_TUNNEL_TOKEN` en la configuración de escritorio (`C:\ProgramData\ZonaPrivada\config\.env`), que es donde queda en un equipo nuevo.
- **Verificado:** prueba de extremo a extremo con dos PostgreSQL desechables y age real (exportar, 1.ª y 2.ª pasada, nada en claro, datos iguales, casos malos), `Mda.Lanzador.Tests` (324 verdes) y la sintaxis de los scripts. Esa prueba **no toca la app real** (`-Prueba`).
- **Sin verificar:** nada de esto se ha ejecutado en un mini PC real ni contra la app instalada (leer su configuración pide administrador, y no lo lancé yo). Probar `-Exportar` en el portátil a una carpeta temporal es seguro y es lo primero que conviene hacer. El corte con el túnel de Cloudflare solo se puede probar el día del cambio.

## 13 de noviembre de 2026 · Tipo de equipo y maqueta 3D del mini PC## 13 de noviembre de 2026 · Tipo de equipo y maqueta 3D del mini PC

- Nuevo ajuste **Ajustes › «Tipo de equipo»**: portátil o mini PC. Con **mini PC**, «Tu PC por dentro» (Inicio y PC) dibuja un mini PC en 3D, separado por secciones como el portátil: carcasa (base con puertos y piloto), placa, procesador, memoria, disco, ventilador con disipador y tapa; sin batería ni gráfica aparte. El despiece, las vistas y las etiquetas siguen funcionando, y cambiarlo vuelve a dibujar la escena al momento.
- Se guarda en el servidor (`GET/PUT /api/equipo`, ajuste `equipo-tipo`), no en el navegador: al pasar la app al mini PC solo hay que elegirlo. Por defecto sigue siendo el portátil. Detalle en [docs/04-frontend.md](docs/04-frontend.md).
- **Verificado:** `TipoEquipoTests` (5), suite de la API, `ng test` (250) y `ng build`; en el navegador, el cambio de portátil a mini PC y vuelta, el despiece, las etiquetas y la sección de Ajustes en móvil (375 px).
- **Sin verificar:** cómo se ve en tu pantalla real con el tema claro y con otros colores (solo lo vi en oscuro y naranja), y el rendimiento en el móvil.

## 13 de noviembre de 2026 · Varias cuentas de Google y calendario por defecto

- Agenda › **«Cuentas y calendarios»**: se pueden enlazar varias cuentas de Google, marcar qué calendarios de cada una se ven (con su color) y elegir el **calendario por defecto** donde se copian los eventos nuevos (también los del bot de Telegram). En el diálogo de un evento sale un selector de calendario cuando hay más de uno.
- Migración nueva `GoogleVariasCuentas` (dos columnas en `eventos`: `GoogleCuenta`, `GoogleCalendario`). Los eventos que ya estaban copiados siguen en la primera cuenta y su calendario principal. La cuenta ya enlazada sigue siendo la misma (`google`).
- API: `GET /api/agenda/estado` ahora trae `google.cuentas` y `google.defecto`; nuevo `PUT /api/agenda/google`; `DELETE /api/conexiones/{clave}` quita solo una cuenta. Detalle en [docs/05-integraciones.md](docs/05-integraciones.md).
- **Verificado:** tests (`GoogleCuentasTests`, 56 de agenda/conexiones verdes; `ng test` 250 verdes), `ng build` y el diálogo y el selector en móvil (375 px) con la API en memoria y las cuentas **simuladas** en el navegador.
- **Sin verificar (necesita Google real):** enlazar de verdad una segunda cuenta, que Google devuelva la lista de calendarios, copiar y mover eventos entre cuentas. Pide `prompt=select_account consent`, así que al enlazar sale el selector de cuentas. Hay que **desplegar** para probarlo.

## 13 de noviembre de 2026 · Atajos: copiar el token o el prompt completo

- En Seguridad › Atajos, al crear un token hay dos botones: **«Copiar solo el token»** y **«Copiar prompt completo»** (un texto para pegar en la app Atajos del iPhone con la dirección, el token, el cuerpo JSON y las acciones paso a paso). Se elige qué atajo montar entre los que permite el token y la dirección que usará el iPhone.
- Los tokens que ya existen tienen **«Prompt para un atajo…»**, que copia el prompt sin token (con `PEGA_AQUI_TU_TOKEN`).
- Nuevo `GET /api/atajos-tokens/direccion` (con sesión): devuelve `App:UrlPublica` para rellenar la dirección. Recetas y generador en `web/src/app/seguridad/atajos-prompt.ts` (+ `.spec.ts`). Además, la lista de permisos ya incluye «personas» y «salud», y `/estado` se enseña como GET.
- **Verificado:** `ng test` (246 verdes), `ng build`, tests de `AtajosTests` (13) y la pantalla en móvil (375 px) con la API en memoria.
- **Sin verificar:** que Apple Intelligence monte el atajo entero a partir del prompt (hay que probarlo en el iPhone) y la dirección pública real tras desplegar.

## 13 de noviembre de 2026 · Despliegue sin manos (paso 1 del gestor de servicios)

Preparación para una app aparte (el gestor de servicios, que irá en `serv.wolvem.dev`) que recompile y despliegue con un botón. Nada de esto cambia cómo se usa `actualizar.cmd` a mano.

- `/api/salud` ahora dice también `version` (el commit con el que se compiló; con cambios sin confirmar, `abc1234-cambios.<fecha>`). Código en `api/Mda.Api/Sistema/VersionApp.cs`, tests en `VersionTests.cs`. Es pública como el resto de `/api/salud`.
- `herramientas/actualizar-escritorio.ps1` reescrito: sin aviso de administrador si ya los tiene, sin preguntas, **ya no mata cualquier `postgres` o `Mda.Api`** (solo los que viven en `Archivos de programa\ZonaPrivada`), guarda lo instalado en `C:\ProgramData\ZonaPrivada\anterior` y nuevos interruptores `-Volver`, `-RevertirSiFalla` y `-NoReabrir`. Comprueba que contesta **la versión nueva**, no solo un 200. Códigos de salida: 0 bien, 1 compilación, 2 instalación, 3 no contesta.
- `ZonaPrivada.exe --salir` (y un evento `Global\ZonaPrivada.Lanzador.Salir`): el lanzador sale con calma, como el menú «Salir» (para la API y PostgreSQL con `pg_ctl stop -m fast`). El script lo usa antes de cambiar archivos.
- **Verificado:** `dotnet test ZonaPrivada.slnx -c Release` (324 + 1641 verdes), la sintaxis del `.ps1`, `-Api -SoloCompilar` (estampa `1.0.0+<commit>` en la API) y `--salir` con y sin evento.
- Nuevo `herramientas/control-zona.ps1` (`parar|iniciar|reiniciar`) y `herramientas/zona-comun.ps1` (lo que comparte con `actualizar-escritorio.ps1`: parar solo lo suyo, abrir el lanzador, comprobar la salud). Probado: sintaxis y que `actualizar-escritorio.ps1 -Web -SoloCompilar` sigue compilando (también lanzado desde el gestor, con la salida en vivo); **parar/iniciar/reiniciar no se han ejecutado contra la app real**.
- El **gestor de servicios** ya existe como proyecto aparte en `C:\Users\wolve\Proyectos\gestor-servicios` (API + Ejecutor + panel + PostgreSQL propio, entrada solo con passkey; irá en `serv.wolvem.dev`): ver su README. Para desplegar desde su botón hay que instalarlo (`instalar-gestor.ps1`, como administrador).
- **Sin verificar (hay que probarlo en el primer despliegue real):** la instalación completa con el script nuevo, la salida con calma del lanzador recibiendo la señal, `-Volver` y `-RevertirSiFalla`. El lanzador instalado hoy es antiguo y no entiende la señal: **el primer despliegue con `-Lanzador` lo cierra a la fuerza** (el script lo avisa); desde el siguiente, sale con calma. Conviene hacer ese primero con `-Lanzador` a mano y mirando.

## 13 de noviembre de 2026 · Tema por defecto del portfolio desde Ajustes

- Nueva sección **«Tema del portfolio»** en Ajustes: modo (día/noche/sistema) y color por defecto de wolvem.dev para todos los visitantes, guardado en el servidor (ajuste `web-tema`) y no en cada navegador.
- API: `GET/PUT/DELETE /api/tema-web` (con sesión) y `GET /api/publico/tema` (público, CORS del portfolio, 404 mientras no haya tema fijo). Código en `api/Mda.Api/Galeria/TemaWeb.cs`, tests en `TemaWebTests.cs`.
- El portfolio guarda la última respuesta y la pinta al instante en la siguiente visita (aunque el equipo esté apagado); lo que cada visitante elija con el pincel manda solo para él y tiene un botón «Volver al tema de la web».
- Para que se vea hay que **desplegar con `actualizar.cmd`** y fijar el tema en Ajustes; hasta entonces la web se comporta como siempre.

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
