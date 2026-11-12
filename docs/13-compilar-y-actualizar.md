# Compilar, probar y actualizar (todo y por partes)

Cómo volver a generar la zona privada cuando cambias algo, **solo lo que has tocado**, y cómo llevarlo a los tres sitios donde corre:

| Sitio | Qué es | Dirección | Para qué |
|---|---|---|---|
| **Desarrollo rápido** | API con la base en memoria + el front con recarga en caliente | `localhost:4200` (front) y `localhost:5195` (API) | ver un cambio al instante, sin instalar nada |
| **Pruebas en Docker** | un proyecto de Docker aparte (`zona-privada-pruebas`) con su base, sin avisos ni Telegram | `localhost:8080` | probar de verdad, con datos copiados, sin tocar lo real |
| **La app real (escritorio)** | `ZonaPrivada.exe` con PostgreSQL y la API como procesos de Windows, en `C:\Program Files\ZonaPrivada` | `127.0.0.1:8090` | lo que usas a diario |

El Docker «de siempre» (`compose.yaml`) sigue existiendo como alternativa y se despliega con `actualizar-docker.cmd` ([06](06-despliegue-y-operacion.md)); esta guía se centra en el escritorio y las pruebas.

## 1. Qué toco → qué hago

| He tocado… | Comprueba | Lleva a la app real |
|---|---|---|
| solo el **front** (`web/`) | `npx ng test --watch=false` y `npx ng build` | `.\herramientas\actualizar-escritorio.ps1 -Web` (no para nada) |
| la **API** (`api/`) | `dotnet test ZonaPrivada.slnx -c Release` | `.\herramientas\actualizar-escritorio.ps1 -Api` (incluye la web) |
| el **agente** o el **lanzador** (`agente/`, `lanzador/`) | `dotnet test lanzador/Mda.Lanzador.Tests -c Release` | `.\herramientas\actualizar-escritorio.ps1 -Lanzador` |
| varias cosas, o no sé | todo lo de arriba | `.\herramientas\actualizar-escritorio.ps1` (hace las tres) |
| el widget del iPhone (`herramientas/ios/`) | `node herramientas/ios/probar-widget.js` | se copia a mano a Scriptable ([12](12-siri-y-atajos.md)) |
| una **migración** o un modelo de datos | `dotnet test` y `dotnet ef migrations has-pending-model-changes` | `-Api`: al arrancar hace una copia previa de la base y aplica la migración |

Reglas que no cambian: las migraciones se generan con `dotnet ef migrations add Nombre --output-dir Data/Migraciones` desde `api/Mda.Api` (nunca a mano), con la API compilada en Release; y si cambias algo de la base, haz el `-Api` completo, no solo el front.

## 2. Probar sin instalar nada (desarrollo rápido)

Dos terminales. Los datos viven en memoria y se pierden al parar.

```powershell
# Terminal 1: la API con un usuario de pruebas (wolve / clave-de-prueba)
cd api
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:Datos__EnMemoria='true'
$env:Admin__Usuario='wolve'; $env:Admin__Email='prueba@local.test'; $env:Admin__Password='clave-de-prueba'
dotnet run --project Mda.Api --urls http://localhost:5195
```

```powershell
# Terminal 2: el front con recarga en caliente (proxy de /api a :5195)
cd web
npm start
```

Entra en `http://localhost:4200`. Trucos:

- La primera vez que arrancas, el **primer intento de entrar a veces se queda en «Comprobando…»**: recarga la página y entra otra vez.
- Si cambias solo estilos o plantillas, se actualiza solo. Si cambias algo de TypeScript que afecta a muchas pantallas, recarga a mano.
- Para ver el modo claro rojo sin tocar nada: en la consola del navegador, `document.documentElement.dataset.mode='light'; document.documentElement.dataset.color='rojo'`.
- Para parar: `Ctrl+C` en cada terminal. Si se queda algo vivo, los puertos 4200 y 5195 son los que hay que liberar.

## 3. Pruebas en Docker (con datos copiados, sin avisos)

Para probar la app «de verdad» (PostgreSQL real, PWA) sin tocar la real. No manda Telegram, correo, WhatsApp ni notificaciones, y tus carpetas van en solo lectura. Detalle en [`compose.test.yaml`](../compose.test.yaml).

```powershell
# levantar o actualizar las pruebas con lo que haya en el código (front + API)
docker compose -f compose.yaml -f compose.test.yaml up -d --build api

# ver los registros
docker compose -f compose.yaml -f compose.test.yaml logs -f api

# pararlas
docker compose -f compose.yaml -f compose.test.yaml stop
```

