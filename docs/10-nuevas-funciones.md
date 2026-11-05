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
