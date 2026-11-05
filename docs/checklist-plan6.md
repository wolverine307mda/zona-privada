# Checklist · Plan 6 (nuevas funciones, todas gratis)

Estados: ⬜ pendiente · ⏭️ próximo · 🔄 en proceso · ✅ hecho (con el commit).
Numeración = la de la lista de ideas que se habló con Mario (las que no se piden, 1, 3, 6 y 7, no están aquí).

## Reglas de este plan

- **Todo gratis y sin claves nuevas**: Open-Meteo (ya integrado), CelesTrak, OpenStreetMap, Tesseract, PdfPig, MetadataExtractor (ya está) y poco más. Nada de servicios de pago ni de IA externa.
- Un commit por idea (o por fase si es grande), en español, con `Co-Authored-By`. **Sin `git push`** y **sin desplegar** hasta que Mario lo diga.
- Carpeta compartida con otras sesiones: `git add` siempre con rutas explícitas; no tocar lo ajeno (`checklist-plan5`, escritorio nativo).
- Cada idea termina con: tests de la lógica pura (`Mda.Api.Tests`), `dotnet test ZonaPrivada.slnx`, `cd web && npx ng build` y `ng test`, prueba en móvil (375 px) con la API en memoria, y su sección en `docs/02-backend.md`, `03-api.md` y `04-frontend.md`.
- Migraciones: `dotnet ef migrations add Nombre --output-dir Data/Migraciones` desde `api/Mda.Api`. Toda tabla nueva con índice por lo que se consulta.
- Lógica en funciones puras con `TimeProvider` (así se prueba sin esperar): el patrón de `Prevision.cs`, `Calculos`, `EvaluadorReglas`.
- Los avisos usan lo que ya hay (`IAvisos`: Telegram, Web Push, correo) y se **deduplican con claves en `Ajustes`** (como `copias-avisado`), para que no se repitan.
- Diseño de pantallas: tokens de `styles.css` y componentes de `web/src/app/ui/`; nada de colores ni radios a mano; claro y oscuro; `prefers-reduced-motion`.

## Lo que ya existe (verificado en el código) y lo que falta

| # | Idea | Ya hay | Falta |
|---|---|---|---|
| 4 | Presupuestos con aviso al 80 % | `Presupuesto` (límite por categoría), rutas `/presupuestos`, el atajo de iPhone dice «al X % del presupuesto», el semáforo cuenta los pasados | el **aviso proactivo** (al 80 % y al 100 %) y el ritmo que te queda por día |
| 5 | Previsión de fin de mes | `Gastos/Prevision.cs` + tarjeta en Cartera (media de 60 días + banda) | que cuente **fijos y nóminas pendientes**, una gráfica día a día y explicar de dónde sale el número |
| 13 | Salud del PC con aviso | el agente manda cada 5 min `SaludPc` (discos, SMART de Windows, temperatura, reinicio pendiente) y el histórico de batería; el semáforo cuenta `AlertasPc` | **histórico** de espacio y temperatura, avisos con umbrales propios y «el disco se llena en N días» |
| 14 | Accesos nuevos | tabla `Acceso`, sesiones con IP y equipo, `GeoIp` (de las sondas), aviso opcional **en cada** inicio de sesión | avisar **solo si es nuevo** (equipo o país), lista de equipos conocidos, historial con ciudad y país |
| 16 | Buscador que actúa | Ctrl K: secciones, notas, crear algo y mandar frases al asistente (abre su diálogo) | previsualizar y **ejecutar en el sitio**, comandos (`>`), buscar en agenda, movimientos y documentos |
| 17 | iPhone | PWA con service worker, Web Push, tokens de Atajos con `/resumen` | `/api/atajos/hoy`, vista `/hoy`, widget con Scriptable, globo con número (Badging). **Un widget propio de iOS no es posible con una PWA** (ver la fase E) |
| 8 | Planificador de salidas | `Luz` (sol, luna, día, semana, aurora, niebla), `PuntoFoto` con orientación, diario de `SalidaFoto` con calibración | el **ranking** de las mejores salidas y el aviso del día antes |
| 9 | ISS y meteoros | `Luz/Eventos.cs`: lluvias de meteoros y eclipses de fechas fijas | las **pasadas de la ISS** (TLE + SGP4) y el aviso de meteoros según luna y nubes |
| 10 | Mapa de fotos | lectura del GPS del archivo (`MetadataExtractor`), `Derivados` (miniaturas WebP) | el **índice** de todas las fotos con GPS y la pantalla de mapa (Leaflet + OpenStreetMap) |
| 11 | Hábitos | nada | módulo nuevo (API + pantalla + Telegram + avisos) |
| 12 | Buscar dentro de documentos | búsqueda por **nombre** en `Archivos` | extraer texto, OCR, índice y búsqueda de texto completo |
| 2 | Cámara bajo demanda | órdenes firmadas al agente, «contraseña otra vez» (`Reautenticar`), cifrado (`Boveda`), modo pánico, reglas de casa | captura (webcam y/o cámara IP), almacén cifrado, pantalla de capturas |

