# 6. Despliegue y operación

## Dónde corre

- **Equipo de casa**: Windows 11 + Docker Desktop (WSL 2). Proyecto Compose `zona-privada` en `C:\Users\wolve\Proyectos\zona-privada`.
- **Dirección pública**: `https://equipo-de-casa.tail77819b.ts.net` (Tailscale Funnel, perfil `publico`).
- La web pública (`wolverine307mda.github.io`, otro repo) enlaza a la app y pregunta `GET /api/salud` para saber si el equipo está encendido.

## Comandos del día a día

```bash
docker compose --profile publico up -d --build   # levantar todo (primera vez o tras cambios grandes)
docker compose up -d --build api                 # desplegar cambios de código (front + API)
docker compose ps                                # estado
docker compose logs -f api                       # registros de la API (también en Seq: http://localhost:5341)
docker compose restart api                       # reiniciar sin recompilar (relee .env)
docker compose down                              # parar (los volúmenes y datos se conservan)
```

> Cambiar el `.env` → `docker compose up -d api` (recrea el contenedor con las variables nuevas).
> La contraseña del administrador se resincroniza con `ADMIN_PASSWORD` en cada arranque.

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
`WebPublica:Origenes` (opcional) cambia los orígenes CORS de `/api/salud`.

## Tailscale Funnel

- Servicio `tailscale` (perfil `publico`), `hostname: equipo-de-casa`, modo *userspace*, estado en el volumen `tailscale`.
- `tailscale/serve.json`: HTTPS en 443 → `http://api:8080`, con `AllowFunnel` (accesible desde Internet).
- Primera vez: clave de un solo uso en https://login.tailscale.com/admin/settings/keys → `TS_AUTHKEY`. Funnel debe estar
  permitido en la política del tailnet.
- Alternativa: **Cloudflare Tunnel** (perfil `tunel`) + Cloudflare Access con tu email (ver historial del README antiguo en git).

## Copias de seguridad

- El servicio `copias` hace `pg_dump -Fc` una vez al día (a partir de las 3:00, o en cuanto se encienda el equipo)
  en `./copias/zonaprivada-AAAAMMDD-HHMM.dump`, y guarda las 14 últimas automáticas.
- La API avisa (correo/push/Telegram) si la última copia tiene más de 30 horas.
- **Guarda copias también fuera del PC** (otro disco o nube): `./copias` no se sube a git.

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
- **Cambiar la contraseña**: cambia `ADMIN_PASSWORD` en `.env` y `docker compose up -d api`.

## Agente de Windows

```bat
agente\arrancar.cmd                      :: lo compila la primera vez (agente/publicado) y lo deja en segundo plano
dotnet run --project agente/Mda.Agente   :: en primer plano para ver qué hace
```

Arranque automático: acceso directo a `agente\arrancar.cmd` en `shell:startup` (Win + R).

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
