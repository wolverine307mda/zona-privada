# 3. Referencia de la API HTTP

- Base: `/api`. Todo exige **sesión** (cookie `mda.sesion`) salvo lo marcado como público.
- JSON en camelCase; los enums viajan como texto (`"Gasto"`, `"Ingreso"`, `"Mensual"`, `"Evento"`, `"Recordatorio"`).
- Fechas sin hora: `"2026-10-31"`. Con hora: ISO 8601 con desfase (`"2026-10-31T10:00:00+01:00"`); se guardan en UTC.
- Errores de validación: `400` con `ValidationProblem` → `{ "errors": { "campo": ["mensaje"] } }`.
- Sin sesión `401`, recurso inexistente `404`, demasiados intentos `429`.

## Público

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/salud` | `{ estado: "ok", hora }`. CORS solo GET para la web pública (GitHub Pages) |
| GET | `/api/publico/galeria` | fotos publicadas (id, título, descripción, alt, categoría, tamaño, color, URLs). 404 si la galería está apagada. CORS GET, 300/min por IP |
| GET | `/api/publico/galeria/{id}/{mini\|grande}` | la foto en WebP sin EXIF (480 px / 1600 px), `Cache-Control: public, immutable` |
| GET | `/api/publico/estado` · `/api/publico/estado/vivo` | «en línea» y, si está activado, CPU/RAM redondeadas, minutos encendido y serie de CPU; `/vivo` es SSE cada 3 s (máx. 20 visitantes, 10 min). 404 si está apagado |
| POST | `/api/auth/login?useCookies=true` | `{ email: <usuario>, password, twoFactorCode? , twoFactorRecoveryCode? }` → pone la cookie. 10/min por IP |
| POST | `/api/sistema/agente` | agente de Windows; cabecera `X-Agente-Token`. Solo existe si hay `Agente:Token` |
| GET | `/api/conexiones/{id}/vuelta` | vuelta de OAuth (protegida por `state` de un solo uso) |

## Sesión e identidad

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/sesion` | `{ usuario, email }` (el front lo usa para saber si hay sesión) |
| POST | `/api/sesion/salir` | cierra la sesión (marca la fila y borra la cookie) |
| POST | `/api/auth/manage/2fa` | Identity: estado / activar (`enable`, `twoFactorCode`) / desactivar / códigos de recuperación |
| GET | `/api/auth/manage/info` | Identity: info de la cuenta |
| — | `/api/auth/register`, `/forgotPassword`, `/resetPassword`, `/resendConfirmationEmail` | **cerradas (404)** |

## Sistema

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/sistema` | última lectura + historial (~2 min) + origen (`equipo` = agente, `contenedor`) |
| WS | `/hubs/sistema` | SignalR; evento `metricas` cada 2 s |

## Gastos / Cartera (`/api/gastos`)

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/categorias` | `[{ id, nombre, tipo, grupo }]` |
| GET | `/mes/{anio}/{mes}` | resumen del mes: totales, comparación, reparto, gasto por día, fijos pendientes |
| GET | `/estadisticas?meses&anio&mes` | serie de los últimos N meses con ahorro acumulado |
| GET | `/gasolina` | repostajes, consumo medio L/100 km, coste por km, último precio |
| GET | `/movimientos?desde&hasta&categoria&q` | lista filtrada |
| POST | `/movimientos` | crea. Cuerpo `MovimientoEntrada`: `{ tipo, fecha, importe, categoria, concepto, notas?, litros?, precioLitro?, kilometros?, recurrenteId?, repite?: "Mensual"\|"Trimestral"\|"Anual", variable? }` |
| PUT | `/movimientos/{id}` | edita |
| DELETE | `/movimientos/{id}` | borra (y su ticket) |
| PUT/GET/DELETE | `/movimientos/{id}/ticket` | foto del ticket (cuerpo = imagen) |
| GET/POST | `/recurrentes` | lista / crea `{ nombre, tipo, categoria, importe, variable, dia, frecuencia, inicio, fin?, activo }` |
| PUT/DELETE | `/recurrentes/{id}` | edita / borra (sus movimientos se quedan sin enlace) |
| POST | `/recurrentes/{id}/registrar` | `{ fecha, importe? }` crea el movimiento del periodo |
| GET/PUT | `/saldo` | saldo actual, previsto, libre, en huchas / `{ actual }` «ahora mismo tengo X» |
| GET/POST | `/huchas` | lista / crea `{ nombre, objetivo, color, emoji, fecha? }` |
| PUT/DELETE | `/huchas/{id}` | edita / borra |
| POST | `/huchas/{id}/aportar` | `{ importe }` (negativo = sacar) |
| GET | `/presupuestos` | límite y gastado por categoría este mes |
| PUT/DELETE | `/presupuestos/{categoria}` | `{ limite }` / quitar |
| GET | `/consejos` | consejos de ahorro calculados con tus datos |
| GET/POST | `/deudas` | lista con progreso / crea `{ nombre, total, categoria, inicio, notas? }` |
| PUT/DELETE | `/deudas/{id}` | edita / borra |
| POST | `/deudas/{id}/pagos` | registra un pago (crea un movimiento enlazado) |
| GET/POST | `/mantenimiento` | mantenimientos del coche / crea `{ nombre, cadaKm?, cadaMeses?, ultimaFecha?, ultimoKm?, notas? }` |
| PUT/DELETE | `/mantenimiento/{id}` | edita / borra |
| POST | `/mantenimiento/{id}/hecho` | marca hecho (fecha y km) |