## Orden y por qué

1. **Fase A · rápidas, reutilizan lo que hay**: 4 → 5 → 14 → 13 → 16 (la 16 al final de la fase porque se alimenta de las demás).
2. **Fase B · fotografía**: 9 → 8 → 10 (la 8 usa los eventos de la 9; la 10 comparte «sitios» con la 8).
3. **Fase C · módulo nuevo**: 11 (hábitos). Después se vuelve a la 16 para añadirle los hábitos.
4. **Fase D · piezas pesadas**: 12 (OCR) → 2 (cámara).
5. **Fase E · iPhone**: 17 al final porque `/hoy` junta lo de las demás (presupuesto, hábitos, mejor salida, ISS).

Dependencias duras: 9 → 8 · 11 → 17 · 4/8/11 → 17 · 12 → 16 (buscar en documentos) · 14 reutiliza `GeoIp`.

---

## Fase A · rápidas

### ✅ 4 · Presupuestos con aviso (80 % y 100 %) — `88ed5a3` (el aviso ya existía; se añadió el ritmo y el barrido del vigilante)
- ⬜ A4.1 Función pura `Presupuestos.Estado(gastado, limite, hoy)` → % , días que quedan, euros por día disponibles y si cruzó 80 % o 100 % (tests: justo 80, justo 100, límite 0, mes nuevo, 31 días, categoría sin gasto).
- ⬜ A4.2 `Vigilante.Presupuestos(db)` dentro de `Revisar` (aislada como las otras): por categoría con límite, aviso por Telegram + push «Ocio al 80 %: 64 de 80 €. Quedan 9 días (7 €/día)». Dedupe con `Ajustes` `presupuesto-avisado:{año-mes}:{categoria}:{80|100}`; se limpia solo al cambiar de mes.
- ⬜ A4.3 Aviso inmediato al apuntar un gasto que cruza el umbral (reutiliza `Atajos.TextoPresupuesto`) para no esperar a los 15 min del vigilante.
- ⬜ A4.4 Cartera: barra de cada presupuesto en ámbar a partir del 80 % y roja al pasar; ajuste «avisar al ___ %» (por defecto 80) en la misma pantalla.
- ⬜ A4.5 Tests del vigilante con reloj falso (que no repite, que avisa una vez al cruzar 100 %) + docs.
- Riesgo: ninguno serio. Esfuerzo: 1 sesión.

### ✅ 5 · Previsión de fin de mes mejorada — `9d92919`
- ⬜ A5.1 Leer `Recurrente` y sumar lo **fijo que vence antes de fin de mes** y las **nóminas** esperadas (hoy `Prevision` los ignora, lo dice su comentario). Ampliar `PrevisionCierre` con `FijosPendientes`, `IngresosPendientes` y `Serie` (saldo previsto por día, central y banda).
- ⬜ A5.2 Tests: mes sin fijos (igual que hoy), con un fijo el día 28, con nómina el 30, fijo ya pagado (no se cuenta dos veces), mes de 28 días.
- ⬜ A5.3 Pantalla: gráfica de líneas (ECharts, colores con `resolverColor`) con la banda, y debajo el desglose «saldo hoy − lo variable que sueles gastar − fijos pendientes + nóminas». Si `fiable` es falso, decirlo en claro.
- ⬜ A5.4 Estado «sin datos suficientes» cuidado (hoy son 14 días mínimos) y esqueleto de carga.
- Riesgo: que los fijos mal puestos deformen la previsión → mostrar siempre el desglose. Esfuerzo: 1 sesión.

