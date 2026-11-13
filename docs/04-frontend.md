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
| `/gastos` (+ `movimientos`, `importar`, `fijos`, `huchas`, `gasolina`, `deudas`, `estadisticas`) | `gastos/*` | |
| `/agenda` | `agenda/agenda.ts` | |
| `/notas` | `notas/notas.ts` | |
| `/documentos` | `documentos/documentos.ts` | |
| `/fotos` | `galeria/galeria.ts` | |
| `/musica`, `/musica/resumen` | `musica/*` | |
| `/fotografia` (+ `previsiones`, `sitios`, `herramientas`, `ar`) | `fotografia/*` (el cielo en `cielo.ts`, los datos en `modelo.ts`) | |
| `/pc` | `pc/pc.ts` (pestañas Equipo, Controles y Casa y red) | |
| `/casa` | `casa/casa.ts` (domótica propia: habitaciones, aparatos, escenas y automatizaciones) | |
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

- **Tokens CSS** en `:root` (`styles.css`): `--brand` y `--brand-2`, `--fondo`, `--lateral`, `--tarjeta`, `--tarjeta-2`, `--texto`, `--soft`,
  `--ok/--warn/--bad` (con variante de día), escala de radios (`--radio-s/m/l/xl` = 12/20/28/36 px y `--radio-pildora`; `--radio` es un alias heredado de
  `--radio-l`), espacios (`--esp-1..7`), `--tactil` (44 px), sombras (`--sombra`, `--sombra-flota`, `--sombra-pulsa`), la tarjeta negra (`--negra`, `--on-negra`…),
  escalas fluidas (`--step-*`, `--step-saludo`, `--step-cifra`), curvas y duraciones (`--ease-*`, `--dur-*`, `--stagger`).
- **Superficies casi sólidas**: las tarjetas van rellenas pero con un 90 % de opacidad (`--vidrio`; `--vidrio-fuerte` 95 %) para que se intuya el fondo, y las negras,
  que son del color del fondo, un 84 % (`--negra-vidrio`; 93 % de día, porque sobre un fondo claro se verían grises). Sin desenfoque (`--desenfoque: none`: en el móvil pesa).
  `--tarjeta` sigue siendo opaca y es la que usan los diálogos, las barras fijas y los campos.
- **Tipografías** locales (`public/fonts`): *Switzer* 300 (cifras y saludos grandes), 400 y 500 (texto y títulos de tarjeta), *Panchang* 700 (solo marca y rótulos
  que se quieran muy anchos; ya no se usa para cifras) y *JetBrains Mono* (código, cifras que no deben bailar como el reloj). Clase `.cifra` para una cifra grande ligera con su unidad.
- **Paletas** (Ajustes → Color y paleta): ocho colores (morado, rojo, naranja, ámbar, lima, turquesa, azul y rosa) y **cada uno es una gama de cinco tonos** del mismo color,
  `--t1` (el más claro) a `--t5` (el más hondo); `--brand` es el central. Cada tipo de elemento usa el suyo: `--tono-titulo` (títulos de tarjeta y de sección), `--tono-icono` y
  `--tono-icono-fondo` (iconos en círculo), `--tono-chip` (chips y contadores), `--brand-2` (el claro: fechas del cabecero, segunda serie de una gráfica) y `--brand-3` (el hondo);
  en noche se usan los tonos claros y de día los hondos. Además la paleta tiñe las superficies con su matiz (`--tinte`, fuerza `--tn`). Se definen en `styles.css`
  (`[data-color=…]`) y se listan en `core/tema.service.ts` (`COLORES`); los nombres de las primeras paletas (seis morados) se guardan como «morado». Modo Día / Noche / Sistema aparte, todo guardado
  en el navegador. Contraste comprobado en noche y en día (texto ≥ 12:1, texto atenuado ≥ 5:1, botones ≥ 4,5:1).
  El cambio de tema se anima con View Transitions en círculo desde el botón; durante esa transición `.vt-tema * { view-transition-name: none }` evita que barras y pantalla se superpongan.
