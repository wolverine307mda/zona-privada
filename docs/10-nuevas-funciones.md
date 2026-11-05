# Nuevas funciones (Plan 6)

Lo que se añadió en el [plan 6](checklist-plan6.md), con sus rutas y dónde está cada cosa. Todo es gratis y sin claves nuevas.

## Presupuestos: el aviso dice cuánto queda

- Ya avisaba al pasar del 80 % y del 100 % al **apuntar** un gasto (`Cartera.ComprobarPresupuesto`).
- Ahora el texto añade el ritmo: «Te quedan 16,00 € para 9 días: unos 1,78 € al día» (`Cartera.Ritmo`, función pura), y solo en el mes en curso.
- El `Vigilante` barre cada 15 minutos todos los presupuestos (`Cartera.ComprobarTodos`) para que también avisen los gastos que no pasan por la API (fijos que se apuntan solos, importaciones). Sigue siendo una vez por nivel y mes (`Presupuesto.AvisadoNivel`).

## Previsión de fin de mes con fijos y nóminas

- `Gastos/Prevision.cs`: saldo de hoy − lo variable que sueles gastar (media de 60 días) × días que quedan − **fijos sin apuntar** + **nóminas sin apuntar** (`Calculos.PendientesDe`, los mismos que enseña «Fijos pendientes»; los cubiertos, aunque estén fechados en otro mes, no cuentan dos veces).
- `GET /api/gastos/cartera/prevision` ahora devuelve también `fijosPendientes`, `ingresosPendientes` y `serie` (saldo previsto día a día con peor y mejor caso).
- Un fijo cuyo día ya pasó y sigue sin apunte se cuenta **hoy**.
- Front: `gastos/grafica-prevision.ts` (curva con ECharts) y el desglose bajo «Cerrarás el mes con».

## Accesos nuevos

- `Seguridad/Equipos.cs`: tabla `equipos_conocidos` (huella = sistema + navegador + país, sin versiones). Al entrar, si el equipo no estaba, se avisa («equipo nuevo»), y si además el país es nuevo, el asunto lo dice («país nuevo»).
- La **primera vez** que se usa (tabla vacía) aprende en silencio: tus equipos de siempre no avisan.
- Ajuste «Solo si es un equipo o un país nuevo» (`avisar-solo-nuevos`, activo por defecto). Con él apagado, avisa de todos los inicios como antes. Solo avisa si «Avisar cuando alguien entre» está encendido.
- Rutas: `GET /api/seguridad/equipos`, `PUT /api/seguridad/equipos/{id}/confianza`, `DELETE /api/seguridad/equipos/{id}` (olvidarlo: la próxima entrada vuelve a avisar). `GET /api/seguridad/accesos` suma `lugar` («Madrid, ES») con `GeoIp`. `PUT /api/seguridad/ajustes` acepta `soloNuevos`.
- Front: `seguridad/equipos.ts` («Equipos conocidos», en Vigilancia) y el interruptor en Cuenta.
- Límite: la geolocalización es la de DB-IP Lite (aproximada); con VPN el país puede ser otro, por eso la huella mezcla equipo y país.

## Salud del PC: «se llena en N días»

- Ya existía el histórico diario (`SaludDia`, 90 días), las reglas y los avisos. `Pc/TendenciaDisco.cs` añade una recta de mínimos cuadrados sobre el espacio libre de los últimos 30 días (7 muestras mínimo y 7 días de recorrido; menos de 1 MB/día es ruido; más de 10 años se ignora).
- Regla nueva `disco-llenando:{unidad}` (🟡) cuando quedan 30 días o menos y el disco aún no está casi lleno (si lo está, manda la regla de siempre). Aviso de «resuelto» incluido.
- `GET /api/pc/llenado` → `[{ nombre, dias }]` de los discos que se están llenando. Front: nota «Se llenará en ~N días» en la tendencia de cada disco (Mi PC).

## Buscador (Ctrl K) que actúa

