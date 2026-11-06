# 11. Plan de recuperación

Qué hacer si el equipo de casa muere (disco, robo, Windows roto, incendio). El objetivo: **volver a tener la app y todos los datos en otro equipo en menos de una hora**,
solo con lo que está fuera del PC.

## Qué tienes fuera del PC (y qué no)

| Qué | Dónde está | Cómo se protege |
|---|---|---|
| Base de datos (cartera, agenda, notas, hábitos, tareas…) | `zonaprivada-AAAAMMDD.dump.age` en OneDrive **y** en Google Drive | cifrado con `age`; 14 diarias + 8 semanales |
| Claves de las cookies (volumen `claves`) | `zonaprivada-AAAAMMDD-claves.tar.age`, mismos sitios | cifrado |
| Configuración y secretos (`.env`) | `zonaprivada-AAAAMMDD-entorno.age`, mismos sitios | cifrado |
| **Clave privada de `age`** | **tu gestor de contraseñas / USB, NUNCA en el PC ni en las carpetas de copias** | es lo único que abre lo anterior: si la pierdes, las copias no valen |
| Código | GitHub (`zona-privada`) | — |
| Tus archivos de Documentos, Imágenes, etc. | **solo en el PC** (no entran en estas copias) | usa la copia de Windows / OneDrive de carpetas conocidas |
| Estado de Tailscale | volumen `tailscale` (se pierde) | en el equipo nuevo se registra otro nodo con `TS_AUTHKEY` nueva |

> La contraseña de Postgres, `MDA_CLAVE_MAESTRA` (que cifra notas y claves de servicios) y `AGENTE_TOKEN` viven en el `.env`: por eso va cifrado en las copias.
> Sin `MDA_CLAVE_MAESTRA` las notas y las claves guardadas **no se pueden leer**, aunque tengas el dump.

## Recuperar en un equipo nuevo

1. Instala Docker Desktop (WSL 2), Git y [age](https://github.com/FiloSottile/age/releases).
2. `git clone` del repo en la carpeta que quieras.
3. Baja de OneDrive o Drive los tres archivos del **mismo día** (`…dump.age`, `…-claves.tar.age`, `…-entorno.age`) a una carpeta temporal.
4. Descifra con tu clave privada (`clave-privada.txt`, la guardada fuera del PC):

   ```bash
   age -d -i clave-privada.txt zonaprivada-AAAAMMDD-entorno.age > .env
   age -d -i clave-privada.txt zonaprivada-AAAAMMDD.dump.age > copia.dump
   mkdir claves-restauradas && age -d -i clave-privada.txt zonaprivada-AAAAMMDD-claves.tar.age | tar -x -C claves-restauradas
   ```
5. Edita el `.env` recién descifrado: rutas de carpetas (`CARPETA_*`, `COPIAS_NUBE`, `COPIAS_DRIVE`) del equipo nuevo y, si hace falta, `TS_AUTHKEY` nueva.
6. Levanta solo la base y restaura:

   ```bash
   docker compose up -d db
   docker compose exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists' < copia.dump
   ```
7. Restaura las claves de las cookies en el volumen (sin esto, las sesiones y los datos protegidos por Data Protection no se leen):

   ```bash
   docker compose create api
   docker run --rm -v zona-privada_claves:/claves -v "$PWD/claves-restauradas:/origen:ro" alpine sh -c 'cp -a /origen/. /claves/'
   ```
8. `docker compose --profile publico up -d --build` y comprueba `curl http://localhost:8080/api/salud`.
9. Entra; si la dirección pública cambió, actualiza `URL_PUBLICA` (los **passkeys** están atados al dominio: si cambia, usa contraseña + 2FA y registra los passkeys otra vez) y avisa a la web pública.

## Simulacro completo (hazlo una vez al año)

El simulacro semanal automático solo prueba que el dump **se restaura**. El de verdad, por lo menos una vez:

1. En otra carpeta (o una VM), sigue los pasos de arriba con las copias de OneDrive/Drive **sin tocar el PC de casa** (puertos distintos: `ports` a `127.0.0.1:8090:8080` en un `compose.override.yaml`).
2. Entra, mira la cartera y descifra una nota: si se ve, la clave maestra y las claves de cookies están bien.
3. Anota la fecha y cuánto has tardado al final de este archivo.

| Fecha | Tiempo | Qué falló |
|---|---|---|
| _(pendiente del primer simulacro)_ | | |

## Antes de que pase: lista de comprobación

- [ ] `COPIAS_AGE_PUBLICA` rellena y la clave privada guardada **fuera** del PC (y en dos sitios).
- [ ] `COPIAS_NUBE` (OneDrive) y `COPIAS_DRIVE` (Google Drive para escritorio) apuntan a carpetas que sincronizan de verdad.
- [ ] En esas carpetas aparecen, tras la copia de las 3:00, **tres** archivos `.age` nuevos por día.
- [ ] La app te manda «✅ copia verificada» por Telegram cada semana.
- [ ] Un segundo sitio con los datos de acceso: Tailscale, GitHub, dominio, `HEALTHCHECK_URL`.

## Desplegar sin miedo (y volver atrás)

`herramientas/desplegar.ps1` (o `.sh` en Linux/WSL) guarda la imagen actual como `…:anterior`, reconstruye, espera a que la API esté sana y, si no arranca, **vuelve sola** a la anterior.
`.\herramientas\desplegar.ps1 -Volver` regresa a mano. Las migraciones que solo añaden tablas o columnas no impiden volver atrás; si una migración borra o cambia columnas, restaura también la copia de antes del despliegue.
