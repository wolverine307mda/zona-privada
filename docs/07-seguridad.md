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
| Sin registro | `/register`, `/forgotPassword`, `/resetPassword`, `/resendConfirmationEmail` → 404 |
| Cookie | `mda.sesion`, `HttpOnly`, `SameSite=Strict`, `Secure` en producción, 8 h deslizantes |
| 2FA | TOTP con app autenticadora + códigos de recuperación |
| Bloqueo | 5 fallos → 15 min |
| Límite por IP | 10 intentos/min en `/api/auth/*` (429) |
| Sesiones revocables | cada cookie lleva un id de sesión comprobado en cada petición; se cierran desde Seguridad |
| Accesos | registro de cada intento de login (IP, dispositivo, motivo), 300 últimos |
| Aviso de inicio | opcional por correo / push / Telegram con hora local |
| Claves de cookie | Data Protection persistido en el volumen `claves` |

## Navegador

- **CSP** estricta: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://*.scdn.co https://*.spotifycdn.com; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`.
- Sin iframes (`frame-ancestors 'none'`), `X-Content-Type-Options: nosniff`, sin scripts de terceros (fuentes locales).
- CORS: solo `GET /api/salud` desde la web pública.
- Markdown de notas: renderizador propio que escapa HTML.
- Documentos abiertos en línea: texto como `text/plain`, sin ejecutar nada.

## Datos y secretos

- Secretos solo en `.env` (ignorado por git) y en *user secrets* en desarrollo. **Nunca** en el repo, ni en tests
  (los tests usan `clave-de-prueba` y bases en memoria).
- Tokens OAuth (Spotify/Google) cifrados con Data Protection; el secreto del cliente nunca llega al navegador; `state` de un solo uso.
- Token de Telegram fuera de los registros (`RemoveAllLoggers()` en sus HttpClients).
- El bot solo responde al chat vinculado con un código de un solo uso.
- El agente usa un token compartido (`X-Agente-Token`); sin token la ruta no existe.
- Documentos: rutas confinadas a cada raíz, sin `..`, sin enlaces simbólicos, borrado a papelera recuperable,
  subidas escritas con nombre temporal.
- Contenedor de la API con usuario sin privilegios.

## Continuidad

- Copia diaria de PostgreSQL (14 días) + aviso si falta. Recomendado: copiar `./copias` fuera del PC.
- Si se filtra un secreto: cambiarlo en el proveedor (BotFather `/revoke`, Google/Spotify, contraseña de aplicación),
  actualizar `.env`, `docker compose up -d api`.
