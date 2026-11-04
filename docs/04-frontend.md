# 4. Frontend (Angular 22)

Proyecto: `web/`. Compilación con `@angular/build` (esbuild). Tests con **Vitest** + jsdom.

## Principios

- Componentes **standalone**, **signals** (`signal`, `computed`, `effect`), app **zoneless**,
  control flow nuevo (`@if`, `@for`, `@switch`), `inject()` en lugar de constructores.
- Cada pantalla se carga **lazy** (`loadComponent`). Three.js solo en `entrar`, ECharts solo donde hay gráficas.
- Sin librería de componentes: HTML + CSS propios. Estilos globales en `src/styles.css`, el resto en el `.css` del componente.
- Textos, nombres de clases CSS, variables y comentarios **en español** (`.caja`, `.tarjeta`, `nuevoGasto()`).

## Arranque (`app.config.ts`)

```ts
provideBrowserGlobalErrorListeners(),
provideHttpClient(withFetch(), withInterceptors([sesionCaducada])),   // 401 fuera del login → a /entrar
provideRouter(routes, withComponentInputBinding(), withViewTransitions()),
```

## Rutas (`app.routes.ts`)

| Ruta | Componente | Guard |
|---|---|---|
| `/entrar` | `entrar/entrar.ts` (candado 3D con Three.js, login + código 2FA) | `sinSesion` |
| `/` → `/inicio` | `shell/shell.ts` (marco) con hijos: | `conSesion` |
| `/inicio` | `inicio/inicio.ts` + `inicio/panel.ts` + `inicio/tiempo.ts` | |
| `/gastos` (+ `movimientos`, `fijos`, `huchas`, `gasolina`, `deudas`, `estadisticas`) | `gastos/*` | |
| `/agenda` | `agenda/agenda.ts` | |
| `/notas` | `notas/notas.ts` | |
| `/documentos` | `documentos/documentos.ts` | |
| `/musica`, `/musica/resumen` | `musica/*` | |
| `/perfil`, `/seguridad` | `perfil/perfil.ts`, `seguridad/seguridad.ts` | |
| `/pronto/:id` | `pronto/pronto.ts` (secciones futuras: fotos, proyectos) | |

Query params útiles: `/notas?nueva=1`, `/agenda?nuevo=evento|recordatorio` (abren el diálogo de crear).

## Carpetas

| Carpeta | Contenido |
|---|---|
| `core/` | `sesion.service` (login con `useCookies=true`, 2FA), `guards` (`conSesion`, `sinSesion`, interceptor `sesionCaducada`), `tema.service` (modo y color), `perfil.service` (nombre y foto; si no hay foto subida usa `img/avatar.webp?v=N`), `notificaciones` (Web Push), `instalar` (PWA), `inclinar` (efecto 3D de tarjetas `[data-inclina]`), `asistente.service` (abrir el asistente desde cualquier sitio), `modulos` (menú lateral) |
| `shell/` | `shell.*` (menú lateral **con submenús** Vida / Medios / Mi equipo —se despliegan con altura animada y recuerdan qué está abierto— y **modo compacto** de solo iconos con el submenú flotante, que se cambia con el botón de la cabecera o la tecla `[`; barra superior con **reloj con segundos** en JetBrains Mono, barra de abajo del móvil: tus 3 secciones más usadas —se cuentan las visitas en este navegador— y la sección actual en el centro, dentro de un círculo elevado que sobresale de un hueco redondo, más «Más» para el menú), `fondo.ts` + `fondos.ts` (fondo animado detrás de toda la app: Aura, Líquido, Isométrico, Cómic, Píxel art, Partículas o Nada; se elige en Ajustes con intensidad, velocidad y dónde se ve; los dibujos se descargan solo si hace falta, van a 30 fps y se paran con la pestaña oculta o con «reducir movimiento»), `ajustes-fondo.ts` (su sección en Ajustes), `asistente.ts` (diálogo del asistente con dictado por voz), `buscador.ts` (Ctrl+K o «/»), `mini-musica.ts` (lo que suena en la barra) |
| `inicio/` | saludo con la foto y la hora, mosaico (`panel`), tarjeta del tiempo, KPIs del PC (ECharts + SignalR en `sistema.service`) |
| `gastos/` | `gastos.service` (todas las llamadas + señal `version` para recargar), `modelo.ts` (categorías, euros, fechas), `dialogo-movimiento` y `dialogo-fijo` (crear/editar, con confirmación de borrado en dos toques), pantallas por pestaña, `teclado.ts` (la hoja del diálogo sigue al teclado del iPhone) |
| `agenda/` | `agenda.service` (apariciones, horario, `textoHorario` «L–V · 8:30–17:30»), mes en rejilla, día **por horas** (bloques en columnas si se solapan, banda del horario de trabajo, raya de «ahora», tocar un hueco crea una cita) o **en lista**, diálogo «Mi horario» |
| `notas/` | `markdown.ts` (Markdown propio y seguro → HTML), tarjetas con tareas marcables, editor |
| `documentos/` | `archivos.service` (cola de subidas con progreso), navegador de carpetas, visor, papelera |
| `seguridad/` | sesiones, accesos, 2FA (QR con `qrcode`; el paso pendiente se guarda 15 min en `localStorage` porque el iPhone recarga la PWA al cambiar de app), canales de aviso, vinculación de Telegram |

