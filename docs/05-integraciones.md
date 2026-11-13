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

Da: los eventos de los calendarios que elijas aparecen en la agenda; los eventos propios marcados «en Google» se copian allí.

**Varias cuentas.** Agenda › «Cuentas y calendarios» (o «Añadir otra cuenta» en ese diálogo) enlaza más cuentas de Google: Google pregunta con qué cuenta entrar cada vez. Por cada cuenta se marcan los calendarios que se ven (por defecto, solo el principal) y se elige **un calendario por defecto** (cuenta + calendario, escribible) donde se copian los eventos nuevos, también los que crea el bot de Telegram. Al crear o editar un evento se puede elegir otro calendario; si ya estaba en Google y se cambia, se mueve (se borra del anterior y se crea en el nuevo). Los eventos de Google se pintan con el color de su calendario.

- Las conexiones se guardan como `google` (la primera, la de siempre) y `google-<6 hex>` (las demás). Volver a enlazar la misma cuenta la renueva sin duplicarla; «Quitar cuenta» borra solo esa.
- Los ajustes (calendario por defecto y calendarios visibles) están en el ajuste `google-agenda`; cada evento propio guarda su `GoogleCuenta` y `GoogleCalendario` (`null` = la primera cuenta y su calendario principal, como antes de esto).
- API: `GET /api/agenda/estado` (cuentas con calendarios y `defecto`), `PUT /api/agenda/google` (`{ defecto, mostrar }`), `DELETE /api/conexiones/{clave}`. Código en `Agenda/GoogleCalendar.cs` y `GoogleAjustes.cs`.

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
Para la ubicación del iPhone (ver [10](10-nuevas-funciones.md)) acepta además `AGENTE_PYMOBILEDEVICE3` y `AGENTE_IPHONE`.
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

## Fotografía: lo que se añadió en la Fase 4

- **Meteoros y eclipses** (`Luz/Eventos.cs`): fechas fijas (las lluvias se repiten cada año; los eclipses visibles desde España hasta 2028) con la luna de esa noche y, si cae en la previsión, las nubes.
- **Aurora** (`Luz/Aurora.cs`): Kp previsto por el NOAA SWPC (gratis, sin clave) frente al Kp que hace falta en la latitud de tu lugar. Solo hemisferio norte; con latitudes como la de España casi nunca llega a ser posible.
- **Niebla y mar de nubes** (`Luz/Niebla.cs`): Open-Meteo con temperatura, rocío, humedad, viento, nubes bajas y temperatura a 850 hPa; reglas físicas simples, no un modelo de predicción.
- **Calculadora 500 / NPF** (`fotografia/npf.ts`): en el navegador, recuerda tu cámara en `localStorage`.
- **Sitios y diario** (`Luz/Puntos.cs`): hasta 30 sitios; el diario compara la nota prevista con cómo salió y, con 3 salidas o más de cada tipo, dice cuánto fiarte. Aún no sube fotos al diario.
- **AR** (`/fotografia/ar`): cámara trasera, brújula y giroscopio; el arco del sol y la luna de hoy sobre la imagen. Pide permiso (en iPhone, el de movimiento); funciona solo por HTTPS. Supone el móvil derecho y el campo de visión se ajusta a mano.

## Cartera: lo que se añadió en la Fase 5

- **Previsión de cierre de mes** (`Gastos/Prevision.cs`): saldo de hoy menos tu gasto variable medio de los últimos 60 días por los días que quedan, con banda de mejor y peor caso. No cuenta fijos ni nóminas hasta que los apuntas.
- **Dónde gastas** (`Gastos/Lugares.cs`): el atajo de Apple Pay puede mandar `lat` y `lon`; se guardan con ~100 m y se juntan en zonas de ~1 km. Estadísticas enseña el mapa de calor.
- **Reglas que aprenden** (`Gastos/Reglas.cs`): corregir la categoría de un gasto (en la app o con los botones del bot) guarda «comercio → categoría» y pisa a la costumbre.
- **Importar extracto** (`Gastos/Extractos.cs`, pestaña Importar): CSV con detección de separador y columnas, vista previa, duplicados sin marcar.
- **Otras monedas** (`Gastos/Divisas.cs`): tipo de referencia del BCE (últimos 90 días, sin clave); el gasto se guarda en euros con la moneda y el importe original.
- **Informe mensual** (`Gastos/Informe.cs`): PDF con QuestPDF (licencia comunitaria, gratis para uso personal); el día 1 a las 9 se manda por Telegram el del mes anterior. La imagen de Docker instala `libfontconfig1` para poder dibujarlo.

## Inicio y sonido (Fase 6)

- **Semáforo de vida** (`Seguridad/Semaforo.cs`): copias, seguridad, dinero, PC y red en verde, ámbar o rojo con su motivo; el global es el peor.
- **Línea del tiempo** (`Seguridad/Linea.cs`): lo que pasó cada día (gastos, salidas de fotografía, órdenes al PC y accesos), compuesto al momento sin tabla propia.
- **Sonificación** (`core/sonido.service.ts`, Ajustes → Sonido): Web Audio con una nota que sube con la carga del PC. Apagada por defecto, con volumen propio. Todavía no reacciona a las sondas (no hay un contador de sondas que la app pueda consultar).
