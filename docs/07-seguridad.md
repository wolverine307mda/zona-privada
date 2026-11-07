# 7. Seguridad

## Modelo

Una sola persona usuaria, datos personales sensibles (dinero, agenda, archivos del PC), servidor en casa expuesto a
Internet. Objetivos: que nadie más entre, que un robo de cookie o de token valga poco y que los datos no se pierdan.

## Exposición de red

- Ningún puerto abierto en el router. La API escucha en `127.0.0.1:8080` y Seq en `127.0.0.1:5341`.
- Internet llega solo por **Tailscale Funnel** (HTTPS con certificado de Tailscale) o, como alternativa, Cloudflare Tunnel (+ Access).
- **Más cerrado aún (opcional): sin Funnel.** `tailscale/serve-privado.json` publica la app solo dentro de tu tailnet (HTTPS, sin `AllowFunnel`): únicamente entran los
  dispositivos con Tailscale (PC e iPhone con su app). Se pierden la galería pública, el estado en vivo y el aviso «encendido» de la web pública, y lo que dependa de
  que un servicio externo llame a tu dirección (comprueba las vueltas OAuth de Spotify/Google y los webhooks antes de cambiar). Para activarlo, en `compose.yaml` pon
  `TS_SERVE_CONFIG: /config/serve-privado.json` en el servicio `tailscale` y `docker compose --profile publico up -d tailscale`. Para volver, `serve.json`.
- `ForwardedHeaders` confía en el proxy (solo accesible desde la red interna de Docker) para conocer IP y esquema reales.

## Autenticación

| Medida | Detalle |
|---|---|
| Sin registro | `/register`, `/forgotPassword`, `/resetPassword`, `/resendConfirmationEmail`, `/refresh`, `/confirmEmail` → 404 |
| Solo cookie | `/login` sin `useCookies=true` → 400: no se emiten tokens Bearer (no se verían en Sesiones) |
| Contraseña | mínimo 12 caracteres; la de `.env` solo se impone cuando cambia en el `.env` (una cambiada desde Perfil no se deshace) |
| Cookie | `mda.sesion`, `HttpOnly`, `SameSite=Strict`, `Secure` en producción, 8 h deslizantes |
| 2FA | TOTP con app autenticadora + códigos de recuperación |
| Bloqueo | 5 fallos → 15 min |
| Límite por IP | 10 intentos/min en `/api/auth/*`; 10 cada 5 min en avisos de prueba y cambio de contraseña; tope general de 600/min (429). La IP sale de la conexión (`X-Forwarded-For` del proxy), nunca de una cabecera que escriba el visitante; `CF-Connecting-IP` solo con `Proxy:Cloudflare=true` |
| Sesiones revocables | cada cookie lleva un id de sesión comprobado en cada petición; se cierran desde Seguridad. Una cookie sin id no vale y ninguna sesión pasa de 30 días |
| Accesos | registro de cada intento de login (IP, dispositivo, motivo), 300 últimos |
| Aviso de inicio | opcional por correo / Telegram con hora local |
| Claves de cookie | Data Protection persistido en el volumen `claves` |

## Navegador