## Agenda (`/api/agenda`)

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/?desde&hasta` | **apariciones** en el rango (repeticiones expandidas + eventos de Google) |
| GET | `/proximos?dias` | próximos eventos y recordatorios |
| POST | `/` | crea `EventoEntrada`: `{ tipo, titulo, notas?, lugar?, inicio, fin?, todoElDia, color, repetir, repetirHasta?, avisoMinutos?, porCorreo, porWhatsApp, porPush, porTelegram, enGoogle }` |
| PUT/DELETE | `/{id}` | edita / borra (también en Google) |
| POST | `/{id}/hecho?hecho=true` | marca un recordatorio hecho (o deshace) |
| GET | `/estado` | canales disponibles, Google enlazado, **horario** |
| GET/PUT | `/horario` | horario de trabajo `{ activo, nombre, dias:[1..7], entrada:"08:30", salida:"17:30" }` |

## Notas (`/api/notas`)

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/?archivadas&q` | lista |
| POST | `/` | `{ titulo?, contenido?, color?, etiquetas?: string[], fijada, archivada }` |
| PUT/DELETE | `/{id}` | edita / borra |

## Documentos (`/api/archivos`)

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/raices` | carpetas montadas con nº de elementos |
| GET | `/lista?raiz&ruta` | contenido de una carpeta |
| GET | `/buscar?raiz&q` | por nombre (máx. 200) |
| GET | `/archivo?raiz&ruta&ver` | descarga o vista en línea (`ver=true`), admite `Range` |
| POST | `/subir?raiz&ruta` | multipart, uno o varios archivos |
| POST | `/carpeta` · `/renombrar` · `/mover` | crear / renombrar / mover dentro de la misma raíz |
| DELETE | `/?raiz&ruta` | a la papelera |
| GET | `/papelera` | lo borrado |
| POST | `/papelera/recuperar` | vuelve a su sitio |
| DELETE | `/papelera?raiz&id` | borrar definitivamente (uno o vaciar) |

## Conexiones y música

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/conexiones` | proveedores, si están configurados/enlazados y la URL de vuelta a dar de alta |
| GET | `/api/conexiones/{id}/conectar` | redirige a los permisos del proveedor (`spotify`, `google`) |
| DELETE | `/api/conexiones/{id}` | desenlaza |
| GET | `/api/musica/ahora` · `/recientes` · `/cola` · `/top` | Spotify |
| POST | `/api/musica/control/{accion}` | `play`, `pause`, `next`, `previous` |

