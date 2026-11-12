# 6. Despliegue y operación

## Dónde corre

- **Equipo de casa**: Windows 11 + Docker Desktop (WSL 2). Proyecto Compose `zona-privada` en `C:\Users\wolve\Proyectos\zona-privada`.
- **Dirección pública**: `https://equipo-de-casa.tail77819b.ts.net` (Tailscale Funnel, perfil `publico`).
- La web pública (`wolverine307mda.github.io`, otro repo) enlaza a la app y pregunta `GET /api/salud` para saber si el equipo está encendido.

## Comandos del día a día

```bash
docker compose --profile publico up -d --build   # levantar todo (primera vez o tras cambios grandes)
docker compose up -d --build api                 # desplegar cambios de código (front + API)
.\herramientas\desplegar.ps1                      # lo mismo con red de seguridad: espera a que esté sana y, si no, vuelve a la versión anterior
docker compose ps                                # estado
docker compose logs -f api                       # registros de la API (también en Seq: http://localhost:5341)
docker compose restart api                       # reiniciar sin recompilar (relee .env)
docker compose down                              # parar (los volúmenes y datos se conservan)
```

> Cambiar el `.env` → `docker compose up -d api` (recrea el contenedor con las variables nuevas).
> La contraseña del administrador se resincroniza con `ADMIN_PASSWORD` solo cuando **cambia en el `.env`**; una cambiada desde Perfil no se deshace al reiniciar.

## Variables de entorno

`.env` (no se sube a git; plantilla en `.env.example`) → `compose.yaml` → configuración de .NET (`__` = `:`).

| `.env` | Configuración en la API | Notas |
|---|---|---|
| `POSTGRES_PASSWORD` | `ConnectionStrings:Postgres` | también lo usan `db` y `copias` |
| `ADMIN_USUARIO` / `ADMIN_EMAIL` / `ADMIN_PASSWORD` | `Admin:Usuario` / `Admin:Email` / `Admin:Password` | se entra con el usuario |
| `SEQ_PASSWORD` | (Seq) | contraseña inicial del admin de Seq |
| — | `Seq:Url=http://seq:5341` | fijo en compose |
| — | `DataProtection:Ruta=/claves` | volumen `claves` |
| `AGENTE_TOKEN` | `Agente:Token` | sin él no existe `/api/sistema/agente` |
| `CARPETA_DOCUMENTOS` … `CARPETA_VIDEOS` | montajes en `Archivos:Base=/carpetas` | rutas de Windows con `/` |
| `URL_PUBLICA` | `App:UrlPublica` | enlaces de correos y vueltas OAuth |
| `ZONA_HORARIA` | `App:Zona` | por defecto `Europe/Madrid` |
| `CORREO_SERVIDOR/PUERTO/USUARIO/CLAVE/PARA` | `Correo:*` | SMTP |
| `WHATSAPP_TELEFONO/APIKEY` | `WhatsApp:*` | CallMeBot |
| `TELEGRAM_TOKEN` | `Telegram:Token` | bot |
| `SPOTIFY_CLIENT_ID/SECRET`, `GOOGLE_CLIENT_ID/SECRET` | `Spotify:*`, `Google:*` | OAuth |
| — | `Copias:Ruta=/copias` | la API vigila que haya copia reciente |
| `TS_AUTHKEY` | (tailscale) | solo la primera vez; luego usa su estado guardado |
| `CLOUDFLARE_TUNNEL_TOKEN` | (cloudflared) | perfil `tunel` |

Solo en desarrollo: `Datos:EnMemoria=true` (con `ASPNETCORE_ENVIRONMENT=Development`) usa EF InMemory.
`WebPublica:Origenes` (opcional) cambia los orígenes CORS de `/api/salud` (por defecto `https://wolvem.dev` y `https://wolverine307mda.github.io`).

## Tailscale Funnel

- Servicio `tailscale` (perfil `publico`), `hostname: equipo-de-casa`, modo *userspace*, estado en el volumen `tailscale`.
- `tailscale/serve.json`: HTTPS en 443 → `http://api:8080`, con `AllowFunnel` (accesible desde Internet).
- Primera vez: clave de un solo uso en https://login.tailscale.com/admin/settings/keys → `TS_AUTHKEY`. Funnel debe estar
  permitido en la política del tailnet.