### ✅ 14 · Accesos nuevos — `8972251` (sin «sospechoso» en el semáforo)
- ⬜ A14.1 Tabla `EquipoConocido` (huella = navegador + sistema + país, nombre, primera y última vez, `Confianza`). Función pura `Huella(agente, geo)` con tests (que cambiar de versión del navegador no cuente como equipo nuevo).
- ⬜ A14.2 En `Sesiones.AlEntrar`: si el equipo es nuevo → aviso inmediato con ciudad y país (de `GeoIp`), IP, hora y el enlace a «Cerrar esa sesión». Ajuste «avisar: todos / solo nuevos / nunca» (hoy es todos o nada), por defecto «solo nuevos».
- ⬜ A14.3 Un acceso desde un **país distinto al habitual** o con **fallos seguidos** se marca como sospechoso (el semáforo lo sube a ámbar).
- ⬜ A14.4 Seguridad: lista de equipos conocidos (renombrar, marcar de confianza, olvidar) e historial de accesos con ciudad, país y resultado. Marcar en el globo de sondas los accesos propios en otro color.
- ⬜ A14.5 Tests con `GeoIp` falso + docs (`07-seguridad.md`).
- Riesgos: las IP de Tailscale Funnel llegan por el proxy (usar `IpCliente.De`, que ya lo resuelve); GeoIP falla con VPN → por eso la huella mezcla equipo y país. Esfuerzo: 1–2 sesiones.

### ✅ 13 · Salud del PC con histórico y avisos — `fbdf14b` (el histórico ya existía; se añadió «se llena en N días»)
- ⬜ A13.1 Tabla `MuestraSalud` (una al día por unidad y la temperatura máxima, el espacio libre y el desgaste), con retención de 400 días. Se rellena al llegar `/api/sistema/agente/salud` (ya cada 5 min; se guarda la primera del día y la peor).
- ⬜ A13.2 Función pura `Tendencia.DiasHastaLleno(muestras)` (regresión lineal sobre 30 días; nulo si no crece). Tests con series creciente, plana, ruidosa y con una limpieza que baja de golpe.
- ⬜ A13.3 Umbrales y avisos en el vigilante: disco >90 % o menos de 20 GB, SMART distinto de «Healthy», CPU >85 °C sostenida (3 lecturas seguidas), desgaste >80 %, reinicio pendiente más de 3 días. Dedupe por clave.
- ⬜ A13.4 Pestaña «Salud» en Mi PC con líneas de espacio y temperatura (ECharts) y «Se llena en ~N días».
- ⬜ A13.5 Docs.
- Riesgos: la temperatura de ACPI no sale en todos los equipos (ya está tratada como `null`); no avisar de lo que no se puede medir. Esfuerzo: 1–2 sesiones.

### ✅ 16 · Buscador que actúa (versión A) — `49b04b3`
- ⬜ A16.1 Vista previa en el propio buscador: si parece una frase («12 café», «recuérdame…»), llamar al intérprete del asistente (el mismo endpoint que ya usa) y mostrar «Gasto · 12 € · Café · Restaurantes — Intro para guardar» sin abrir su diálogo; Mayús+Intro lo abre para retocar.
- ⬜ A16.2 Registro de acciones `core/acciones.ts` (id, texto, atajo, si pide «contraseña otra vez»). Se escriben con `>`: cambiar tema/color/fondo, bloquear la app, bloquear/suspender el PC, activar una escena de casa, abrir Ajustes. Las que mandan órdenes al PC piden `Reautenticar`, como en su pantalla.
- ⬜ A16.3 Búsqueda de contenido en el servidor: `GET /api/buscar?q=` que mira notas, eventos de la agenda y movimientos (con límite de 5 por tipo y orden por relevancia/fecha); resultados agrupados.
- ⬜ A16.4 Últimas acciones usadas (guardadas en este navegador) y accesibilidad (`aria-activedescendant` ya está; comprobar los grupos con `role=group`).
- ⬜ A16.5 Tests del registro de acciones (vitest) y del endpoint (xUnit); docs.
- Riesgo: que el preview tarde → debounce de 250 ms y cancelar la petición anterior. Esfuerzo: 2 sesiones. Las partes de hábitos y documentos se añaden en el punto «16 · versión B».

---

## Fase B · fotografía