- **Fondos** (Ajustes → Fondo): 33 en tres grupos —**clásicos** (aura, líquido, isométrico, cómic, píxel art, partículas, boreal, bokeh, olas, estrellas, rejilla, nada), **nuevos** (nebulosa,
  plasma, seda, lluvia digital, luciérnagas, hexágonos, circuitos, caleidoscopio, sonar, flujo, cristales, nevada, cometas, haces) y **superhéroes** (multiverso, reactor, gamma, tormenta, místico,
  vibranio, cósmico: dibujos originales inspirados en el universo Marvel, sin personajes, logos ni imágenes con derechos, y con colores propios)—, con intensidad, **fuerza del color** (50-200 %:
  más saturado y más opaco), velocidad y dónde se ven (todo, páginas o menú). Van en una capa `z-index: -1` (`shell/fondo.ts`; las escenas, en `fondos.ts` —clásicos—, `fondos-nuevos.ts`,
  `fondos-marvel.ts` y lo común en `fondos-base.ts`, que se descargan solo si se eligen; el aura en `body::before`). Se dibujan **a la resolución de la pantalla** (hasta 2× en ordenador, 1,5× en el móvil)
  y cada miniatura de Ajustes es el fondo de verdad dibujado en pequeño. **El color de fondo va solo en `html`**, porque si el `body` también lo pinta tapa esas capas y ningún fondo se ve. En el móvil
  (`pointer: coarse` o menos de 760 px) cada escena dibuja menos cosas (`ligero`), a 20 fotogramas, sin máscara, con la altura `100lvh` (no cambia al esconderse la barra del navegador) y **se queda quieta mientras haces scroll**.
- **Colores para Three y ECharts**: leen las variables con `getPropertyValue`, que devuelve el texto sin resolver (`color-mix(...)`); `core/color-css.ts` (`resolverColor`) le pide
  al navegador el color resuelto y lo pasa a `#rrggbb`. Las escenas 3D piden el siguiente fotograma antes de dibujar y se saltan uno que falle: una excepción ya no las deja en blanco.

## Sistema de diseño: `web/src/app/ui/`

Componentes standalone y `OnPush` que usan todas las pantallas (se importan de `ui/index.ts`). Documentados con un comentario en cada archivo.

| Componente | Para qué |
|---|---|
| `<app-tarjeta>` | superficie con variantes `vidrio` (sólida con sombra), `plana`, `negra` (alto contraste) y `acento`; radios `m`, `l`, `xl` |
| `<app-esqueleto>` | hueco de carga con barrido de luz (`linea`, `bloque`, `circulo`, `grafica`); reserva el alto final. Con movimiento reducido late en vez de barrer |
| `<app-estado-vacio>` / `<app-estado-error>` | «no hay nada» con icono y acción / «ha fallado» con **Reintentar** (`(reintentar)`, y `ocupado` para no pulsarlo dos veces) |
| `<app-etiqueta-flotante>` | píldora icono + nombre + dato para anclar a un punto del 3D (`x`, `y` en %) |
| `<app-control-radial>` | arco de marcas con valor grande y − / +; `[(valor)]`, `(confirmado)` al soltar; slider accesible (teclado) |
| `<app-interruptor>` | interruptor en píldora de 44 px con `ocupado` y `desactivado` |
| `<app-numero>` | número que rueda al cambiar (salta con movimiento reducido; los lectores solo oyen el valor final) |
| `<app-tarjeta-metrica>` | tarjeta negra con cifra grande, chip de variación y barras con rayado; con `cargando` mantiene su alto |

La lógica pura (geometría del arco, interpolación del número, alturas de barras y variación) está aparte en `arco.ts`, `numero.ts` y `barras.ts`, con sus `*.spec.ts`.