- **CSP** estricta: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://*.scdn.co https://*.spotifycdn.com https://tile.openstreetmap.org; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`.
- Sin iframes (`frame-ancestors 'none'`), `X-Content-Type-Options: nosniff`, sin scripts de terceros (fuentes locales).
- `Strict-Transport-Security` (solo por HTTPS), `Cross-Origin-Opener-Policy: same-origin`, `Cross-Origin-Resource-Policy: same-origin` (salvo `/api/salud` y `/api/publico`), `Cache-Control: no-store` en `/api/*`, `Permissions-Policy` con `geolocation=(self)` y sin cabecera `Server`.
- `AllowedHosts` limita la cabecera `Host` (`ALLOWED_HOSTS` en `.env`).
- CORS: solo `GET` desde la web pública, en `/api/salud` y `/api/publico/*` (galería y estado en vivo), con límite de ritmo propio.
- **Galería pública**: nada sale sin activar el interruptor y marcar cada foto; el id es aleatorio, la ruta real nunca viaja y siempre se sirve una copia WebP recomprimida (el codificador no copia EXIF: sin GPS ni cámara) con caché inmutable.
- **Globo 3D**: cada foto decide si sale; al público solo llegan el nombre y la posición **redondeada a ~10 km**, y nunca nada dentro del radio excluido alrededor de tu casa. El GPS exacto del archivo solo lo ve el dueño en la privada.
- **Atajos de iOS**: tokens con alcance (gasto, pago, lectura), guardados como hash SHA-256, revocables, sin cookie y sin acceso al resto de la app; aviso por Telegram desde una red nueva.
- **Passkeys (Face ID / Windows Hello)**: entrar sin contraseña ni código (un passkey ya es «algo que tienes + tu cara»); atados al dominio de `App:UrlPublica`, máx. 5, revocables. La contraseña y los dos pasos siguen como respaldo.
- **Bloqueo al volver a la app**: ajuste de cada dispositivo (Seguridad › Face ID). Al salir de la app, la sesión se bloquea EN EL SERVIDOR pasado el tiempo elegido (o tras 3 min de silencio si iOS la cerró sin avisar) y contesta 423 a todo salvo a `/api/sesion/*`, incluido el tiempo real, hasta desbloquearla con Face ID o con la contraseña. Quitarlo o aflojarlo pide la contraseña otra vez.
- **Reautenticación**: las órdenes al PC piden la contraseña (y el código si hay dos pasos) otra vez; vale 2 min y solo para esa sesión.
- **Órdenes al PC**: lista cerrada, firmadas con HMAC-SHA256 con el secreto del agente, caducan a los 60 s, avisan a Telegram y las ejecuta el agente, que pregunta él (sin abrir puertos). Suspender o apagar dejan el PC inalcanzable hasta que lo enciendas tú.
- **Descargar en el PC**: solo con Face ID, solo http(s), nunca a la red local ni al propio PC (comprobado al conectar), sin programas, 4 GB y carpeta fija; ver [10-nuevas-funciones.md](10-nuevas-funciones.md).
- **Señuelos**: archivos falsos en Documentos; abrirlos o descargarlos avisa y cierra la sesión.
- **Estado en vivo**: apagado por defecto; solo números redondeados (nunca equipo, discos, red ni IP); el directo se corta a los 10 min y con ≤ 20 visitantes a la vez.
- Markdown de notas: renderizador propio que escapa HTML.
- Documentos abiertos en línea: texto como `text/plain`, sin ejecutar nada.

## Datos y secretos

- Secretos solo en `.env` (ignorado por git) y en *user secrets* en desarrollo. **Nunca** en el repo, ni en tests
  (los tests usan `clave-de-prueba` y bases en memoria).
- Tokens OAuth (Spotify/Google) cifrados con Data Protection; el secreto del cliente nunca llega al navegador; `state` de un solo uso.
- Token de Telegram fuera de los registros (`RemoveAllLoggers()` en sus HttpClients).
- El bot solo responde al chat vinculado con un código de un solo uso.
- El agente usa un token compartido (`X-Agente-Token`, mínimo 32 caracteres); sin token válido la ruta no existe. Se rechaza lo que llegue por el proxy (`X-Forwarded-For`/`X-Original-For`: el agente va directo a la API), el cuerpo no puede pasar de 16 KB y tiene límite de ritmo.
- Documentos: rutas confinadas a cada raíz, sin `..`, sin enlaces simbólicos, borrado a papelera recuperable,
  subidas escritas con nombre temporal. No se pueden subir ni renombrar a programas o scripts (`.exe`, `.bat`, `.ps1`, `.lnk`…; `PERMITIR_EJECUTABLES=true` lo permite) y se deja una reserva de 2 GB libres en el disco.
  `MODO_<CARPETA>=ro` en `.env` deja una carpeta de solo lectura.
- Contenedor de la API con usuario sin privilegios, sistema de archivos de solo lectura (salvo `/tmp`, `/claves` y las carpetas montadas), sin capacidades de Linux ni escalada de privilegios, y con tope de memoria y procesos. Registros de Docker con rotación.

## Llaves de acceso (passkeys) y confirmaciones