- Sale en `http://localhost:8080` con una barra «Entorno de pruebas». **Usa el mismo puerto que el Docker de siempre**: no las tengas encendidas a la vez.
- La API de Docker construye el front **dentro** de la imagen (`Dockerfile`), así que aquí no hay que compilar nada a mano: `--build` lo rehace todo.
- Cuando te guste, llévalo a la real con el apartado 4.

## 4. La app real (escritorio)

Un solo comando (`actualizar.cmd`, doble clic o desde una consola), por partes. Compilar no pide permisos; instalar sí (Windows pregunta una vez).

```powershell
cd C:\Users\wolve\Proyectos\zona-privada

.\actualizar.cmd                                    # = actualizar-escritorio.ps1 (todo); admite -Web, -Api, -Lanzador, -SoloCompilar

.\herramientas\actualizar-escritorio.ps1 -Web         # solo el front: no cierra nada, recarga la página para verlo
.\herramientas\actualizar-escritorio.ps1 -Api         # API + web: cierra y vuelve a abrir el lanzador
.\herramientas\actualizar-escritorio.ps1 -Lanzador    # el .exe y el agente del PC
.\herramientas\actualizar-escritorio.ps1              # todo
.\herramientas\actualizar-escritorio.ps1 -Api -SoloCompilar   # prepara dist\actualizacion y no instala
```

Lo que hace, en orden:

1. **Compila** lo que pidas: `ng build` (web), `dotnet publish` de la API para Windows (con la web dentro de `wwwroot`) y `dotnet publish` del lanzador. Si algo falla, **no instala nada**.
2. **Pide permisos** una vez e **instala**: copia a `C:\Program Files\ZonaPrivada\componentes\api` y/o `ZonaPrivada.exe`. Con `-Api` o `-Lanzador` cierra el lanzador, la API y PostgreSQL antes de copiar, y lo vuelve a abrir.
3. **Comprueba** que `http://127.0.0.1:8090/api/salud` contesta 200 (espera hasta 2 minutos; el arranque tarda más si hay migraciones).

Qué pasa al arrancar una API distinta:

- El lanzador hace **solo una copia previa de la base** (`antes-de-actualizar-*.dump`, en la carpeta de copias de `C:\ProgramData\ZonaPrivada`) y después se aplican las migraciones nuevas. Las migraciones **no se deshacen**: si algo sale mal, esa copia es el camino de vuelta ([11](11-recuperacion.md)).
- El script deja el registro de la instalación en `dist\actualizacion\actualizar.log`.

### Si algo no va

| Síntoma | Qué hacer |
|---|---|
| «Se canceló el permiso de administrador» | No se ha instalado nada. Vuelve a lanzarlo y acepta el aviso (si no lo ves, mira detrás de otras ventanas o en otro monitor). |
| «La API no ha contestado en 2 minutos» | Mira el icono de la bandeja del lanzador y sus registros. Si el lanzador no está abierto, ábrelo desde `C:\Program Files\ZonaPrivada\ZonaPrivada.exe`. |
| Tras `-Web` sigues viendo lo viejo | La web es una PWA con service worker: recarga forzada (`Ctrl+Shift+R`) o cierra y abre la app. |
| `ng build` o `dotnet publish` falla | Lee el error: no se instala nada hasta que compile. Arregla y repite. |
| Quiero volver atrás en el código | `git checkout <commit> -- .` y repite el comando que toque. Con migraciones nuevas ya aplicadas, usa la copia previa (ver arriba). |

## 5. Docker de siempre (si lo sigues usando)

```powershell
docker compose up -d --build api      # front + API
.\herramientas\desplegar.ps1          # lo mismo con red de seguridad: si no queda sana, vuelve a la versión anterior
.\herramientas\desplegar.ps1 -Volver  # volver a la versión guardada como «anterior»
```

## 6. Antes de subir a GitHub

```powershell
dotnet test ZonaPrivada.slnx -c Release
cd web; npx ng test --watch=false; npx ng build; cd ..
node herramientas/ios/probar-widget.js
git status          # revisa que no vaya ningún secreto: .env está ignorado, pero mira igualmente
```

Un commit por cosa, en español. Los puertos que verás: `4200` y `5195` (desarrollo), `8080` (Docker, de siempre o de pruebas), `8090` (escritorio), `5433` (PostgreSQL del escritorio) y `8765` si abres los bocetos de `disenos/` con `python -m http.server 8765` (esa carpeta es de diseño y no forma parte de la app).