- **Vista previa**: una frase que parece un apunte («12 café») se interpreta con `POST /api/asistente/entender` (la misma que el asistente) y la primera opción dice lo entendido. **Intro** la guarda (gasto o evento) con «Deshacer»; **Mayús+Intro** abre el asistente para retocarla. Con dudas o preguntas se comporta como antes.
- **Acciones** con `>` (o por su nombre al buscar): color, fondo, modo día/noche/sistema, bloquear/suspender el PC, mantener despierto y escenas del PC. Las órdenes al PC piden «confirmar que eres tú» (403 `reautenticar`) como en Mi PC. Registro en `shell/acciones.ts` (con tests).
- **Contenido**: `GET /api/buscar?q=` busca en eventos de la agenda (título y lugar) y movimientos (concepto): cinco de cada, los más recientes primero, mínimo 2 letras. Las notas siguen en `/api/notas?q=` (van cifradas).

## ISS y lluvias de meteoros

- `Luz/Iss.cs`: `ServicioIss` baja el TLE de la ISS de CelesTrak (`celestrak.org/NORAD/elements/gp.php?CATNR=25544&FORMAT=tle`, gratis, sin clave; se guarda 12 h, no se pide más de una vez cada 2 h y, si falla, vale el último bueno). `Pasadas.Calcular` propaga la órbita con **SGP.NET** (SGP4, licencia MIT) cada 20 s y se queda con los tramos en que la estación está a más de 10° sobre el horizonte, el Sol a más de 6° bajo el horizonte (`Astro.AlturaSol`) y la estación **iluminada** (sombra cilíndrica de la Tierra, `Pasadas.Iluminado`).
- `GET /api/luz/iss?dias=10` (1–14) → pasadas con inicio, máximo, fin, altura, azimuts, rumbos y las nubes previstas; `tleEdadDias` avisa de TLE viejos. Front: `fotografia/iss.ts` (Previsiones) con un mapa del cielo SVG por pasada.
- `Vigilante.Cielo` (cada 15 min, de 9 a 23 h): avisa por push y Telegram de una pasada **buena** (sube de 40°) 45 min antes, o la tarde-noche anterior si es de madrugada (`Pasadas.ParaAvisar`, con prueba), siempre que las nubes previstas no pasen del 40 %. Clave `iss-avisada` en `Ajustes`.
- Meteoros: las lluvias y su luna ya salían en `/api/luz/eventos`; ahora, por la tarde de la noche del máximo, si la luna es <40 % y las nubes ≤50 %, llega el aviso (clave `meteoros-avisado:{lluvia}:{año}`).

## Planificador de salidas

- Ya existían la nota de cada ventana (`/api/luz/semana`, nubes y luna) y «Hoy toca este». `Luz/PlanSalidas.cs` suma el **ranking de la semana entre todos tus sitios**: la nota se ajusta ±1 según hacia dónde mira el sitio respecto al Sol (o al núcleo de la Vía Láctea en las noches); como mucho una salida por sitio y día. Sin sitios guardados, usa tu lugar de siempre.
- `GET /api/luz/puntos/plan` → las 6 mejores. Front: `fotografia/plan-salidas.ts` (arriba de «Mis sitios»). La calibración con el diario de salidas sigue enseñándose en el diario, no se aplica a la nota.
- Es una heurística: se dice en la pantalla.

## Mapa de fotos

- `Galeria/MapaFotos.cs`: tabla `fotos_geo` (ruta, tamaño, fecha del archivo, hash, lat, lon, fecha de captura EXIF). `IndicadorFotos` recorre «Imágenes» 90 s después de arrancar y cada 30 min, solo lo nuevo o cambiado, 300 fotos por pasada (sigue sin esperar si quedan pendientes) y quita lo que ya no está. Se apunta también la foto sin GPS para no volver a leerla.
- `GET /api/galeria/mapa` (sesión + **PIN**, como el resto de «Imágenes»): puntos con ruta, hash, lat, lon y fecha. Es solo privado: el globo del portfolio público sigue siendo otro, redondeado y con zona excluida alrededor de casa.
- Front: pestaña «Mapa» en Galería (`galeria/mapa-fotos.ts`): Leaflet + markercluster cargados solo al abrir la pestaña, teselas de **OpenStreetMap** con su atribución (`https://tile.openstreetmap.org` añadido al `img-src` de la CSP), filtro por año y tira de miniaturas al tocar un punto. De noche las teselas se oscurecen.
- Nuevas dependencias de `web`: `leaflet`, `leaflet.markercluster` (+ sus tipos); `angular.json` las admite como CommonJS.