### ✅ 9 · ISS y lluvias de meteoros — `0d0a7d2`
- ⬜ B9.1 Servicio `ServicioIss`: baja el TLE de la ISS de CelesTrak (`celestrak.org/NORAD/elements/gp.php?CATNR=25544&FORMAT=tle`, gratis, sin clave), lo guarda en caché 12 h (no se pide más de 1 vez cada 2 h) y, si falla, usa el último guardado (y avisa de su edad).
- ⬜ B9.2 Propagación SGP4: decidir entre el paquete NuGet `SGPdotNET` (MIT) o escribirla; elegir tras ver que compila con .NET 10 y pasar el test de referencia (un TLE fijo con posición conocida a una hora dada).
- ⬜ B9.3 Función pura `Pasadas.Calcular(tle, lugar, desde, dias)`: visible solo si el Sol está más de 6° bajo el horizonte, la ISS está al sol (no en la sombra de la Tierra) y sube de 20°. Devuelve inicio, máximo, fin, altura máxima y dirección. Tests con el TLE fijo (que no salga ninguna a mediodía, que el máximo caiga entre inicio y fin).
- ⬜ B9.4 `GET /api/luz/iss` (10 días) y fusión con `Eventos`. Meteoros: calcular la **luna** de la noche del máximo y marcar «buena noche» si ilumina <40 % (usa `Astro`).
- ⬜ B9.5 Vigilante: push + Telegram 30 min antes de una pasada con altura ≥ 40° y nubes <40 % (previsión de `Tiempo`), y el día del máximo de una lluvia de meteoros con luna baja. Dedupe por clave.
- ⬜ B9.6 Pantalla: tarjeta «ISS esta semana» en Fotografía → Eventos, con un mini arco del cielo (SVG) y la hora.
- Riesgos: la sombra de la Tierra es la parte fina (si sale mal se verían pasadas invisibles) → tests con una pasada conocida; CelesTrak limita peticiones por abuso (por eso la caché). Esfuerzo: 2 sesiones.

### ✅ 8 · Planificador de salidas — `1c81032` (nota por punto, avisos de luz y diario ya existían; sin calibración aplicada)
- ⬜ B8.1 Revisar qué da `Tiempo`: añadir a la petición de Open-Meteo las nubes **bajas, medias y altas**, visibilidad y viento en altura si faltan (gratis, mismos endpoints).
- ⬜ B8.2 Función pura `Plan.Puntuar(tipo, punto, luz, tiempo, luna, aurora, niebla)` → 0–100 con la razón por criterio. Amanecer/atardecer: nubes altas y medias entre 30–70 %, bajas <30 %, sin lluvia, el sol cae hacia donde mira el punto (`Orientacion`). Noche: nubes <20 %, luna baja, sin niebla, aurora si Kp ≥ umbral para su latitud. Pesos en una tabla, no repartidos por el código.
- ⬜ B8.3 Tests de tabla con casos de verdad (cielo cubierto = bajo, nubes altas en atardecer = alto, noche con luna llena = bajo, orientación opuesta penaliza).
- ⬜ B8.4 Calibración: usar `Puntos.Calibrar` (diferencia entre lo previsto y lo que anotaste en el diario) para ajustar un sesgo por tipo; mostrarlo («sueles puntuar 8 más de lo real»).
- ⬜ B8.5 `GET /api/luz/plan?dias=7`: las 5 mejores salidas con hora, sitio, nota y por qué. Se calcula por punto y tipo; resultados en caché 30 min.
- ⬜ B8.6 Pantalla: «Mejores salidas de la semana» en Fotografía (tarjetas con la puntuación en un anillo, razones en una línea, botón «Avisarme»).
- ⬜ B8.7 Vigilante: la víspera a las 20:00 (o las 7 h antes si es amanecer) si hay una salida ≥ 80 y la marcaste. Dedupe.
- Riesgo: es una **heurística**, no una verdad → se dice en la pantalla y se calibra con tu diario. Esfuerzo: 2–3 sesiones.

