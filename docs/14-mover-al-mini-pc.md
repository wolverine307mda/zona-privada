# 14. Mover la zona privada al mini PC

La app corre hoy en el portátil y acabará corriendo, para siempre, en un mini PC. Esta guía lo traslada **sin perder nada y sin pasar por el desastre**: el portátil sigue intacto hasta
que decidas apagarlo, y puedes probar el mini PC en paralelo antes del corte. Sirve igual para cualquier otro cambio de equipo.

## Qué se mueve y qué no

| Se mueve (con `mover-equipo.ps1`, **cifrado** con age) | Cómo llega al mini PC |
|---|---|
| Base de datos: cartera, agenda, notas, hábitos, tareas, tokens de Atajos, passkeys… | volcado con `pg_dump` y restaurado con `pg_restore`, comprobando usuarios y tablas |
| Claves de cifrado de la API (`claves`): cookies, sesiones y tokens de Spotify/Google | empaquetadas y desempaquetadas |
| Configuración (`C:\ProgramData\ZonaPrivada\config\.env`): `MDA_CLAVE_MAESTRA` (sin ella las notas y las claves guardadas **no se leen**), contraseña de PostgreSQL, `AGENTE_TOKEN`, `CLOUDFLARE_TUNNEL_TOKEN`… | copiada tal cual (lo de antes se guarda como `.env.antes-del-traslado-…`) |
| Ajustes del lanzador (`%AppData%\ZonaPrivada\config.json`) | copiados |

| NO se mueve solo | Qué hacer |
|---|---|
| Tus archivos de Documentos, Imágenes, etc. (la galería, el explorador) | cópialos o usa OneDrive/Drive; luego pon las rutas nuevas en `CARPETA_*` del `.env` |
| La clave **privada** de `age` | ya debe estar fuera del PC (gestor de contraseñas / USB): sin ella nada se abre |
| El túnel de Cloudflare | no está en el PC: basta el mismo token en el mini PC (y apagar el del portátil, ver «El corte») |
| El gestor de servicios (`serv.wolvem.dev`) | tiene su propia instalación: ver «El gestor de servicios» |

Los **passkeys siguen valiendo** porque el dominio (`app.wolvem.dev`) no cambia y viven en la base de datos.

## 0. Antes (una vez, en el portátil)

- [ ] `COPIAS_AGE_PUBLICA` rellena en `C:\ProgramData\ZonaPrivada\config\.env` y la **clave privada guardada fuera del PC**. `mover-equipo.ps1 -Exportar` no cifra ni exporta sin ella.
- [ ] La zona privada **en marcha** (icono junto al reloj) y con los datos que quieres llevarte.
- [ ] Apunta la versión que corre (`/api/salud` → `version`). En el mini PC instala **la misma o una posterior**: las migraciones solo van hacia delante.

## 1. Preparar el mini PC

