# 7. Seguridad

## Modelo

Una sola persona usuaria, datos personales sensibles (dinero, agenda, archivos del PC), servidor en casa expuesto a
Internet. Objetivos: que nadie más entre, que un robo de cookie o de token valga poco y que los datos no se pierdan.

## Exposición de red

- Ningún puerto abierto en el router. La API escucha en `127.0.0.1:8080` y Seq en `127.0.0.1:5341`.
- Internet llega solo por **Tailscale Funnel** (HTTPS con certificado de Tailscale) o, como alternativa, Cloudflare Tunnel (+ Access).
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

- **CSP** estricta: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://*.scdn.co https://*.spotifycdn.com; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`.
- Sin iframes (`frame-ancestors 'none'`), `X-Content-Type-Options: nosniff`, sin scripts de terceros (fuentes locales).
- `Strict-Transport-Security` (solo por HTTPS), `Cross-Origin-Opener-Policy: same-origin`, `Cross-Origin-Resource-Policy: same-origin` (salvo `/api/salud` y `/api/publico`), `Cache-Control: no-store` en `/api/*`, `Permissions-Policy` con `geolocation=(self)` y sin cabecera `Server`.
- `AllowedHosts` limita la cabecera `Host` (`ALLOWED_HOSTS` en `.env`).
- CORS: solo `GET` desde la web pública, en `/api/salud` y `/api/publico/*` (galería y estado en vivo), con límite de ritmo propio.
- **Galería pública**: nada sale sin activar el interruptor y marcar cada foto; el id es aleatorio, la ruta real nunca viaja y siempre se sirve una copia WebP recomprimida (el codificador no copia EXIF: sin GPS ni cámara) con caché inmutable.
- **Globo 3D**: cada foto decide si sale; al público solo llegan el nombre y la posición **redondeada a ~10 km**, y nunca nada dentro del radio excluido alrededor de tu casa. El GPS exacto del archivo solo lo ve el dueño en la privada.
- **Atajos de iOS**: tokens con alcance (gasto, pago, lectura), guardados como hash SHA-256, revocables, sin cookie y sin acceso al resto de la app; aviso por Telegram desde una red nueva.
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

## Continuidad

- Copia diaria de PostgreSQL (14 días) + aviso si falta. Recomendado: copiar `./copias` fuera del PC.
- Si se filtra un secreto: cambiarlo en el proveedor (BotFather `/revoke`, Google/Spotify, contraseña de aplicación),
  actualizar `.env`, `docker compose up -d api`.