## Hábitos y rachas

- Módulo nuevo `Habitos/Habitos.cs` y pantalla `/habitos` (menú Vida). Tablas `habitos` y `registros_habito` (única por hábito y día). Un hábito tiene nombre, días de la semana (máscara: lunes = 1 … domingo = 64; 127 = todos), objetivo diario (1 = sí o no; 8 = ocho vasos), unidad, hora de aviso opcional y si está archivado. Máximo 20 activos.
- **Rachas** (`Rachas.Calcular`, función pura con tests): solo cuentan los días que tocaban; hoy sin hacer aún no rompe la racha; un día libre que se hizo suma; los días apuntados antes de crear el hábito adelantan su inicio (para ponerse al día; `ServicioHabitos.Inicio`); la historia llega a 400 días. Devuelve racha actual, mejor racha, cumplimiento de los últimos 30 días que tocaban y un mapa de 91 días (una letra por día: `h` hecho, `p` a medias, `f` fallado, `o` pendiente hoy, `l` libre, `-` anterior al hábito).
- Rutas: `GET /api/habitos?archivados=` · `POST` · `PUT /{id}` (también archivar) · `DELETE /{id}` · `PUT /{id}/marcar` `{ fecha?, cantidad? }` (por defecto hoy y el objetivo entero; 0 quita la marca; no se marca el futuro ni más de un año atrás). Las fechas son las de la zona horaria de casa.
- Avisos: `Vigilante.Habitos` (cada 15 min, de 9 a 23 h): a la hora del hábito, si toca hoy y no está hecho, push + Telegram («Llevas 12 días seguidos: no la pierdas» si la racha es de 3 o más), una vez al día (`Habito.AvisadoFecha`).
- Telegram: `/habitos` (lo de hoy con rachas) y `/hecho meditar` (suma uno o lo completa; pide aclarar si el nombre encaja con varios).
- Front: toque para apuntar (o contador con − y + si el objetivo pasa de 1), racha, mapa de 13 semanas por columnas de lunes a domingo y diálogo de edición. Tarjeta «Hábitos» en Inicio (`3/5 · racha de 12`). En el buscador (Ctrl K): «Hecho: Meditar» para los pendientes de hoy.
- No se hizo (y queda fuera a propósito): interpretar frases del asistente tipo «hecho meditar» o «bebí 2 vasos» (lo cubren Telegram y el buscador), categorías, hábitos compartidos y la «excusa» semanal.

## Buscar dentro de los documentos (y OCR)