1. Windows al día, tu usuario con permisos de administrador, **IP reservada** en el router y la BIOS en «encender tras un corte de luz».
2. Energía: nunca suspender ni hibernar el equipo (Configuración › Sistema › Energía), y un SAI si puedes.
3. Para Wake-on-LAN desde fuera (`Encender el PC` en Atajos) activa WoL en la BIOS y en la tarjeta de red.
4. Instala Git, el SDK de .NET, Node.js y [age](https://github.com/FiloSottile/age/releases) **solo si vas a compilar allí** (recomendado: así `actualizar.cmd` y el gestor funcionan igual que hoy). Si prefieres compilar siempre en el portátil, basta con copiar `dist\ZonaPrivada\`.
5. Clona el repositorio en la misma ruta que en el portátil si puedes (`C:\Users\<tú>\Proyectos\zona-privada`): el catálogo del gestor y tus costumbres siguen valiendo.
6. Instala la versión de escritorio ([09](09-escritorio.md)): `powershell -File herramientas\empaquetar-nativo.ps1 -Descargas <zips>` y, como administrador, `dist\ZonaPrivada\ZonaPrivada.exe --instalar-escritorio`.
   **Todavía no la abras** (o, si ya la abriste, ciérrala y borra `C:\ProgramData\ZonaPrivada\pg`: el paso 3 necesita que la base se cree *después* de colocar la configuración).

## 2. Exportar (en el portátil)

```powershell
.\herramientas\mover-equipo.ps1 -Exportar -Carpeta D:\traslado
```

Pide permiso de administrador (la configuración solo la leen ellos). Deja en la carpeta cuatro `.age` (base, claves, configuración, ajustes del lanzador), un `traslado.json` (cifras para comprobar al restaurar: usuarios, tablas, versión) y un `LEEME.txt`. Todo lo secreto va cifrado: la carpeta puede viajar en un USB o por la nube.
Si algo falla (la zona privada parada, sin clave de age, carpeta no vacía…), lo dice y no deja nada a medias.

Lleva la carpeta al mini PC (USB, red local…) y ten a mano `clave-privada.txt`.

## 3. Restaurar (en el mini PC) — dos pasadas

```powershell
.\herramientas\mover-equipo.ps1 -Restaurar -Carpeta D:\traslado -ClavePrivada D:\clave-privada.txt
```

**1.ª pasada** (la base aún no existe): coloca `.env`, claves y ajustes del lanzador. Después:

1. Revisa en `C:\ProgramData\ZonaPrivada\config\.env` las rutas **de este equipo**: `CARPETA_*`, `COPIAS_NUBE`, `COPIAS_DRIVE` (las del portátil no valen).
2. Abre la zona privada **una vez** y espera a que el icono diga «en marcha»: así PostgreSQL crea su base con la contraseña del paquete.
3. **Déjala en ensayo** (menú del icono) hasta el corte: en ensayo no carga Telegram, correo, Google, Spotify…, así que no habrá dos bots ni avisos duplicados con el portátil encendido.

**Segunda pasada**: el mismo comando. Descifra la base, guarda una copia de lo que hubiera (`copias\antes-del-traslado-…dump`), la sustituye, comprueba que usuarios y tablas son **los del paquete** y reinicia la zona privada. Si algo no cuadra, se detiene y dice dónde está la copia de antes.

## 4. Probar en paralelo (sin tocar el portátil)

En el mini PC, con el ensayo activo: abre `http://127.0.0.1:8090`, entra, mira la cartera, abre una nota (si se lee, la clave maestra y las claves de cookies están bien) y la galería. El portátil sigue siendo el que atiende `app.wolvem.dev`.
Si algo falla, vuelve a empezar la restauración: el paquete no se consume.

## 5. El corte (cuando quieras, unos 10 minutos)

Un túnel de Cloudflare con el **mismo token** en dos equipos reparte las visitas entre los dos: por eso se apaga el viejo **antes** de encender el nuevo.

1. **Portátil**: `.\herramientas\control-zona.ps1 parar` y `.\herramientas\instalar-tunel.ps1 -Quitar`.
   *(Si has escrito datos desde que exportaste, repite el paso 2 y la 2.ª pasada del paso 3 para llevártelos.)*
2. **Mini PC**: sal del **ensayo** en el menú del icono (a partir de aquí hablan Telegram, correo, Google, Spotify y el resto).
3. **Mini PC**: `.\herramientas\instalar-tunel.ps1` (usa el `CLOUDFLARE_TUNNEL_TOKEN` de la configuración; en Cloudflare, `app.wolvem.dev` → `http://localhost:8090`, como hoy).
4. Comprueba `https://app.wolvem.dev/api/salud`, entra con tu passkey y manda un aviso de prueba por Telegram (con «Probar» en la app).
5. **Ajustes › Tipo de equipo › Mini PC** (cambia el dibujo de «Tu PC por dentro»).
6. El **agente del PC** (el que apaga, mide y despierta aparatos) vive dentro del lanzador: a partir de ahora controla el mini PC. Revisa Red/«Encender el PC» si tenías aparatos con la MAC del portátil.

## El gestor de servicios

Es una instalación aparte (`gestor-servicios`): en el mini PC se instala igual (`herramientas\instalar-gestor.ps1`, como administrador) y `C:\ProgramData\Gestor\config\proyectos.json` tiene que apuntar a las **carpetas del mini PC** (`{raiz}`). Su base solo guarda historial, así que no hace falta moverla; el passkey de `serv.wolvem.dev` sí hay que registrarlo de nuevo en esa instalación. Hasta entonces el gestor del portátil puede seguir en marcha (no compiten: cada uno despliega en su equipo).

## Volver atrás

- Hasta que desinstales nada del portátil, volver es: `control-zona.ps1 parar` e `instalar-tunel.ps1 -Quitar` en el mini PC, ensayo **desactivado** y `instalar-tunel.ps1` en el portátil, y abrir allí la zona privada.
- Lo escrito en el mini PC no vuelve solo: `-Exportar` en el mini PC y `-Restaurar` (2 pasadas) en el portátil.

## Después

- Las copias cifradas diarias (OneDrive/Drive) **ya incluyen la configuración** (`…-entorno.age`) desde esta versión, así que el simulacro de recuperación ([11](11-recuperacion.md)) sirve también para el escritorio.
- Haz un simulacro de verdad en el mini PC al mes de estar allí: restaurar desde las copias de la nube en una carpeta temporal.
- Cuando pasen unas semanas, desinstala del portátil (`ZonaPrivada.exe --desinstalar`) y quita su carpeta de datos.

## Probado y sin probar

- **Probado** (con dos PostgreSQL desechables, sin tocar la app real): exportar, la 1.ª y la 2.ª pasada, que nada secreto queda en claro en los `.age`, que se guarda lo anterior, que los datos del destino son los del origen y que una clave privada equivocada o una clave pública de age vacía se rechazan.
- **Sin probar en un mini PC real:** la instalación, el reinicio con la tarea «Zona privada», el túnel de Cloudflare y el corte. La primera vez, hazlo con calma y mirando.