## Estilos y diseño

- **Tokens CSS** en `:root` (`styles.css`): `--brand`, `--fondo`, `--tarjeta`, `--texto`, `--soft`, `--ok/--warn/--bad`,
  tipografías, escalas fluidas (`--step-*` con `clamp`), radios, curvas (`--ease-*`), «vidrio» (`--vidrio`, `--desenfoque`).
- **Tipografías** locales (`public/fonts`): *Panchang* 700 (títulos, ancha), *Switzer* 400/500 (texto),
  *JetBrains Mono* (código, cifras que no deben bailar como el reloj).
- **Temas**: modo Día / Noche / Sistema y 5 colores (naranja, lima, cobalto, rosa, rojo), guardados en el navegador.
  El cambio de tema se anima con View Transitions en círculo desde el botón; durante esa transición
  `.vt-tema * { view-transition-name: none }` evita que barras y pantalla se superpongan.
- **Mosaico de Inicio** (`panel.css`): `flex-wrap` con ancho mínimo por caja (`flex: 1 1 300px`, dinero y tiempo 420px,
  pequeñas 200px) → cada fila crece hasta el borde salgan las cajas que salgan. En móvil (≤ 640px) las pequeñas van de dos en dos.
- **Móvil**: los diálogos son hojas que suben desde abajo; con un diálogo abierto el fondo no se mueve;
  `input/select/textarea` a 16px mínimo (iOS no hace zoom); campos de fecha/hora con `appearance: none` y
  `min-width: 0` (iOS les impone un ancho propio y se salían del formulario).
- Accesibilidad: `aria-*` en botones de icono, `prefers-reduced-motion` desactiva animaciones, foco visible.

## PWA

- `public/manifest.webmanifest` + iconos (`img/icono-*.png`, `apple-touch-icon.png`).
- `public/sw.js` (service worker propio, caché `mda-vN`):
  - navegación: **red primero**, sin red → la app guardada;
  - estáticos: caché y refresco en segundo plano (*stale-while-revalidate*) → para forzar una imagen nueva se versiona la URL (`avatar.webp?v=3`);
  - `/api` y `/hubs` **nunca** se guardan;
  - `push` → notificación; `notificationclick` → abre/enfoca la app en la ruta del aviso.
- En el iPhone las notificaciones push solo funcionan con la app añadida a la pantalla de inicio.

## Cómo habla con la API

- Siempre rutas relativas (`/api/...`); en desarrollo `proxy.conf.json` las manda a `http://localhost:5195` (y `/hubs` con WebSocket).
- Servicios con `HttpClient` + `firstValueFrom` (async/await). Errores de validación con `erroresDe(err)` → `{ campo: [mensajes] }`.
- Las pantallas se recargan con una señal `version` del servicio (`recargar()`), y el evento global
  `window.dispatchEvent(new Event('mda:agenda'))` avisa a la agenda cuando el asistente crea algo.

## Componentes destacados