### ✅ 10 · Mapa de fotos — `3e2733c` + front
- ⬜ B10.1 Tabla `FotoGeo` (raíz, ruta, tamaño y fecha del archivo, fecha de captura EXIF, lat, lon, ancho, alto). Servicio de fondo `IndicadorFotos` que recorre solo lo nuevo o modificado (compara tamaño y fecha, como `Pregenerar`), lee el EXIF con `MetadataExtractor` (ya en el proyecto), con prioridad baja y de 1 en 1.
- ⬜ B10.2 `GET /api/galeria/mapa?anio=&caja=` devolviendo puntos agrupados por celdas según el zoom (agrupar en el servidor evita mandar miles de puntos). `GET /api/galeria/mapa/celda` con las miniaturas de esa zona (usa `Derivados`).
- ⬜ B10.3 **CSP**: `Seguridad.cs` solo permite imágenes de `'self'`, `data:`, `blob:` y Spotify. Añadir `https://tile.openstreetmap.org` (y comprobar `connect-src`) y probar que lo demás sigue bloqueado.
- ⬜ B10.4 Front: `npm i leaflet leaflet.markercluster` (+ tipos), en un componente cargado con `import()` (para no engordar el inicio). Teselas de OpenStreetMap con su **atribución visible**, `maxZoom` 18, sin precarga masiva (política de uso de OSM). Marcadores con la paleta de la app, panel con la tira de miniaturas al tocar, filtro por año.
- ⬜ B10.5 Privacidad: este mapa es **solo privado** (el globo del portfolio público sigue siendo otro, redondeado a 10 km y con zona excluida alrededor de casa). Tests de que `/api/galeria/mapa` exige sesión.
- ⬜ B10.6 Tests: lectura de GPS de un JPEG de ejemplo (con y sin GPS), agrupación por celdas; docs.
- Riesgos: indexar una carpeta enorme la primera vez (limitar a N por pasada y dejar progreso visible); OSM puede cortar el uso abusivo → si molesta, cambiar la URL de teselas en un ajuste. Esfuerzo: 2–3 sesiones.

---

## Fase C · módulo nuevo

### ✅ 11 · Hábitos y rachas — API `d656824` + pantalla (sin frases del asistente: Telegram y buscador)
- ⬜ C11.1 Modelo: `Habito` (nombre, icono, tono, frecuencia: cada día / ciertos días de la semana / N veces por semana, objetivo diario —1 por defecto, 8 para «agua»—, hora de aviso opcional, archivado) y `RegistroHabito` (hábito, fecha, cantidad), único por hábito y día. Migración + índices.
- ⬜ C11.2 Lógica pura `Rachas`: racha actual y mejor, % de cumplimiento a 30 días y mapa de 90 días, **solo contando los días que tocaban**. Una «excusa» opcional por semana que no rompe la racha. Tests: día sin tocar, racha cruzando el cambio de mes, cambio de hora, hábito creado a medias.
- ⬜ C11.3 Rutas `/api/habitos` (CRUD, `PUT /{id}/marcar` con fecha y cantidad, `GET /resumen`). Fechas en la zona horaria de casa (`Zona`).
- ⬜ C11.4 Pantalla `/habitos` (registrada en `core/modulos.ts`, con pestaña en el móvil si cabe): lista de hoy con interruptor `app-interruptor` o contador, racha con número animado (`app-numero`), mapa de calor de 90 días con ECharts, crear/editar con diálogo. Esqueleto y estados vacío y de error de `ui/`.
- ⬜ C11.5 Tarjeta en Inicio: «Hoy 3/5 · racha más larga 12».
- ⬜ C11.6 Telegram: `/hecho meditar` y botones en el aviso; asistente: «hecho meditar», «bebí 2 vasos» (se añade al intérprete con tests en `AsistenteTests`).
- ⬜ C11.7 Vigilante: a la hora de aviso, si no está hecho y la racha ≥ 3, «tu racha de 12 días está en juego». Máximo 1 aviso por hábito y día.
- ⬜ C11.8 Docs (`02-backend`, `03-api`, `04-frontend`) y entrada en `modulos.ts`.
- Riesgo: crecer demasiado → mantenerlo pequeño (sin categorías ni sociales). Esfuerzo: 3 sesiones.

### 🔄 16 · Buscador, versión B (hábitos hechos; documentos cuando exista el 12)
- ⬜ C16.1 Acciones «marcar hábito» y «abrir hábito» en el registro de acciones; resultados de hábitos en `/api/buscar`.
- ⬜ C16.2 Cuando exista el 12: resultados de documentos con el fragmento encontrado.

