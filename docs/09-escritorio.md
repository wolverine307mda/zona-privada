# 9 · Versión de escritorio (sin Docker)

La zona privada también puede correr **sin Docker**, como procesos de Windows que lleva el propio `ZonaPrivada.exe`. Se probó a la vez que Docker:
**Docker sigue siendo la producción** hasta que tú decidas el corte; la versión de escritorio arranca en **ensayo** y no lo toca.

## Qué es cada cosa

| | Docker (hoy) | Escritorio |
|---|---|---|
| API + web | contenedor `api` (`127.0.0.1:8080`) | `componentes\api\Mda.Api.exe` (`127.0.0.1:8090`) |
| Base de datos | contenedor `db` (PostgreSQL 17) | PostgreSQL portable (`127.0.0.1:5433`), datos en `C:\ProgramData\ZonaPrivada\pg` |
| Salida a Internet | contenedor `tailscale` (Funnel) | Tailscale de Windows (`serve` / `funnel`) |
| Copias | contenedor `copias` (`herramientas/copias.sh`) | el lanzador (`CopiasNativas.cs`), mismos nombres y formatos |
| Registros | Seq | ficheros en `C:\ProgramData\ZonaPrivada\registros` (7 días) |
| Agente del PC | dentro del lanzador | igual, hablando con Docker mientras haya ensayo |

Los programas viven en `C:\Program Files\ZonaPrivada\` (solo lectura salvo administradores) y los datos en `C:\ProgramData\ZonaPrivada\`.
Las dos versiones no comparten puertos ni base de datos: pueden estar encendidas a la vez.

## Instalarla

1. Baja PostgreSQL (zip de binarios, EDB) y `age` **de su web oficial** (enlaces en `herramientas/componentes.json`) a una carpeta.
2. Comprueba el SHA-256 de cada archivo contra la web oficial y pónlo en `herramientas/componentes.json` (el script se niega a seguir si falta o no coincide).
3. `powershell -ExecutionPolicy Bypass -File herramientas\empaquetar-nativo.ps1 -Descargas C:\ruta\con\los\zips` → `dist\ZonaPrivada\`.
4. En una consola de administrador: `dist\ZonaPrivada\ZonaPrivada.exe --instalar-escritorio`.
5. En el menú del icono: **Modo escritorio (beta)**. La primera vez ofrece copiar el `.env` de Docker (mismas claves y administrador).
6. **Importar datos de Docker (solo lectura)…** trae la base y las claves; comprueba que hay los mismos usuarios y tablas.

## Ensayo

Mientras `Ensayo` está activo la versión de escritorio **no carga** las claves de Telegram, WhatsApp, correo, Spotify, Google, Tuya, Home Assistant,
AdGuard ni el latido externo, y no manda notificaciones push (`Escritorio:Ensayo`, `Secretos.Silenciables`). Así no hay avisos duplicados, ni dos bots de
Telegram escuchando, ni órdenes dobles a tus aparatos. El agente del PC sigue con Docker.

## Blindaje

- La API y PostgreSQL corren con la cuenta local **`zp-servicio`**, sin privilegios (contraseña aleatoria guardada con DPAPI de máquina). Nunca como tú ni como administrador.
- Permisos NTFS mínimos (`icacls`): solo ve sus carpetas de datos y **tus carpetas compartidas** en lectura o modificación según `MODO_<carpeta>`. La configuración (`.env`) y la contraseña del servicio **no** las puede leer.
- Todo escucha solo en `127.0.0.1`; regla de cortafuegos que bloquea la entrada a los puertos 8090 y 5433; PostgreSQL solo con `scram-sha-256`.
- Cada proceso va en un **Job Object** (muere con el lanzador; la API con tope de 1 GB).
- Los binarios de terceros se aceptan solo con su SHA-256 fijado. `componentes\` no es modificable sin ser administrador.
- Menú **Comprobar seguridad…**: cuenta, puertos expuestos, usuario de los procesos, cortafuegos y legibilidad de la configuración.

## Hacer el corte (cuando quieras)

No lo hace nada solo. Con la versión de escritorio estable y los datos importados:

1. Sal del **ensayo** (menú; avisa de que se duplicarían los avisos si Docker sigue encendido).
2. Para el Funnel de Docker (`docker compose stop tunel`) y abre el de escritorio (menú **Abrir… a Internet (Funnel)**).
3. Los passkeys están atados al dominio: el de escritorio usa el nombre Tailscale de este equipo; registra de nuevo tus llaves desde Seguridad.
4. Cuando todo vaya bien durante unos días, puedes parar Docker. Para **volver**, cambia a «Modo Docker» en el menú: Docker no se ha tocado.

## Límites conocidos

- Tailscale de Windows no se instala solo; el nodo del PC es distinto del nodo del contenedor.
- `serve`/`funnel` usan la sintaxis actual de Tailscale; si cambia, ajustar `Logica/Tailscale.cs`.
- La cuenta `zp-servicio` se crea con el lanzador como administrador; sin esos permisos la versión de escritorio no arranca (no hay modo menos seguro).
- Lo que lee `Docker` en la importación es solo la base y las claves de cifrado; las miniaturas se regeneran.