- Alternativa: **Cloudflare Tunnel** (perfil `tunel`) + Cloudflare Access con tu email (ver historial del README antiguo en git).

## Copias de seguridad

El contenedor `copias` ejecuta [`herramientas/copias.sh`](../herramientas/copias.sh) cada 10 minutos:

1. **Copia diaria** (a partir de las 3:00, o en cuanto se encienda el equipo): `pg_dump -Fc` en `./copias/zonaprivada-AAAAMMDD-HHMM.dump`;
   se guardan las 14 últimas. La API avisa (correo/push/Telegram) si la última tiene más de 30 horas.
2. **Copia cifrada fuera del PC** (si hay `COPIAS_AGE_PUBLICA` en el `.env`): el dump, el volumen `claves` (las claves de las cookies) y el propio `.env`
   se cifran con [age](https://github.com/FiloSottile/age) y se copian a `COPIAS_NUBE` (una carpeta que sincronice OneDrive) **y** a `COPIAS_DRIVE`
   (una carpeta dentro de «Mi unidad» de Google Drive para escritorio). Cómo recuperar todo: [11-recuperacion.md](11-recuperacion.md).
   Se guardan 14 diarias y 8 semanales (`semanal/`, las de domingo). Sin la clave privada nadie puede leerlas.
   - Una vez: `age-keygen -o clave-privada.txt`, **guarda ese archivo fuera del PC** (gestor de contraseñas, USB), y pon la línea
     `Public key: age1…` en `COPIAS_AGE_PUBLICA`. Después `docker compose up -d copias`.
   - Descifrar: `age -d -i clave-privada.txt zonaprivada-AAAAMMDD.dump.age > copia.dump` (y `…-claves.tar.age | tar -x`).
3. **Simulacro semanal de restauración**: la última copia se restaura en una base vacía (`simulacro`, que se borra al terminar) y el
   resultado queda en `./copias/.ultimo-simulacro`. La API lo manda a Telegram: «✅ copia verificada» o «⚠️ NO se pudo restaurar».

Copia manual:

```bash
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > copias/manual-$(date +%Y%m%d-%H%M).dump
```

Restaurar (⚠️ sustituye los datos actuales):

```bash
docker compose stop api
docker compose exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists' < copias/ARCHIVO.dump
docker compose start api
```

## Latido («PC apagado»)

Con `HEALTHCHECK_URL` (un check gratuito de [healthchecks.io](https://healthchecks.io) con periodo de 5 min y margen de 15) la API hace un GET
cada 5 minutos. Si el PC se apaga, se corta internet o Docker se cae, el latido para y Healthchecks te avisa por correo o Telegram.

## Base de datos a mano

```bash
docker compose exec -T db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

- Usuario `mda`, base `zonaprivada`. Tablas propias en **minúsculas** (`movimientos`, `eventos`, `ajustes`…),
  columnas en **PascalCase entre comillas** (`"Fecha"`, `"Importe"`); tablas de Identity con mayúsculas (`"AspNetUsers"`).

Recetas:

```sql
-- desbloquear la cuenta tras 5 fallos
update "AspNetUsers" set "LockoutEnd" = null, "AccessFailedCount" = 0;
-- ver el horario de trabajo
select "Valor" from ajustes where "Clave" = 'horario';
-- últimos accesos
select "Fecha", "Ip", "Exito", "Motivo" from accesos order by "Id" desc limit 20;
```

- **Desactivar el 2FA** si se pierde el móvil: usar un código de recuperación al entrar, o
  `update "AspNetUsers" set "TwoFactorEnabled" = false;`.
- **Cambiar la contraseña**: desde Perfil, o cambia `ADMIN_PASSWORD` en `.env` (mínimo 12 caracteres) y `docker compose up -d api`.

## «Zona privada.exe» (arranque con un clic)

Un solo programa (`lanzador/Mda.Lanzador`) que sustituye a abrir Docker, `docker compose up` y `agente\arrancar.cmd`:

```bat
dotnet publish lanzador\Mda.Lanzador -c Release -o lanzador\publicado   :: genera lanzador\publicado\ZonaPrivada.exe (autocontenido, ~50 MB)
lanzador\publicado\ZonaPrivada.exe --instalar                           :: un aviso de administrador: tarea de inicio + accesos directos
lanzador\publicado\ZonaPrivada.exe --desinstalar                        :: lo quita
```

- Al abrirlo: arranca Docker Desktop si hace falta, espera a que responda, ejecuta `docker compose --profile publico up -d` y **aloja el agente**
  del PC dentro (ya no hace falta `mda-agente.exe`). Vive en la bandeja junto al reloj: verde = todo en marcha, ámbar = funciona pero algo falla
  (sin salida a Internet, un contenedor parado, el agente sin conectar), rojo = no hay app (se vuelve a levantar solo cada 5 minutos).
- Menú del icono: abrir la app, abrir en local, registros (Seq), reiniciar la API, **actualizar** (reconstruir la API), arrancar/parar todo,
  «Iniciar con Windows», abrir la carpeta y el registro (`%AppData%\ZonaPrivada\lanzador.log`).
- «Iniciar con Windows» crea una **tarea programada al iniciar sesión con privilegios máximos** (20 s de retraso): arranca sola y sin avisos de
  administrador. Con administrador el agente puede leer la temperatura y el desgaste de los discos.
- Lee `AGENTE_TOKEN` y `URL_PUBLICA` del `.env` de la carpeta del proyecto (la busca sola; si no, te la pregunta una vez).
- Para actualizar el propio `.exe`, ciérralo desde el menú antes de volver a publicar.
- Un PC apagado o suspendido no se puede arrancar desde fuera; el icono solo trabaja con la sesión de Windows iniciada.

## Agente de Windows (sin el lanzador)

```bat
agente\arrancar.cmd                      :: lo compila la primera vez (agente/publicado) y lo deja en segundo plano
dotnet run --project agente/Mda.Agente   :: en primer plano para ver qué hace
```

Sigue valiendo como respaldo (acceso directo a `agente\arrancar.cmd` en `shell:startup`). No lo uses a la vez que el lanzador.

## Problemas típicos

| Síntoma | Causa / solución |
|---|---|
| «Usuario o contraseña incorrectos» con la contraseña buena | cuenta bloqueada 15 min (5 fallos) o límite por IP → esperar o desbloquear (SQL de arriba) y `docker compose restart api` |
| El panel enseña la memoria/disco de Docker, no del PC | el agente no está en marcha o `AGENTE_TOKEN` no coincide |
| En el iPhone sigue la versión vieja / la foto antigua | el service worker sirve de caché y refresca en segundo plano: cerrar y abrir la app; para recursos versionar la URL |
| El bot de Telegram no responde | `TELEGRAM_TOKEN` vacío/revocado (ver registros: «Bot de Telegram … escuchando»), o el chat no está vinculado |
| Spotify/Google: error de redirección | la URI de vuelta del proveedor debe ser exactamente `<URL_PUBLICA>/api/conexiones/<id>/vuelta` |
| No llegan correos | contraseña de aplicación de Gmail; probar en Seguridad › Avisos › Probar |
| `dotnet build` falla con «archivo en uso» | hay una API de desarrollo corriendo; párala antes de compilar |
| Documentos vacío | las rutas `CARPETA_*` no existen o Docker Desktop no tiene acceso a esa unidad |

## Rendimiento del servidor (medido)

- **Compresión**: Brotli y gzip para JSON y para el front (JS, CSS, HTML, SVG). Antes solo se comprimía el JSON: el `main` salía de 126 KB y ahora de 48 KB; el CSS de 10 KB a 4 KB.
- **Caché del front**: los archivos con hash en el nombre (`main-XXXXXXXX.js`) se guardan un año (`immutable`); `index.html`, `sw.js` y las rutas de la app se revisan siempre, así un despliegue nuevo se ve al recargar. Código en `Seguridad/CacheFront.cs`.
- **Imagen Docker**: la API se publica precompilada (ReadyToRun, `linux-x64`) para arrancar y contestar antes; el contenedor `api` tiene `healthcheck` sobre `/api/salud`.
- **Galería privada**: la lista de «tu carpeta de imágenes» busca las fotos publicadas en un diccionario en vez de recorrer una lista por cada archivo.
- **Documentos**: el recuento de elementos de las carpetas (`/api/archivos/raices`) se recuerda 60 s.
- **Front**: las pantallas se descargan en reposo (`PreloadAllModules`), así que al pulsar un menú ya están.

## Ejecutable «ZonaPrivada.exe» (lanzador 1.2)

Un solo programa que lo arranca todo y vive junto al reloj. Desde la 1.1 se vale solo:

- **Primer arranque**: si falta algo en el `.env`, inventa las claves que se pueden inventar (contraseña de la base, de Seq, token del agente y la **clave maestra de cifrado**), pide el correo y la
  contraseña del administrador (mínimo 12 caracteres) y enseña la clave maestra para guardarla fuera del PC. Nunca cambia una clave que ya tiene valor (cambiar la de la base o la maestra dejaría los datos ilegibles)
  y deja una copia `.env.copia-…` (ignorada por git).
- **Docker Desktop**: si no está instalado, ofrece instalarlo con `winget`.
- **Arranque**: Docker → `compose up -d` → espera a que `/api/salud` conteste de verdad (hasta 3 min) → agente → comprobación de la salida a Internet.
- **Salida a Internet** (menú): enciende o apaga el perfil `publico` (Tailscale); se guarda en `%AppData%\ZonaPrivada\config.json`.
- **Actualizar**: `git pull --ff-only` (si la carpeta es un clon) y reconstruye la API; también hay «reconstruir sin traer cambios».
- **Monitor del PC y registro…**: ventana con procesador, memoria, disco, red, los procesos que más gastan y el registro del lanzador en vivo.
- **Exportar diagnóstico**: un zip en el escritorio con el estado de Docker, los últimos registros de la API, el registro del lanzador y los **nombres** del `.env` con «definida / vacía» (nunca los valores).
- La lógica pura (`Logica/Entorno.cs`, `Logica/Primera.cs`) tiene pruebas en `Mda.Lanzador.Tests`; las ventanas y el arranque real se prueban a mano.

Se publica con `dotnet publish lanzador/Mda.Lanzador -c Release -o lanzador/nuevo-1.2`. Si el `.exe` anterior está abierto, ciérralo desde la bandeja antes de sustituirlo.

## Presencia automática (llegué / me voy)

Al añadir tu móvil en «Mi PC → Aparatos de casa» y marcar **«Es mi móvil»**, el vigilante de la red (`VigilanteRed`, cada minuto) decide si estás en casa: con el móvil en la red, **llegada** (al momento); con
todos los móviles marcados fuera durante **10 minutos**, **salida** (los móviles se duermen y dejan de contestar un rato). Al cambiar de estado aplica solo `Casa.Aplicar` («llegué a casa» / «me voy»), como el atajo del iPhone.
El primer estado que se ve solo se apunta (`presencia-estado` en `ajustes`) para no tocar el PC al activarlo. Si el agente no está en marcha, te lo cuenta por Telegram.
Para los móviles, un «conexión rechazada» cuenta como «está en la red» (no suelen tener ningún puerto abierto). Usa una IP fija del móvil en el router (o su IP de Tailscale); con la dirección Wi-Fi privada de iOS cambiante no funciona.

## Historial de latencia (router)

Marcando «Guardar su historial de latencia» en un aparato (hasta 3, pensado para el router) el vigilante guarda **una lectura por minuto** (`red_lecturas`). Pasadas 48 horas se resumen en una fila por hora completa
(`red_horas`: lecturas, caídas, latencia media y máxima) y a los 30 días se borran, así la gráfica del mes son 720 puntos y la base no crece sin parar. La latencia es el tiempo de abrir una conexión TCP: dentro del contenedor
no hay permiso para ICMP (ping), y para ver si la red va lenta o se corta da lo mismo. En Mi PC → Aparatos de casa sale el botón «Gráfica» con 24 h, 7 días o 30 días.

## Test de velocidad de Internet

El agente mide contra el servidor público de Cloudflare (`speed.cloudflare.com`): latencia (mediana de 4 peticiones), bajada (hasta 25 MB o 12 s) y subida (8 MB), unos **35 MB por prueba**, y manda las tres cifras a la API
(tabla `velocidades`, últimas 400). `VelocidadNocturna` pide una cada noche entre las 3 y las 5 (hora de casa) si hace más de 20 horas de la última y el agente está en marcha; en Mi PC → «Velocidad de Internet» hay
«Medir ahora» y la curva del mes. Para quitar la prueba nocturna, para el agente por la noche o quita el servicio en `Program.cs`.