## Seguridad y perfil

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/seguridad/sesiones` | sesiones abiertas (actual y «en línea») |
| POST | `/api/seguridad/latido` | la pestaña sigue abierta |
| DELETE | `/api/seguridad/sesiones/{id}` · `/api/seguridad/sesiones` | cerrar una / todas menos esta |
| GET | `/api/seguridad/accesos` | intentos de entrar |
| GET | `/api/seguridad/estado` | 2FA, canales de aviso configurados, ajustes |
| PUT | `/api/seguridad/ajustes` | avisar de inicios de sesión, resumen semanal… |
| POST | `/api/seguridad/avisos/prueba/{canal}` | `correo`, `whatsapp`, `push`, `telegram` |
| GET/PUT | `/api/perfil` | nombre, apellidos, correo, teléfono |
| POST | `/api/perfil/clave` | cambiar contraseña |
| GET/PUT/DELETE | `/api/perfil/foto` | foto de perfil (si no hay, el front usa `img/avatar.webp`) |
| GET | `/api/push/clave` | clave pública VAPID |
| GET/POST/DELETE | `/api/push` | dispositivos suscritos / suscribir / darse de baja (`/{id}` para uno) |

## Asistente, Telegram y tiempo

| Método | Ruta | Descripción |
|---|---|---|
| POST | `/api/asistente/entender` | `{ texto }` (≤ 300) → `Propuesta { clase, resumen, …campos, dudas, pregunta?, respuesta? }` |
| GET | `/api/telegram` | configurado, nombre del bot, chat vinculado |
| POST | `/api/telegram/vincular` | `{ enlace: "https://t.me/<bot>?start=<código>" }` |
| DELETE | `/api/telegram` | desvincular |
| GET | `/api/tiempo` | lugar, actual, por horas, semana, avisos activados |
| GET | `/api/tiempo/buscar?q=` | buscar ciudad |
| PUT | `/api/tiempo/lugar` | guardar ciudad (también desde geolocalización) |
| PUT | `/api/tiempo/avisos` | activar/desactivar el aviso de la víspera |


## Galería y lo que ve el público (`/api/galeria`, `/api/publico-ajustes`)

| Método | Ruta | Descripción |
|---|---|---|
| GET/PUT | `/api/galeria/estado` | interruptor general `{ activa }` (por defecto apagado), nº de fotos |
| GET | `/api/galeria/carpeta?ruta` | fotos de la carpeta «Imágenes» con su estado de publicación |
| GET/POST | `/api/galeria/fotos` | lista / publica `{ ruta, titulo?, descripcion?, alt?, categoria? }` |
| PUT/DELETE | `/api/galeria/fotos/{id}` | edita (`publicada`…) / quita de la galería (el original no se toca) |
| PUT | `/api/galeria/orden` | `{ ids: [...] }` |
| GET | `/api/galeria/fotos/{id}/{tam}` | vista previa con sesión |
| GET/PUT | `/api/publico-ajustes` | `{ estado, cpu }`: qué ve el público del equipo (apagado por defecto) |

## Más módulos (luz, señuelos, atajos, globo)

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/luz` · PUT `/api/luz/ajustes` | hora dorada y noches de Vía Láctea de hoy y mañana con su nota; ajustes de los avisos |
| GET | `/api/luz/dia?fecha=` | «Cuándo salir a hacer fotografía»: efemérides (amanecer, atardecer con acimut, crepúsculos, luna), serie de 145 puntos (cada 10 min) con sol, luna y núcleo galáctico (altura y acimut), sombra, nubes por capas y nota, y las ventanas del día. De ayer a 7 días |
| GET | `/api/luz/semana` | los próximos 7 días: ventanas, mejor momento de cada uno, nubes y los 5 mejores momentos con su porqué |
| GET | `/api/luz/eventos` | lluvias de meteoros y eclipses de los próximos 6 meses con la luna de esa noche y las nubes si caen en la previsión (fechas fijas en `Luz/Eventos.cs`) |
| GET | `/api/luz/aurora` | Kp previsto por el NOAA SWPC, el Kp que hace falta en la latitud de tu lugar y si hoy es posible (solo hemisferio norte; en `Luz/Aurora.cs`) |
| GET | `/api/luz/niebla` | los próximos 3 amaneceres: niebla, inversión térmica o mar de nubes según temperatura, rocío, humedad, viento, nubes bajas y temperatura a 850 hPa (`Luz/Niebla.cs`) |
| GET/POST/PUT/DELETE | `/api/luz/puntos` (`/{id}`) | tus sitios de fotografía (nombre, coordenadas, hacia dónde miras, notas; hasta 30). `GET /api/luz/puntos/hoy` da la mejor ventana de hoy y mañana de cada uno; `/api/luz/dia` y `/semana` aceptan `?punto=id` |
| GET/POST/DELETE | `/api/luz/salidas` | el diario de salidas: fecha, sitio, tipo, nota prevista (1-10), cómo salió (1-5) y una nota; el GET incluye la calibración por tipo de luz (`Luz/Puntos.cs`) |
| GET/POST | `/api/senuelos` · PUT/DELETE `/{id}` · POST `/{id}/probar` · PUT `/ajustes` | archivos señuelo en Documentos |
| GET/POST/DELETE | `/api/atajos-tokens` | tokens para Atajos de iOS (con sesión); el token solo sale al crearlo |
| GET/POST | `/api/atajos/categorias` · `/movimiento` · `/frase` · `/resumen` · `/ping` · `/pago` | los usa el iPhone con `Authorization: Bearer mda_…` (alcance gasto, pago o lectura) |
| GET/PUT | `/api/galeria/globo` | zona excluida alrededor de casa `{ lat, lon, radioKm }` |
| GET | `/api/galeria/fotos/{id}/gps` | el GPS del archivo original (solo para ti); 204 si no tiene |