**Norma para compartir CSS (fase 1C).** Angular encapsula los estilos de cada componente, por eso `gastos/comun.css` se importa en ~44 componentes. Reglas:
1. Lo que sea una pieza reutilizable (tarjeta, interruptor, esqueleto, estado vacío o de error, control radial…) va como **componente de `ui/`**, no como CSS copiado.
2. Lo que sea una clase global y pequeña (`.button`, `.cifra`, `.icon`, `.esqueleto`, `.cargando`) va en `styles.css`.
3. `comun.css` y `dialogos.css` quedan para las piezas de listas y formularios que aún no son componentes; no se les añaden piezas nuevas.
4. Ni colores, ni radios ni sombras «a mano»: se usan los tokens. Ni `backdrop-filter` ni fondos translúcidos en tarjetas.
5. Cada pantalla enseña esqueleto mientras carga (nunca «Cargando…» ni un «0» falso), estado vacío con icono y, si falla, estado de error con Reintentar.

**Menú y marco** (`shell/`): el menú es una tarjeta flotante con buscador (abre el de Ctrl+K), cada apartado con su icono en un círculo, el activo en píldora (clara en noche, negra
en día) con el círculo en el color de acento, y el usuario abajo. Ajustes (`shell/ajustes.ts`) es su propio componente. En el móvil la barra superior y la de abajo son sólidas.

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
fecha, vibración en el móvil al acertar o fallar. Es una pantalla de bloqueo sin tarjeta, en tres bandas (hora arriba, escáner en el centro, acciones abajo): el botón de Face ID, «Usar la contraseña» (con el código de dos pasos) y «Cerrar sesión». Respeta la muesca y la barra de inicio del iPhone y su ancho no pasa de 360 px (antes la tarjeta medía más que el hueco y quedaba pegada a la derecha).

## Reproductor de la barra superior

`shell/mini-musica.ts`: al tocar la portada o el título de lo que suena se abre una **ventana** (no se navega): cabecera con «Sonando ahora» (onda que baila) y cerrar, portada grande y centrada sobre su propia luz (algo más pequeña en pausa), título, artistas y álbum,
progreso con marcador y tiempos, anterior / pausa / siguiente, dispositivo y volumen, lo que viene después (tres canciones; dos en pantallas de menos de 900 px de alto y ninguna en las de menos de 700, para que quepa sin desplazar) y «Abrir Música» para saltar a la pantalla completa. El color de la
portada tiñe la ventana. Al abrirla el foco va al diálogo, no al botón de cerrar. Con la ventana abierta pregunta a Spotify cada 5 s (cada 15 s si no).

## Inicio · «Tu PC por dentro»

Ya no va dentro de una tarjeta: el dibujo 3D flota sobre la página con una luz del color de acento detrás y los bordes difuminados, con **píldoras de vidrio** alrededor
(procesador, memoria, disco con su temperatura, red y encendido; en el móvil pasan a una fila debajo). Debajo, tres módulos sin caja: **Batería** (`inicio/bateria.ts`, con tres dibujos a elegir —celda de
barras con la corriente entrando al cargar, anillo y barras— y colores ámbar y rojo con poca carga), **Espacio** (`inicio/espacio.ts`: una barra por disco, ámbar al 85 % y roja al 95 %) y **Ahora mismo**
(procesador, memoria, temperatura del disco, red, volumen y brillo). La temperatura sale de `/api/pc` (la salud que manda el agente). Las estadísticas de debajo se quedan como estaban.

**Tipo de equipo (portátil o mini PC).** Ajustes › «Tipo de equipo» elige qué se dibuja en «Tu PC por dentro» (Inicio y PC). El portátil lleva batería y gráfica y dos ventiladores; el **mini PC** (sobremesa) lleva una base de carcasa con puertos y piloto, la placa, el procesador, la memoria, el disco, un solo ventilador con disipador y la tapa transparente; el despiece, las vistas (uso, calor, aire) y las etiquetas funcionan igual, sin batería. Se guarda en el servidor (ajuste `equipo-tipo`, `GET/PUT /api/equipo` con sesión, valores `portatil` y `minipc`), porque es del equipo y no del navegador: al mover la app al mini PC solo hay que elegirlo ahí. Código: `Sistema/TipoEquipo.cs`, `core/equipo.service.ts`, `shell/ajustes-equipo.ts` y la entrada `tipo` de `inicio/pc-3d.ts` (cambiarla vuelve a montar la escena).