- `Archivos/Indice.cs`. Hasta ahora Documentos solo buscaba por **nombre**. Un servicio de fondo (`IndiceDocumentos`, 3 min tras arrancar y cada 30 min, y al subir algo) lee el texto de los archivos de **Documentos, Escritorio y Descargas** (`Indexado:Raices`; fotos, música y vídeo no) y lo guarda en la tabla `textos_archivo` (ruta, tamaño, fecha, origen, estado, texto de hasta 400 000 letras). Solo lee lo nuevo o cambiado, 25 archivos por pasada (sigue sin esperar si quedan), y quita lo que ya no está. La papelera y los archivos ocultos no se indexan.
- **Qué lee** (todo en tu equipo, nada sale a Internet): texto plano y similares (`.txt .md .csv .json .log .xml .html .yml…`), Office (`.docx .xlsx .pptx .odt .ods .odp`: son zips de XML y se leen sin librerías; DTD prohibidos, partes de más de 25 MB ignoradas), PDF con texto (**PdfPig**, Apache-2.0, hasta 200 páginas) e **imágenes y PDF escaneados con Tesseract** (español e inglés; los PDF pasan a imagen con `pdftoppm` de poppler, hasta 20 páginas). Un PDF con menos de 30 letras de texto se trata como escaneado.
- **OCR**: se ejecuta con prioridad baja, de uno en uno y con 60 s como máximo por programa. Sin Tesseract (por ejemplo en la versión de escritorio sin Docker) o con `Indexado__Ocr=off`, las imágenes y los PDF escaneados simplemente no se leen y todo lo demás funciona. El `Dockerfile` instala `tesseract-ocr`, `tesseract-ocr-spa`, `tesseract-ocr-eng` y `poppler-utils` (unos 17 MB de idiomas más los programas). Empaquetar Tesseract en el instalador de escritorio queda como tarea aparte.
- **Búsqueda**: en PostgreSQL, **texto completo en español** (índice GIN `ix_textos_archivo_texto` sobre `to_tsvector('spanish', "Texto")`, creado por la migración): entiende plurales, acentos y conjugaciones («facturas» encuentra «factura»; «batir» encuentra «baten») y ordena por relevancia. En la base en memoria de las pruebas se busca por palabras sueltas. El fragmento con la palabra sale de `Lectores.Fragmento`.
- Rutas (con PIN, como todo Documentos): `GET /api/archivos/buscar-texto?q=&raiz=` (20 resultados: raíz, ruta, nombre, origen, fragmento) y `GET /api/archivos/indice` (indexados, sin texto, pendientes, si trabaja, si hay OCR).
- Front: en Documentos, al buscar aparece «En el contenido» con el fragmento de cada archivo y un aviso mientras el índice sigue leyendo. El buscador (Ctrl K) suma un grupo «Documentos» **solo si el PIN no hace falta o ya está puesto** (si no, abriría el teclado del PIN a mitad de la escritura).
- Ajustes: `Indexado:Activo` (`no` lo apaga), `Indexado:Raices`, `Indexado:Ocr` (`auto`/`off`), `Indexado:Idiomas` (por defecto `spa+eng`; si falta el español, prueba con inglés).
- Verificado: lectores y pasadas con tests (docx, xlsx, pdf generado, zips rotos, DTD, tope por pasada); la búsqueda de texto completo y el índice GIN con un PostgreSQL real; la instalación de Tesseract 5.3 (spa, eng) y poppler en la imagen base de la app. **Sin verificar**: el OCR de un escaneo real en este equipo (no hay Tesseract instalado fuera del contenedor).

## Fotos bajo demanda (pantalla, cámara del PC, cámara IP)

Para **tus propios equipos y tu casa**. Viene **apagado**; no graba vídeo ni audio; cada foto queda apuntada con su origen y su motivo y te llega un aviso (push y Telegram, sin la imagen); se guardan **cifradas** y se borran solas.