## Passkeys, «Mi PC» y reautenticación

| Método | Ruta | Descripción |
|---|---|---|
| POST | `/api/auth/passkey/entrada/opciones` · `/entrada` | entrar con un passkey (sin sesión); el segundo recibe `{ credencial }` y pone la cookie. 10/min por IP |
| GET/POST/PUT/DELETE | `/api/passkeys` · `/opciones` · `/{id}` | gestionar los passkeys (con sesión, máx. 5). El GET trae nombre, sincronizada/verificada, alta (IP, dispositivo, idioma, zona, pantalla), último uso y nº de usos; el POST recibe `{ credencial, nombre, dispositivo }`; el PUT `{ nombre }` renombra |
| POST | `/api/seguridad/reautenticar` | `{ password, codigo? }`: vale 2 min para esta sesión |
| POST | `/api/seguridad/reautenticar/passkey/opciones` · `/passkey` | lo mismo con Face ID (`{ credencial }`) |
| POST | `/api/seguridad/panico` | cierra sesiones, revoca tokens, apaga lo público y bloquea el login 1 h |
| POST | `/api/pc/casa` · `/concentracion` · `/multimedia` | `{ estado: llegada\|salida }`, `{ activo }`, `{ accion: playpause\|siguiente\|anterior\|silencio }`: varias órdenes al agente de una vez; sin Face ID (no apagan nada). 409 si el agente no está en marcha |
| POST | `/api/atajos/casa` | lo mismo que `/api/pc/casa` para el iPhone (token con permiso `casa`) |
| GET/PUT | `/api/pc/avisos` | `{ descargas, windows, copias, bateria }`: qué te avisa el PC |
| POST | `/api/pc/ordenes` `{ tipo: "energia", parametro: equilibrado\|rendimiento\|ahorro\|cine }` | cambia el plan de energía de Windows (`powercfg`); «cine» es el equilibrado con el PC y la pantalla despiertos. Sin Face ID |
| GET | `/api/pc/bateria` | la última medida de la salud de la batería y el historial de 90 días (capacidad de diseño, actual, ciclos y desgaste %) |
| POST | `/api/sistema/agente/bateria` | el agente manda `{ diseno, completa, ciclos }` tras `powercfg /batteryreport` (token); una medida por día. Se pide con la orden `bateria-informe` |
| POST | `/api/pc/ordenes` `{ tipo: "abrir", parametro }` | abre en el PC una dirección http(s) (hasta 300 letras, sin usuario ni espacios) o una app de la lista (`calculadora`, `bloc`, `explorador`). Pide Face ID siempre; el agente lo abre con el explorador de Windows, sin consola |
| GET/POST/PUT/DELETE | `/api/escenas` · `/{id}` | escenas del PC (brillo, volumen, plan de energía, concentración, PC despierto y apagado programado; cada campo vacío = no tocar). La primera vez salen tres de ejemplo (Cine, Trabajo, Dormir) |
| POST | `/api/escenas/{id}/aplicar` | aplica la escena encolando una orden por ajuste; si programa el apagado pide Face ID (403 `reautenticar`) |
| POST | `/api/atajos/escena` `{ nombre }` | lo mismo desde el iPhone (token con permiso `casa`); las escenas que apagan el PC no se pueden lanzar así |
| GET | `/api/red/{id}/historial?horas=` | gráfica de latencia y caídas de un aparato con historial (1 h a 30 días): puntos, disponibilidad, media, máximo y caídas. Hasta 3 aparatos con historial |
| GET | `/api/red/velocidad?dias=` | las medidas del test de velocidad de Internet (bajada, subida y latencia) y su resumen; se guardan las últimas 400 |
| POST | `/api/sistema/agente/velocidad` | el agente manda `{ bajadaMbps, subidaMbps, pingMs }` tras la prueba (token). Se pide con la orden `velocidad`, a mano o de madrugada |
| GET | `/api/hogar/estado` · `/api/hogar/entidades` | qué servicios de casa hay configurados y las luces, enchufes, ventiladores, interruptores virtuales y escenas de Home Assistant (409 si falta configurarlo, 502 si no responde) |
| POST | `/api/hogar/entidades/{id}/{accion}` | `encender`, `apagar`, `alternar` (o `activar` en una escena) sobre una entidad de esos cinco tipos; nada de cerraduras ni alarmas |
| GET | `/api/hogar/adguard` | consultas DNS, bloqueadas (%), respuesta media, si la protección está activa y los dominios más bloqueados y consultados de AdGuard Home |
| POST | `/api/hogar/adguard/proteccion` `{ activa, minutos }` | activa la protección o la pausa de 1 minuto a 24 horas (AdGuard la reactiva sola) |
| POST | `/api/red/{id}/despertar` | Wake-on-LAN: manda al agente una orden `wol` con la MAC del aparato (409 si el agente no está en marcha, 403 `reautenticar` sin Face ID, 400 si no tiene MAC) |
| GET/POST/PUT/DELETE | `/api/red` · `/{id}` · POST `/{id}/probar` | aparatos de la red de casa (nombre, IP o nombre, puerto, tipo); solo direcciones locales o de Tailscale; se prueban cada minuto |
| GET/POST/DELETE | `/api/portapapeles` · `/{id}` · POST `/{id}/al-pc` · `/del-pc` | texto compartido móvil ↔ PC (20 últimos, 24 h, 2000 letras) |
| POST | `/api/sistema/agente/eventos` | el agente cuenta una descarga terminada o «Windows pide reiniciar» (token) |
| GET/POST | `/api/sistema/agente/portapapeles` · `/{id}` | el agente lee el texto a copiar o deja el del PC (token) |
| GET/PUT/DELETE | `/api/seguridad/claves` · `/{servicio}/{nombre}` | claves de servicios cifradas en la base: la lista no lleva valores; ver (GET), cambiar y quitar piden confirmar (403 `reautenticar`) |
| GET/POST/DELETE/PUT | `/api/seguridad/pin` | PIN de Documentos: estado, poner el PIN (401 si falla, 429 tras cinco), quitarlo de la sesión, cambiarlo (pide confirmar). Documentos, `/api/galeria/carpeta` y `/api/senuelos` contestan 403 `pin` sin él |
| GET | `/api/galeria/carpeta/mini?ruta=&v=` | miniatura WebP de 360 px de una imagen de «Imágenes», publicada o no (con el PIN); con `v` (hash de tamaño y fecha, lo da la lista de la carpeta) se guarda un año |
| GET | `/api/pc/historial` | una foto de la salud por día (espacio libre y desgaste de los discos), 90 días |
| GET | `/api/pc` | salud del PC, si el agente está conectado, si la reautenticación vale y las últimas órdenes |
| POST | `/api/pc/ordenes` · GET `/api/pc/ordenes/{id}` | `{ tipo, parametro? }` (`bloquear`, `despierto`, `reiniciar`, `suspender`, `apagar`, `cancelar`); pide reautenticar (403 `reautenticar:true`) y avisa a Telegram |
| POST | `/api/sistema/agente/salud` | el agente manda discos, volúmenes y reinicio pendiente (token) |
| GET | `/api/sistema/agente/ordenes` | el agente pregunta (espera hasta 20 s) y recibe las órdenes firmadas con HMAC-SHA256 |
| POST | `/api/sistema/agente/ordenes/{id}` | el agente cuenta el resultado `{ ok, resultado }` |
| PUT | `/api/sesion/bloqueo` | `{ segundos }` (5–3600, null = no pedirlo) para ESTA sesión; aflojarlo pide reautenticar |
| POST | `/api/sesion/segundo-plano` · `/desbloqueo/opciones` · `/desbloqueo` · `/desbloqueo-clave` | avisar de que sales de la app; desbloquear con passkey o con contraseña |
| PUT | `/api/sesion/bloqueo` | `{ segundos }` (5–3600, null = no pedirlo) para ESTA sesión; aflojarlo pide reautenticar |
| POST | `/api/sesion/segundo-plano` · `/desbloqueo/opciones` · `/desbloqueo` · `/desbloqueo-clave` | avisar de que sales de la app; desbloquear con passkey o con contraseña |