---

## Fase D · piezas pesadas

### ⬜ 12 · Buscar dentro de documentos (OCR local)
- ⬜ D12.1 Decisión de herramientas, todas gratis: texto plano y código (se lee), Office (`DocumentFormat.OpenXml`, MIT), PDF con texto (`PdfPig`, Apache-2.0), imágenes y PDF escaneados (**Tesseract** con idiomas `spa` y `eng`; los PDF escaneados se pasan a imagen con `pdftoppm` de poppler). Todo se ejecuta **en tu PC**, nada sale a Internet.
- ⬜ D12.2 `Dockerfile`: instalar `tesseract-ocr tesseract-ocr-spa tesseract-ocr-eng poppler-utils` en la imagen final (unos 100–150 MB más) y comprobar que funciona con el usuario sin privilegios. Ajuste `Indexado:Ocr` = `auto` | `off` (en la versión de escritorio nativa, sin Tesseract, el OCR queda en `off` y el resto sigue funcionando; empaquetar Tesseract en el instalador nativo es una **fase aparte** a decidir con Mario).
- ⬜ D12.3 Tabla `TextoArchivo` (raíz, ruta, tamaño, fecha, origen `texto|pdf|ocr`, estado, texto). En PostgreSQL con índice GIN sobre `to_tsvector('spanish', texto)`. Interfaz `IBuscadorTexto` con implementación PostgreSQL y otra de respaldo (`LIKE`) para los tests en memoria.
- ⬜ D12.4 Servicio de fondo `IndiceDocumentos`: recorre `Carpetas` (comparando fecha y tamaño; en Docker sobre Windows no hay eventos de archivos, por eso sondea cada 30 min y al subir algo), de 1 en 1, con límites (25 MB, 20 páginas de OCR por archivo, 60 s por archivo, 1 proceso a la vez, prioridad baja) y registro de lo que falla sin repetirlo.
- ⬜ D12.5 **Respetar la privacidad que ya hay**: no indexar la papelera ni nada que `Carpetas` trate como protegido (PIN); comprobar en `Carpetas.cs` y en los tests de `ArchivosTests`. Los textos indexados se tratan como datos sensibles (no salen en registros).
- ⬜ D12.6 `GET /api/archivos/buscar-texto?q=` con `ts_headline` (el fragmento con la palabra resaltada), ruta, tipo y fecha. Pantalla de Documentos: la búsqueda existente suma «En el contenido» con fragmentos; indicador «indexando 340 de 1 200».
- ⬜ D12.7 Tests: extracción de texto de un `.docx`, un PDF con texto y una imagen de ejemplo (esta última solo si hay `tesseract`, si no se salta), que respeta las carpetas protegidas, que no reindexa lo que no cambió.
- Riesgos: la **imagen de Docker crece** y la primera indexación consume CPU del PC (prioridad baja y límites); la calidad del OCR depende del escaneo (se dice en la pantalla). Esfuerzo: 3–4 sesiones.

### ⬜ 2 · Cámara bajo demanda
Uso previsto: **tus propios equipos y tu casa**. Va **desactivado por defecto**, no graba de forma continua ni audio, y cada captura queda en el registro.
- ⬜ D2.1 Agente: orden `captura:camara` y `captura:pantalla`. Pantalla con `System.Drawing` (sin dependencias). Webcam con `Windows.Media.Capture.MediaCapture` (hay que cambiar el TFM a `net10.0-windows10.0.19041.0`); **el agente debe correr en la sesión del usuario** (así corre dentro del lanzador; un servicio de Windows no puede abrir la cámara) y Windows tiene que permitir la cámara a las apps de escritorio. Respaldo si falla: `ffmpeg -f dshow` (decidir con Mario si merece empaquetarlo).
- ⬜ D2.2 Segunda fuente: **cámara IP** de casa por su URL de instantánea (HTTP) como un conector más de `Domotica/Drivers.cs`, para la «cámara vieja».
- ⬜ D2.3 Subida: el agente envía un JPEG de máximo 1280 px y ~300 KB por `POST /api/pc/capturas` con `X-Agente-Token`. La API lo **cifra** con AES-256-GCM (subclave HKDF `capturas`, como `Boveda` pero para archivos), lo guarda en un volumen (`/capturas`) y apunta `Captura` (fecha, origen, motivo, tamaño).
- ⬜ D2.4 Disparadores: (1) botón «Foto ahora» en Mi PC, que pide la **contraseña otra vez** (`Reautenticar`); (2) opcional: al activar el pánico (`Panico.cs`); (3) opcional: tras N fallos de acceso («foto del intruso») si el PC está despierto; (4) opcional: acción de una regla de casa (`MotorReglas`).
- ⬜ D2.5 Pantalla «Capturas» (en Mi PC): rejilla con fecha, origen y motivo, ampliar, borrar, borrado automático a los 30 días (ajustable). Nunca se mandan por Telegram ni correo salvo que lo actives (subiría la imagen a un tercero).
- ⬜ D2.6 Aviso y rastro: cada captura aparece en la «línea de vida» (`Linea.cs`) y manda push al propio móvil («Se tomó una foto desde la cámara del PC»).
- ⬜ D2.7 Tests: cifrado y descifrado ida y vuelta, que sin «contraseña otra vez» la orden se rechaza, retención, tamaño máximo; probar la captura real en el PC de Mario (no se puede automatizar).
- Riesgos: **depende del hardware** (cámara disponible, permisos de Windows, PC despierto); lo más delicado es la privacidad de lo que se guarda → cifrado, retención corta y todo opcional. Esfuerzo: 3–4 sesiones.