- Cada llave tiene un **nombre** que eliges y se puede renombrar. De cada una se guarda (tabla `passkeys_info`) desde dónde se creó (IP, dispositivo,
  navegador, idioma, zona horaria, pantalla), cuándo y desde dónde se usó por última vez y cuántas veces; junto a lo que ya guarda Identity
  (sincronizada o solo en el dispositivo, verificada con cara/huella). Alta y baja de una llave avisan por Telegram.
- Las acciones delicadas (órdenes al PC, aflojar el bloqueo de la app) piden **volver a confirmar**: con Face ID
  (`/api/seguridad/reautenticar/passkey`) o con la contraseña (y el código de dos pasos). Vale 2 minutos y solo para esa sesión.

## Pánico, sondas y latido

- **Botón de pánico** (Seguridad, o `/panico` en Telegram): cierra todas las sesiones, revoca los tokens de los atajos, apaga la galería y el estado
  públicos y bloquea el inicio de sesión (login y passkey) durante 1 hora. `/desbloquear` en Telegram lo levanta antes.
- **Sondas**: `/.env`, `/wp-login.php`, `/admin`, `/.git/config`… contestan 404 como cualquier ruta inexistente, se apuntan y la IP pública queda
  bloqueada 1 hora (nunca una IP local o desconocida). Resumen por hora a Telegram, solo si hubo alguna.
- **Latido** a Healthchecks.io: ver «Latido» en [6. Despliegue](06-despliegue-y-operacion.md).
- Opción no activada: dejar la app **solo dentro de Tailscale** (sin Funnel). Es lo más seguro, pero la app instalada en el iPhone y el
  portfolio dejarían de llegar desde fuera de tu red.

## Continuidad