- **Dónde**: Mi PC → Controles → «Fotos del PC y de casa» (`pc/capturas.ts`). Interruptor general (apagado por defecto), botones «Captura de pantalla», «Foto de la cámara» y «Cámara IP», rejilla de fotos con visor y «Borrar». Ajustes: foto al activar el **pánico**, foto tras **3 intentos fallidos de entrar en 10 minutos** (como mucho una cada 10 min), días de borrado (30 por defecto), dirección de la cámara IP y «Borrar todas».
- **Confirmación**: encender o cambiar los ajustes, cada «Foto ahora» y «Borrar todas» piden **confirmar que eres tú** (Face ID o contraseña, 403 `reautenticar`), igual que las órdenes delicadas al PC.
- **Almacén**: tabla `capturas_pc` (fecha, origen, motivo, tamaño, ancho, alto, imagen). La imagen se reduce a 1280 px, se recodifica en JPEG (sin metadatos) y se cifra con **AES-256-GCM** y una subclave propia (`Boveda.CifrarBytes`, propósito `capturas`; formato nonce 12 + etiqueta 16 + cifrado). Con una clave maestra distinta no se puede descifrar. Sin volumen nuevo en Docker ni carpeta en el PC.
- **Rutas**: `GET /api/pc/capturas` (ajustes y las 60 últimas), `GET /api/pc/capturas/{id}/imagen` (descifrada, `no-store`), `DELETE /{id}`, `DELETE /` (todas, con confirmación), `PUT /ajustes`, `POST /capturar { origen }`. El agente deja la imagen en `POST /api/sistema/agente/captura?origen=&motivo=` (token compartido, sin pasar por el proxy, máximo 4 MB, solo si las fotos están encendidas).
- **Pantalla y cámara del PC** las hace el agente con una orden firmada nueva (`captura`, parámetro `pantalla|camara` + `:manual|panico|intruso`). La pantalla: `Graphics.CopyFromScreen` de todo el escritorio (System.Drawing). La cámara: **ffmpeg** (gratis: `winget install Gyan.FFmpeg`) por DirectShow, descartando los primeros fotogramas (exposición); `AGENTE_FFMPEG` (ruta) y `AGENTE_CAMARA` (nombre) la afinan. El agente tiene que correr **en tu sesión de Windows** (dentro del lanzador o con `arrancar.cmd`; un servicio no ve la cámara) y Windows tiene que dejar la cámara a las aplicaciones de escritorio. Los archivos nuevos del agente están también en el lanzador (`Captura.cs`, `CapturaLogica.cs`).
- **Cámara IP**: la propia API pide la instantánea (JPEG) a una URL guardada cifrada. **Solo IP literales de la red local** (10.x, 172.16-31.x, 192.168.x, 169.254.x, 127.x, IPv6 local): sin nombres ni IP públicas, para que no sirva para pedir cosas a Internet. Admite `usuario:clave@` (autenticación básica) y corta a 8 s y 4 MB.
- **Pruebas**: cifrado ida y vuelta y detección de manipulación, validación de direcciones (IP públicas, nombres, esquemas raros), tope y borrado por días, disparadores del pánico y de los fallos, la API (sin sesión, sin token, apagado) y la parte pura del agente (`CapturaTests` del lanzador). **Sin probar en tu equipo**: la captura real de la pantalla y de la cámara dependen del hardware y de Windows; la primera vez, prueba «Captura de pantalla» y luego «Foto de la cámara».
- No incluido a propósito: acción de las reglas de casa, envío de la foto por Telegram o correo (subiría la imagen a un tercero) y WinRT `MediaCapture` (obligaba a cambiar el TFM del agente y del lanzador).

## Lo de hoy en el iPhone

- **Lo que no se puede**: un widget propio en la pantalla de inicio desde una PWA (iOS no lo permite); los accesos directos del manifest no funcionan en iOS (sí en Android, y se ignoran sin romper nada).
- `Atajos/Hoy.cs`: `Hoy.Componer` junta en una respuesta el gasto de hoy, los presupuestos al 80 % o más, la agenda de hoy, los hábitos que tocan (hechos y pendientes) y la mejor salida de la semana (`PlanSalidas`; si falla la previsión no se cae el resto), y lo escribe ya en `lineas` (`Hoy.Lineas`, con prueba). Rutas: `GET /api/hoy` (con sesión, para la pantalla) y `GET /api/atajos/hoy` (token de Atajos con permiso **lectura**: uno de solo «gasto» no vale).
- **Pantalla `/hoy`** (`hoy/hoy.ts`, en el buscador como «Hoy»): una columna pensada para el móvil con el gasto, los presupuestos, la agenda, los hábitos pendientes (un toque los apunta) y la salida; se actualiza al volver a la app.
- **Widget con Scriptable** (app gratuita): `herramientas/ios/hoy-widget.js` + guía `herramientas/ios/LEEME.md`. La dirección y el token se guardan en el llavero del iPhone la primera vez (no en el script).
- **Globo en el icono** (Badging API): `sw.js` pone el número de avisos sin leer al llegar un push y `core/globo.ts` lo quita al abrir la app. Requiere app instalada y notificaciones permitidas (iOS 16.4+); **sin probar en un iPhone real**.
- Manifest: accesos directos nuevos (Hoy, Hábitos, Nota nueva).
- Al probar `/habitos` y `/hecho` de Telegram con su armazón de pruebas salió un fallo real (releer el JSON anónimo de los hábitos sin camelCase): ahora `ServicioHabitos` devuelve un registro tipado (`HabitoDto`) y hay prueba de Telegram (`HabitosTelegramTests`).