---

## Fase E · iPhone

### ⬜ 17 · Hoy en el iPhone (lo que sí se puede)
Lo que **no** se puede: un widget propio en la pantalla de inicio desde una PWA (iOS no lo permite) ni los accesos directos del manifest en iOS (en Android sí). Lo que sí, gratis:
- ⬜ E17.1 `GET /api/atajos/hoy` (alcance `lectura`): gasto de hoy, presupuestos cerca del límite, próximo evento, hábitos pendientes, mejor salida de la semana y la pasada de la ISS, en JSON corto.
- ⬜ E17.2 Vista `/hoy` en la app: una sola pantalla pensada para abrir y mirar (esqueleto, sin scroll en un iPhone, actualiza al volver a la app). Es el `start_url` opcional de la PWA instalada.
- ⬜ E17.3 **Widget con Scriptable** (app gratuita): script `herramientas/ios/hoy-widget.js` que llama a `/api/atajos/hoy` con un token de Atajos y pinta un widget pequeño y mediano. Documentar cómo instalarlo (crear el token en Seguridad, pegar el script, añadir el widget).
- ⬜ E17.4 **Globo en el icono** (Badging API): el service worker pone el número de cosas pendientes (hábitos de hoy sin hacer, avisos sin leer) al recibir un push; requiere app instalada y permiso de notificaciones (iOS 16.4+). Comprobar con el móvil real.
- ⬜ E17.5 Manifest: accesos directos nuevos (Hoy, Hábitos, Nota rápida), que funcionan en Android y se ignoran en iOS sin romper nada.
- ⬜ E17.6 Tests del endpoint (alcance y contenido), docs y una guía corta en `05-integraciones.md`.
- Riesgo: lo que depende de iOS (Scriptable, Badging) solo se confirma en el móvil de Mario. Esfuerzo: 2 sesiones.

---

## Decisiones para Mario (con lo que haré si no dice otra cosa)

| Tema | Por defecto |
|---|---|
| Cámara: ¿webcam, cámara IP o las dos? | primero captura de pantalla + webcam del PC (con MediaCapture); la IP después |
| Cámara: ¿«foto del intruso» tras fallos de acceso? | desactivado; se puede activar en Seguridad |
| OCR: ¿empaquetar Tesseract también en la versión de escritorio nativa? | no ahora: en nativo, OCR `off` hasta decidirlo |
| Mapa: teselas de OpenStreetMap | sí, con atribución; si molesta, cambiar de proveedor |
| Avisos de accesos | «solo equipos nuevos» en lugar de «todos» |
| ¿Orden de la lista? | la de arriba; se puede cambiar sin romper nada salvo las dependencias duras |
| Desplegar | al terminar cada fase, solo si Mario lo pide |

## Lo que queda fuera a propósito

Un widget nativo de iOS (haría falta una app de pago de Apple), grabar vídeo o audio, subir nada a servicios externos (ni imágenes ni documentos), y cualquier dependencia con coste o con clave.
