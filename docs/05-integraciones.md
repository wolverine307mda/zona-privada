# 5. Integraciones externas

Todas son **opcionales**: si falta su configuración, la app funciona sin ellas y la interfaz lo indica.

## Telegram (bot)

1. En Telegram, habla con **@BotFather** → `/newbot` → copia el token.
2. `.env`: `TELEGRAM_TOKEN=...` y `docker compose up -d api`. En los registros: «Bot de Telegram @NombreBot escuchando».
3. En la app: **Seguridad › Avisos › Telegram › Vincular** → abre `https://t.me/<bot>?start=<código>` → **Iniciar**.
   El código vale una vez; desde ese momento el bot **solo atiende a ese chat**.

Uso: escribir frases como al asistente («taxi 15 € ayer», «dentista el martes a las 10», «¿cuánto llevo en
restaurantes?», «¿qué tengo mañana?») → el bot responde con lo entendido y botones **Guardar / Cancelar**; tras guardar,
**Deshacer**. Comandos: `/hoy`, `/tiempo`, `/saldo`, `/ayuda`. Recibe además los avisos del sistema (recordatorios marcados
para Telegram, presupuestos, inicios de sesión, copias, coche, tiempo de mañana, resumen semanal).

Técnica: long polling con `getUpdates` (no hace falta webhook ni puerto abierto). El token **nunca** se registra
(HttpClients con `RemoveAllLoggers()`). Si el token se filtra: `/revoke` en @BotFather, nuevo token en `.env`, reiniciar API.

## Spotify

1. https://developer.spotify.com/dashboard → crear app → *Redirect URI*: `<URL_PUBLICA>/api/conexiones/spotify/vuelta`
   (la app la muestra en Música/Seguridad).
2. `.env`: `SPOTIFY_CLIENT_ID`, `SPOTIFY_CLIENT_SECRET`, `URL_PUBLICA`. Reiniciar la API.
3. En la app: Música → Conectar.

Da: lo que suena (con portada), control (play/pause/siguiente/anterior), recientes, cola, top de artistas/canciones.
La CSP permite imágenes de `*.scdn.co` y `*.spotifycdn.com`.

## Google Calendar

1. https://console.cloud.google.com → API de Google Calendar activada → credenciales **ID de cliente OAuth «Aplicación web»**
   → URI de redirección `<URL_PUBLICA>/api/conexiones/google/vuelta`.
2. `.env`: `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`. Reiniciar.
3. En la app: Agenda/Seguridad → Conectar Google.

Da: los eventos del calendario principal aparecen en la agenda; los eventos propios marcados «en Google» se copian allí.

## Correo (SMTP)

`.env`: `CORREO_SERVIDOR` (p. ej. `smtp.gmail.com`), `CORREO_PUERTO=587`, `CORREO_USUARIO`, `CORREO_CLAVE`
(en Gmail una **contraseña de aplicación**), `CORREO_PARA` (destinatario). Plantilla HTML profesional en
`Avisos/Plantilla.cs` (compatible con clientes de correo: tablas y estilos en línea, modo oscuro, pie con enlaces).

## WhatsApp (CallMeBot)

Sigue las instrucciones de https://www.callmebot.com para obtener una API key ligada a tu número.
`.env`: `WHATSAPP_TELEFONO` (con prefijo, p. ej. `34600000000`), `WHATSAPP_APIKEY`.

## Web Push (notificaciones de la app)

Sin terceros: estándar Web Push con claves **VAPID** generadas al primer uso y guardadas en `ajustes`
(`vapid-publica`, `vapid-privada`). En la app: Seguridad › Avisos › Notificaciones en este dispositivo.
En iPhone: primero «Compartir › Añadir a pantalla de inicio» y abrir desde ahí.

## Open-Meteo (el tiempo)

Sin clave ni registro. Previsión (`api.open-meteo.com`) y geocodificación (`geocoding-api.open-meteo.com`), caché 15 min.
Elegir ciudad en la tarjeta del tiempo de Inicio. Aviso la víspera (desde las 20:00) por push y Telegram.

## Agente de Windows

No es externo, pero vive fuera de Docker: `agente/Mda.Agente` (consola .NET 10). Variables: `AGENTE_URL`
(por defecto `http://localhost:8080`) y `AGENTE_TOKEN` (si no está en el entorno lo lee del `.env` del repo).
Manda cada 2 s CPU, memoria, discos y red reales a `POST /api/sistema/agente` con `X-Agente-Token`. Cada 5 min manda además la **salud** (estado de los discos por WMI, espacio, reinicio pendiente; la temperatura y el desgaste solo con el agente como administrador) y atiende las **órdenes** de «Mi PC» preguntando a la API (bloquear, mantener despierto, reiniciar, suspender, apagar), solo si la firma y la caducidad cuadran. Es un programa para Windows (`net10.0-windows`).

## Home Assistant (casa conectada)

En **Seguridad → Claves → Home Assistant** se guardan (cifradas) su dirección (`http://homeassistant:8123` o la IP de tu red) y un **token de acceso de larga duración** (Home Assistant → tu perfil → Seguridad). La dirección tiene que ser de tu red
de casa, de Tailscale o de un contenedor: no se acepta una de Internet. En Mi PC aparece «Casa conectada» con un interruptor por cada luz, enchufe, ventilador, interruptor virtual y botón de activar por escena
(`ClienteHogar` en `api/Mda.Api/Pc/Hogar.cs`). Cerraduras, alarmas y el resto de dominios no se tocan desde aquí.
Si no lo tienes, `docker compose --profile casa up -d` levanta Home Assistant (panel en `http://localhost:8123`) y AdGuard Home (`http://localhost:3001`) en este mismo equipo.

## AdGuard Home (bloqueo de anuncios en la red)

En **Seguridad → Claves → AdGuard Home** se guardan (cifrados) su dirección (de tu red, de Tailscale o `http://adguard:3000` si usas el perfil `casa`), su usuario y su contraseña. En Mi PC, dentro de «Casa conectada», sale el porcentaje
de consultas bloqueadas, las consultas y la respuesta media, los dominios que más se bloquean y un botón para **pausar la protección 10 minutos** (por si una web que necesitas no carga; AdGuard la reactiva sola) o reanudarla
(`ClienteAdGuard` en `api/Mda.Api/Pc/AdGuard.cs`). Para que AdGuard bloquee de verdad, el router (o el PC) tiene que usarlo como servidor DNS. **Pi-hole no está soportado**: tiene otra API y no se ha probado contra uno.

## Tu habitación en 3D (Mi PC)

`web/src/app/pc/habitacion-3d.ts` dibuja tu escritorio con Three.js y lo enlaza con el estado real: la pantalla, la luz y el ventilador de la torre siguen la carga del PC (si el agente está conectado),
los LED del router se ponen verdes o rojos según `/api/red`, el móvil se enciende si está en casa y cada cubo de la estantería es otro aparato de la red. Se gira arrastrando; si WebGL falla sale un botón de reintento.