- **Asistente** (`shell/asistente.ts`): campo + micrófono (Web Speech API), llama a `/api/asistente/entender`,
  enseña una tarjeta editable según la clase (movimiento, agenda con «Hasta» y «Dónde», hucha, horario con chips de días),
  guarda con las rutas normales y permite **deshacer**. Las respuestas a preguntas se muestran con `white-space: pre-line`.
- **Buscador** (Ctrl+K o «/»): ir a secciones, acciones rápidas, buscar notas.
- **Tiempo** (`inicio/tiempo.ts`): ciudad por búsqueda o geolocalización, horas, semana, interruptor del aviso de la víspera.

## Cargas y esqueletos (estilo HUD)

- **Arranque**: un reactor de anillos que giran, partículas en órbita, una línea que barre y «Iniciando…». Es CSS puro (`.nucleo` en `styles.css`): sale en
  `index.html` antes de que baje Angular y luego lo pinta `core/nucleo.ts` (`<app-nucleo />`); con `mini` es un reactor pequeño para meter dentro de una pantalla.
- **«Cargando…»** (`.cargando`, `.esta-cargando`): mini reactor delante del texto, en todas las pantallas a la vez.
- **Esqueletos** (`.esqueleto`): líneas y tarjetas con un barrido de luz del color de acento.
- Con «reducir movimiento» el reactor queda quieto; las líneas y tarjetas del esqueleto siguen moviéndose porque indican que se está cargando.

## Pantalla de entrar: el candado

`entrar/candado-3d.ts` (Three.js, se carga solo en esa ruta): candado de metal con cuerpo pulido, placa, cilindro con llave, arco de acero y LED de estado; reflejos con un
estudio generado al vuelo (`RoomEnvironment`, sin descargar imágenes); anillos holográficos y de partículas, rejilla y estrellas. Tres animaciones:
`intento()` (LED que parpadea y un anillo que escanea), `abrir()` (la llave gira, LED verde, el arco sube y se abre sobre su pata) y `rechazar()` (LED rojo, sacudida y el arco cae).
Con «reducir movimiento» se ve el resultado sin animar. Al salir libera geometrías, texturas y el contexto WebGL.

## Pantalla de «Face ID al volver»

`shell/bloqueo.ts`: el escáner alrededor de tu foto tiene cuatro estados que se ven a la vez en el anillo, las esquinas, la insignia y el mensaje: **esperando**
(anillo lento), **comprobando** (línea que barre la foto y anillo rápido), **abierto** (todo en verde, 650 ms antes de recargar) y **fallo** (rojo y sacudida). Lleva reloj grande con la
fecha, vibración en el móvil al acertar o fallar, y debajo el botón de Face ID, la contraseña con el código de dos pasos y «Cerrar sesión».

## Reproductor de la barra superior

`shell/mini-musica.ts`: al tocar la portada o el título de lo que suena se abre una **ventana** (no se navega): portada grande con un vinilo que sale y gira, título, artistas y álbum,
progreso con tiempos, anterior / pausa / siguiente, dispositivo y volumen, las tres canciones que vienen después y «Abrir Música» para saltar a la pantalla completa. El color de la
portada tiñe la ventana. Con la ventana abierta pregunta a Spotify cada 5 s (cada 15 s si no).

## Inicio · «Tu PC por dentro»

Ya no va dentro de una tarjeta: el dibujo 3D flota sobre la página con una luz del color de acento detrás y los bordes difuminados, con **píldoras de vidrio** alrededor
(procesador, memoria, disco con su temperatura, red y encendido; en el móvil pasan a una fila debajo). Debajo, tres módulos sin caja: **Batería** (`inicio/bateria.ts`, con tres dibujos a elegir —celda de
barras con la corriente entrando al cargar, anillo y barras— y colores ámbar y rojo con poca carga), **Espacio** (`inicio/espacio.ts`: una barra por disco, ámbar al 85 % y roja al 95 %) y **Ahora mismo**
(procesador, memoria, temperatura del disco, red, volumen y brillo). La temperatura sale de `/api/pc` (la salud que manda el agente). Las estadísticas de debajo se quedan como estaban.