- Copia diaria de PostgreSQL (14 días) + copia cifrada con age a OneDrive + simulacro semanal de restauración + aviso si falta
  (ver [6. Despliegue](06-despliegue-y-operacion.md#copias-de-seguridad)).
- Si se filtra un secreto: cambiarlo en el proveedor (BotFather `/revoke`, Google/Spotify, contraseña de aplicación),
  actualizar `.env`, `docker compose up -d api`.

## Cifrado de datos en la base (clave maestra)

Notas (título y contenido), portapapeles, claves del push y la clave privada VAPID se guardan **cifradas** con AES-256-GCM
(`api/Mda.Api/Seguridad/Boveda.cs`). La clave es `MDA_CLAVE_MAESTRA` del `.env` (`openssl rand -base64 32`); cada tipo de dato
usa su propia subclave (HKDF) y GCM detecta si alguien toca un dato. En la base se ve `v1:…`; la API lo devuelve en claro.

- **Guárdala también fuera del PC.** Sin ella esos datos no se pueden leer, ni siquiera con una copia de la base.
- En producción la API no arranca sin ella. En desarrollo y pruebas se usa una clave fija conocida.
- Al primer arranque con la clave se cifra lo que ya había (una sola vez, marcado en `ajustes` como `boveda-v1`).
- La búsqueda de notas se hace en memoria, porque en la base el texto ya no se puede buscar.
- Un hash (SHA) no sirve para esto: no se puede deshacer. Las contraseñas de acceso sí van con hash (PBKDF2 de Identity).
- Pendiente: el secreto del 2FA y el resto de ajustes siguen como antes.

## Claves de servicios (Seguridad → Claves)

Telegram, WhatsApp, correo, Spotify, Google y el latido externo se editan desde la app y se guardan **cifradas** en la tabla `secretos`
(`api/Mda.Api/Seguridad/Secretos.cs`). Funcionan como una fuente más de configuración con la prioridad más alta: el código sigue leyendo
`config["Telegram:Token"]` y ve el valor de la base, así que cambiar una clave vale al momento, sin reiniciar.

- Al primer arranque lo que haya en el `.env` se importa a la base (una vez por campo); a partir de ahí **manda la base** y esas líneas del `.env` se pueden quitar.
- Quitar una clave la deja vacía (no vuelve a entrar la del `.env`).
- La pantalla nunca recibe los valores: ver, cambiar o quitar pide confirmar con Face ID o contraseña (`Reautenticar`, 2 minutos) y el valor se oculta solo a los 20 s.
- Con la base en memoria (pruebas y desarrollo) no se importa nada del `.env`.

## PIN de Seguridad

Cuatro cifras que se piden al entrar en Documentos, en «tu carpeta de imágenes» de la galería y en la lista de señuelos
(`api/Mda.Api/Seguridad/Pin.cs`). Solo se guarda su huella (PBKDF2). Vale 10 minutos desde el último uso y solo en la sesión donde se
puso. Cinco fallos seguidos bloquean esa sesión 15 minutos. La app enseña un teclado propio (`shell/pin.ts`) cuando el servidor contesta
403 `{ pin: true }` y repite la petición al acertar.

- El PIN inicial sale de `DOCUMENTOS_PIN_INICIAL` del `.env` la primera vez; luego se cambia en Seguridad (pide Face ID) y esa línea se puede quitar.
- Sin PIN guardado no se pide nada.
- `GET /api/archivos/raices` queda fuera (Inicio solo cuenta carpetas).
- Las miniaturas van por cookie, no por la app: por eso el PIN es estado de la sesión en el servidor y no una cabecera.

## Temperatura del procesador

El agente lee las zonas térmicas de ACPI por WMI (`MSAcpi_ThermalZoneTemperature`, hace falta ejecutarlo como administrador, como hace el lanzador) y manda la más caliente con la salud del PC
(`SaludPc.CpuTemperaturaC`). Si lleva **85 °C o más durante 5 minutos seguidos** (se apunta desde cuándo en `ajustes`: `salud-caliente-desde`), `ReglasSalud` avisa por Telegram y notificación una vez al día,
aconsejando el plan «Ahorro» de energía, y avisa de nuevo cuando baja. La salud se evalúa cada 10 minutos, así que el aviso llega en la segunda lectura caliente seguida.
Limitaciones: es la zona térmica de la placa, que en muchos portátiles sigue de cerca al procesador pero no es la lectura del chip, y hay equipos que no la exponen (entonces no hay dato ni aviso).
Para la temperatura exacta del chip haría falta una librería con su propio controlador (LibreHardwareMonitor), que no se ha añadido.
- **Vigilante del Funnel** (`Seguridad/VigilanteFunnel.cs`): cada 10 minutos pide `/api/salud` a `App:UrlPublica` como lo haría alguien de fuera y mira cuánto le queda al certificado; avisa por Telegram si deja de responder (a la segunda vez seguida), cuando vuelve y cuando el certificado está a menos de 10 días de caducar.
- **Tarpit** (`Seguridad/Tarpit.cs`): las rutas de sonda reciben el 404 a un byte cada 2 s durante un minuto (tope de 20 conexiones a la vez); las IP de casa no.
- **Mapa de atacantes** (`Seguridad/GeoSondas.cs`, globo en Seguridad): cada sonda se guarda 90 días y se ubica con la base DB-IP Lite (CC BY 4.0, hay que citarla: «IP geolocation by DB-IP»). La IP de origen queda guardada en tu base de datos; la geolocalización es aproximada (ciudad o país).

## Equipos de confianza, «no lo reconozco» y mapa

En Seguridad › Equipos conocidos cada equipo muestra si está **en línea** (sesión activa hace menos de 2 min), **reciente** (menos de 3 días) o **hace tiempo**, y cuándo y desde dónde se vio por última vez («hace 3 h desde Madrid, ES»). **No lo reconozco** (dos toques) cierra las sesiones abiertas de ese tipo de equipo con esa IP, lo marca `Sospechoso` (el semáforo de Seguridad pasa a ámbar mientras haya alguno), avisa por push y Telegram con su IP y su ciudad y enlaza a Perfil para cambiar la contraseña. El equipo desde el que estás entrando no se puede marcar (te echarías a ti mismo). Marcarlo «de confianza» quita la sospecha.

El **mapa de accesos** pinta los equipos y las redes de los atajos (`TokenAtajo.Redes`) con la base DB-IP Lite que ya usa el globo de sondas. Va detrás del PIN y siempre dice que la ubicación es aproximada (una VPN o el móvil pueden salir lejos). Verde = de confianza, naranja = conocido, rojo = no reconocido, azul = red de un atajo. Rutas: `POST /api/seguridad/equipos/{id}/no-reconozco`, `GET /api/seguridad/equipos/mapa`.
