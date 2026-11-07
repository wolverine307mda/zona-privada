# Checklist · Plan 7 (30 ideas elegidas por Mario)

Estados: ⬜ pendiente · ⏭️ próximo · 🔄 en proceso · ✅ hecho (con el commit).
Numeración = la de la lista de 90 ideas que se habló con Mario (6 de noviembre de 2026). Aquí están las 30 que pidió:
**1, 5, 6, 8, 9, 12, 13, 17, 19, 39, 40, 41, 43, 46, 49, 58, 59, 60, 67, 68, 69, 70, 74, 75, 76, 77, 79, 81, 85, 89.**

## Reglas de este plan (las del Plan 6, más las nuevas)

- **Gratis y con las menos claves posibles.** Fuentes sin clave: Open-Meteo (ya integrada), TVMaze (series), LRCLIB (letras), el servicio público de precios de carburantes del Ministerio (España), OpenStreetMap
  (ya en el mapa de fotos). Las únicas cosas que piden algo tuyo están en «Lo que necesito de ti» al final.
- **Un commit por idea (o por fase si es grande)**, en español, con `Co-Authored-By`. **Sin `git push` y sin desplegar** hasta que Mario lo diga. Carpeta compartida con otras sesiones: `git add` siempre con rutas explícitas.
- **Orden de despliegue**: primero el entorno de **pruebas** (`compose.test.yaml`, con su propia base de datos), después la **real** (paquete del lanzador). Antes de tocar la real, el lanzador ya hace solo una
  copia «antes de actualizar» (`antes-de-actualizar-*.dump`) si la API cambia; aun así, en cada fase con migraciones se avisa.
- Lógica en **funciones puras con `TimeProvider`** (patrón de `Prevision.cs`, `Calculos`, `EvaluadorReglas`) y tests en `Mda.Api.Tests`; los avisos usan `IAvisos` y se **deduplican con claves en `Ajustes`**.
- Cada idea termina con: tests de la lógica pura, `dotnet test ZonaPrivada.slnx`, `cd web && npx ng build` y `ng test`, prueba en pantalla en escritorio y a 375 px, y su sección en `docs/`.
- Migraciones: `dotnet ef migrations add Nombre --output-dir Data/Migraciones` (no escribirlas a mano: ya dio problemas) y `has-pending-model-changes` limpio. Toda tabla nueva con índice por lo que se consulta.
- **Datos personales de terceros y de salud** (contactos, cumpleaños, peso, entrenos): se guardan **cifrados con `Boveda`** (como las notas), no salen en las «Llamadas externas» (los cuerpos ya se tapan por nombre de campo)
  y las pantallas van detrás del **PIN de Seguridad** (`PinFiltro`) si Mario lo activa para ese módulo.
- Diseño de pantallas: tokens de `styles.css` y componentes de `web/src/app/ui/`; claro y oscuro; `prefers-reduced-motion`; tarjetas con esqueleto de carga y estado de error.

---

## Lo que ya existe (verificado en el código) y lo que falta de verdad

| # | Idea | Ya hay | Falta | Esfuerzo |
|---|---|---|---|---|
| 1 | Previsión de fin de mes con rango | ✅ **Hecha** (Plan 6, `9d92919`): `Gastos/Prevision.cs` (central, bajo y alto, fijos y nóminas pendientes, serie día a día) y `gastos/grafica-prevision.ts` | Nada nuevo; solo una pasada de pulido (leerla en el móvil, explicar «bajo/alto» con una frase) | 0,2 |
| 5 | Gasto por lugar en un mapa | ✅ **Hecha**: `Gastos/Lugares.cs` + `gastos/mapa-gasto.ts` (Leaflet), con filtro por categoría y detrás del PIN | — | 1 |
| 6 | Presupuesto por semana | ✅ **Hecha**: `PresupuestoSemanal` + `PresupuestosSemana` + interruptor en Cartera (queda la línea semanal del atajo `/hoy`) | — | 1 |
| 8 | Fecha de cobro de la nómina | `Recurrente` (incluye nóminas) y la previsión suma las pendientes | **Detectar el día de cobro**, «dinero hasta cobrar» y aviso si no llega | 1 |
| 9 | Garantías y devoluciones | `Papeles` (caducidades con avisos escalonados) | Garantía/devolución **ligada a un gasto**, con foto del ticket | 1,5 |
| 12 | Gasolina: cuándo y dónde repostar | `Repostaje` con precio por litro y consumo (`CocheEndpoints.cs`) | Precios de las gasolineras (Ministerio), la más barata cerca y «conviene hoy» | 1,5 |
| 13 | Contactos: cumpleaños, datos y gustos | nada | **Módulo Personas** + importación selectiva (vCard / Google / Atajo) | 3 |
| 17 | Planificador de comidas | `Compra` (lista con memoria y «ya toca») | **Recetas + menú semanal + lista de la compra automática** | 3 |
| 19 | Salud y ejercicio con un plan | (la «Salud» que hay es la del PC) | **Módulo Bienestar**: peso y medidas, entrenos, plan guiado, ayuda | 4 |
| 39 | Modo «me voy a dormir» | `Contexto` (llego/salgo/hora), escenas de casa y de PC | Evento `duermo`, comprobaciones y mensaje al Atajo | 1,5 |
| 40 | Modo «salgo de casa» | evento `salgo` (apaga el PC y lo bloquea) | **Lista de comprobación** (ventanas, estufas, aparatos encendidos) | 1 |
| 41 | Historial de temperatura y humedad | `LecturaCasa` (30 días) y `/aparatos/{id}/historial` + gráfica **por aparato** | Vista **por estancia**, semana/mes, mín/media/máx y avisos de moho/frío | 1,5 |
| 43 | Aviso de puerta o aparato abierto | `ReglaCasa` con «durante N minutos» (solo números) y `SoloAvisar` | Que el motor entienda **estados** (abierto/encendido) y avise con botón de apagar | 1 |
| 46 | Ropa tendida y lluvia | `Tiempo` (Open-Meteo) y avisos de víspera | Marca «ropa tendida», aviso **minutos antes** de la lluvia | 1 |
| 49 | Mapa de dispositivos de confianza | `Equipo` con `Confianza`, aviso de equipo/país nuevo, `GeoIp` | «**No lo reconozco**» (cierra sesiones y avisa) y **mapa** con «última vez visto» | 1,5 |
| 58 | Salud del disco y cuándo se llena | ✅ en gran parte: `TendenciaDisco.cs` (días hasta llenarse), aviso de SMART ≠ Healthy | Atributos SMART útiles, aviso proactivo a N días y «qué ocupa» | 1 |
| 59 | Bandeja bidireccional | `Pc/Bandeja.cs` (PC → app) y orden `descargar` | **App → PC** (subir desde el móvil) y **PC → móvil** con enlace que caduca | 2 |
| 60 | Abrir y cerrar aplicaciones | `agente/Abrir.cs` (lista cerrada: calculadora, bloc, explorador) | **Catálogo de apps elegidas** (Claude…), y **cerrar** las que más gastan | 2 |
| 67 | Temas de color completos | 8 colores de acento + claro/oscuro/sistema (`tema.service.ts`) | **Temas completos** (papel, terminal, sepia, alto contraste, OLED…) | 2 |
| 68 | Inicio personalizable | `panel.html` fijo; patrón de guardado en servidor (`PanelCasa`) | Registro de tarjetas, **arrastrar/ocultar/tamaño** y varias vistas | 3 |
| 69 | Widget en la pantalla de bloqueo | `/api/atajos/hoy` y `herramientas/ios/hoy-widget.js` (Scriptable) | Versión **de pantalla de bloqueo** (accesorios) y contenido nuevo | 1 |
| 70 | Tipografía y densidad | nada | Tamaño de letra, densidad y fuente legible **por dispositivo** | 1 |
| 74 | Fondos que reaccionan | 33 fondos con intensidad, velocidad y fuerza | Señales vivas (música, carga del PC, hora) que los muevan | 1,5 |
| 75 | Letras sincronizadas | reproductor de escritorio (`shell/mini-musica.ts`) | Letras de LRCLIB, resaltado por tiempo y caché | 1,5 |
| 76 | Historial de lo que escuchas | `/api/musica/recientes` y `/top` (Spotify) | **Historial propio persistente** (Spotify solo da 50) y estadísticas | 2 |
| 77 | Listas por estado de ánimo | control de reproducción (`/control/{accion}`) | Listas asignadas a «momentos» y un toque para ponerlas | 1,5 |
| 79 | Series y películas | nada | Módulo con estados, avisos de estreno y «dónde verlo» | 2,5 |
| 81 | Constructor visual de reglas | `Automatizaciones` (formulario), `ReglaCasa`, `ReglaContexto` | **Constructor por bloques** unificado y simulación | 3 |
| 85 | Panel de salud de la app | `Avisos/Vigilancia.cs` (última vez de cada comprobación) y 5xx | Tamaño de la base, tablas que crecen, copias y **acciones de limpieza** | 1,5 |
| 89 | Pantalla de bienvenida personal | saludo por hora en el héroe de Inicio | Frases según estación, cumpleaños, lo de ayer… | 1 |

Esfuerzo en «sesiones» (una sesión = una tanda larga de trabajo con pruebas). Suma aproximada: **~50 sesiones**.

---

## Decisiones transversales (valen para todo el plan)

1. **Módulos nuevos** (cada uno con su carpeta, su pantalla, su sección en `docs/` y su fila en la tabla de `CLAUDE.md`):
   `Personas` (13), `Cocina` (17), `Bienestar` (19), `Series` (79), `Garantias` (9, dentro de `Gastos`) y `Sistema/SaludApp` (85). El resto **extiende** módulos que ya existen.
2. **Estados de aparatos** (43, 39, 40): hoy el motor solo mira números. Se añade un tipo de condición «estado» (`abierto`, `encendido`) que mira el valor de la entidad y el tiempo que lleva así.
   Es la base de 43, 40 y 81.
3. **Eventos de contexto** (39, 40, 46): se amplía `ReglaContexto.Evento` con `duermo`, `despierto` y `salgo` con comprobaciones; el iPhone los dispara con una automatización de Atajos (el patrón de `/api/atajos/casa`).
   Nuevo endpoint `POST /api/atajos/modo` (permiso `casa`) que devuelve `{mensaje}` como todos los atajos.
4. **Atajos nuevos** (13, 19, 46, 69): permisos `personas` y `salud` (además de `casa` y `lectura`), cada uno con su receta en `docs/12-siri-y-atajos.md` y texto listo para pegar.
5. **Telegram**: comandos nuevos solo donde aportan (`/cumples`, `/ropa`, `/agua`, `/entreno`, `/cerrar`). Todos en `Telegram/Conversacion.cs`, con el mismo estilo que `/tarea` y `/compra`.
6. **Interfaz**: gráficas con ECharts y colores con `resolverColor`; mapas con Leaflet (ya con la corrección del Referer de OSM); arrastrar con `@angular/cdk/drag-drop` (única dependencia nueva, oficial de Angular).
7. **Sin IA externa**: las «sugerencias» (regalos, menús, planes de ejercicio, bienvenida) son reglas y plantillas propias, deterministas (semilla por fecha) y comprobables con tests.
8. **Privacidad de ubicación**: coordenadas siempre redondeadas (~100 m, como `Lugares.Redondear`) y los mapas de lo que gastas o de dónde estás van detrás del PIN.

---

## Orden y por qué

1. **Fase A · Dinero** (rápidas, reutilizan lo que hay): 5 → 6 → 8 → 12 → 9 → (1 solo pulido).
2. **Fase B · Casa y rutinas**: 43 (motor de estados) → 41 → 40 → 39 → 46. La 43 va primera porque 40 y 39 la usan.
3. **Fase C · PC y seguridad**: 58 → 49 → 60 → 59 → 85.
4. **Fase D · Música**: 76 → 75 → 77 → 74 (la 74 usa la portada y lo que suena de las otras).
5. **Fase E · Módulos de vida**: 13 (Personas) → 17 (Cocina) → 79 (Series) → 19 (Bienestar, la más grande, al final para hacerla con calma).
6. **Fase F · Interfaz**: 67 → 70 → 68 → 69 → 89 → 81. La 68 va después de los módulos para que sus tarjetas ya existan; la 89 usa cumpleaños (13) y bienestar (19); la 81 cierra porque junta reglas de casa, de contexto y automatizaciones con todos los tipos nuevos.

Dependencias duras: 43 → 40 → 39 · 43 → 81 · 13 → 89 · 19 → 89 · 17 → `Compra` · 13/17/19/79 → 68 (tarjetas de Inicio) · todas las anteriores → 69 (contenido del widget).

---

## Fase A · Dinero

### ✅ 5 · Mapa de lo que gastas
- ✅ A5.1 Se amplió la ruta que ya había, `GET /api/gastos/lugares?anio&mes&categoria` (sin ruta nueva): devuelve las zonas de la categoría pedida y las `categorias` del periodo (de más a menos gasto) para los filtros. Tests de `Categorias` y `DeCategoria`.
- ✅ A5.2 `gastos/mapa-gasto.ts`: mapa real de Leaflet con OpenStreetMap (teselas con `referrerPolicy: 'origin'`, modo oscuro invertido), un círculo por zona que crece con el gasto y ventana al pulsar con los comercios (hecha con elementos, no con HTML pegado). Sustituye al dibujo esquemático en SVG.
- ✅ A5.3 Detrás del PIN (`PinFiltro`): sin PIN configurado no pide nada; con PIN, el teclado sale solo al abrir Estadísticas (el interceptor ya repite la petición).
- ✅ A5.4 Filtros de mes/año y de categoría; estado vacío con la explicación del atajo; probado en pantalla con datos de ejemplo.
- Decisión: se mantiene el círculo por zona de ~1 km (no se ve el portal) y no se añadió `leaflet.heat`.

### ✅ 6 · Presupuesto por semana
- ✅ A6.1 Tabla propia `presupuestos_semanales` (`PresupuestoSemanal`: categoría, límite, `Arrastrar`, semana y nivel ya avisados) con migración generada por `dotnet ef`. El presupuesto mensual no se toca. La semana va de lunes a domingo.
- ✅ A6.2 Lógica pura en `Gastos/PresupuestoSemanal.cs` (`Semana`, `PresupuestosSemana.Estado/Nivel/Arrastre`): porcentaje, días que quedan contando hoy, euros por día y arrastre de lo que sobró la semana pasada con tope de la mitad del límite. Tests: lunes/domingo, año que cruza, 80/100 %, arrastre.
- ✅ A6.3 Aviso al 80 y 100 % por push y Telegram (sin correo, para no llenar el buzón) con dedupe por semana en la propia fila; se comprueba al apuntar un gasto y en el barrido del vigilante (`Cartera.ComprobarTodos`). Un gasto de otra semana no avisa de la de hoy. Tests con reloj fijo y un `IAvisos` falso.
- ✅ A6.4 Cartera: interruptor «Mes · Semana» en la tarjeta (solo mirando el mes actual) y pestañas «Al mes / A la semana» en el diálogo, con la casilla «sumar lo que sobre». Rutas: `GET /api/gastos/presupuestos-semana`, `PUT` y `DELETE /api/gastos/presupuestos-semana/{categoria}`.
- ⬜ A6.5 Pendiente (poco valor): la línea semanal en `/api/atajos/hoy` (el atajo sigue hablando del mes).

### ⬜ 8 · Día de cobro de la nómina
- ⬜ A8.1 Función pura `DiaDeCobro.Detectar(ingresos)`: toma los ingresos grandes de los últimos 6 meses (≥ 60 % del ingreso mensual medio o marcados como nómina en `Recurrente`) y devuelve el día habitual (mediana) y su fiabilidad; si el día cae en fin de semana, anota «se adelanta al viernes». Ajuste manual que manda sobre la detección. Tests: 3 meses regulares, día variable, un mes sin nómina, mes de 28 días.
- ⬜ A8.2 «Dinero hasta cobrar»: `Consejos` hoy divide hasta fin de mes; se añade el **horizonte = próximo cobro** (si se conoce) con «te quedan 340 € para 9 días: 37 €/día».
- ⬜ A8.3 Aviso «hoy es día de cobro y aún no hay ingreso apuntado» (a las 20:00, una sola vez) y, si llega un ingreso grande, «nómina recibida: toca apartar a tus huchas» con botón.
- ⬜ A8.4 Pantalla: ajuste «Mi día de cobro» (auto/manual) en Cartera y la cuenta atrás en la tarjeta de dinero de Inicio.
- Riesgos: falsos positivos con ingresos puntuales → pide al menos 3 meses y deja cambiarlo. Esfuerzo: 1 sesión.

### ⬜ 12 · Gasolina: cuándo y dónde repostar
- ⬜ A12.1 `ServicioCarburantes`: descarga diaria (con caché de 12 h en memoria) del servicio público del Ministerio (`PreciosCarburantes/EstacionesTerrestres`), sin clave. Parser puro con tests sobre una muestra guardada (cabeceras con comas decimales y tildes).
- ⬜ A12.2 Ajustes del coche: combustible (Gasolina 95 / Diésel A…), radio (km) y punto de referencia (casa, que ya se sabe por `Luz`/tiempo, o el que elijas). Función pura `Carburantes.MasBaratas(estaciones, punto, radio, combustible)`.
- ⬜ A12.3 «¿Conviene repostar hoy?»: compara el precio más barato cercano con la **media de tu provincia de la semana** y con tu propio histórico de `Repostaje.PrecioLitro`; veredicto con las tres cifras (nunca un «sí» sin explicarse). Tests con casos de subida, bajada y pocos datos.
- ⬜ A12.4 Pantalla «Gasolina» (en Coche): las 5 más baratas con distancia y precio, tu último repostaje, el ahorro posible por depósito (litros típicos × diferencia) y un enlace a Maps. Móvil a 375 px.
- ⬜ A12.5 Aviso opcional («Diésel bajó 6 céntimos cerca de ti») con dedupe diario y umbral configurable.
- Riesgos: el servicio del Ministerio a veces tarda o cae → caché y mensaje «datos de hace X horas». Esfuerzo: 1,5 sesiones.

### ⬜ 9 · Garantías y devoluciones
- ⬜ A9.1 Tabla `Garantia` (`MovimientoId?`, `Producto`, `Comercio`, `FechaCompra`, `GarantiaHasta`, `DevolucionHasta?`, `Estado`: vigente · devuelta · reclamada · caducada, `Notas`, `ArchivoRuta?` del ticket o la foto). Por defecto, 3 años de garantía legal (artículo 120 de la Ley General para la Defensa de los Consumidores, para bienes nuevos) y 14 días de desistimiento en compras a distancia; ambos editables.
- ⬜ A9.2 Función pura `Garantias.Estado(g, hoy)` y los **avisos escalonados** reutilizando la lógica de `Papeles` (60/30/7 días antes de la garantía; 3 días antes de que acabe la devolución). Tests con el reloj falso.
- ⬜ A9.3 Entrada fácil: en cada movimiento ≥ 50 € (umbral ajustable) un botón «Guardar garantía» (rellena producto, comercio y fecha); en Telegram, tras un gasto grande, un botón «¿Guardo garantía?»; y subir la foto del ticket (se guarda cifrada en `Archivos`).
- ⬜ A9.4 Pantalla «Garantías» (en Papeles o Cartera): lista por fecha de fin, filtros «vigentes / por caducar / cerradas», y detalle con el ticket. Tarjeta «Por caducar» en Inicio.
- Riesgos: la garantía real depende del producto y del país; se muestra como **«según la regla por defecto»** y se puede cambiar. Esfuerzo: 1,5 sesiones.

### ✅ 1 · Previsión de fin de mes
- ⬜ A1.1 Pasada de pulido (0,2 sesiones): leer la tarjeta en el móvil, frase que explique «bajo/alto» y revisar el estado «pocos datos». Sin tablas ni rutas nuevas.

---

## Fase B · Casa y rutinas

### ⬜ 43 · Aviso de puerta o aparato abierto/encendido demasiado tiempo
- ⬜ B43.1 Leer cómo guarda el motor los estados `bool` (`Catalogo.cs`: `abierto` en sensores de puerta; `encendido` en enchufes y luces) y en `MotorReglas` añadir el tipo de condición **estado** (`Estado = "abierto" | "encendido"`, `Minutos`) además del numérico. Migración solo si hace falta un campo (`Tipo` en `ReglaCasa`).
- ⬜ B43.2 Pura `EvaluadorReglas.Estado(regla, historialDeEstado, ahora)`: «lleva así desde X» con histéresis para no avisar por un rebote. Tests: abre y cierra rápido, se queda abierta, aparato que se enciende justo al aviso, aparato sin conexión (no avisa).
- ⬜ B43.3 Aviso por Telegram/push con **botón** «Apagar» o «Ya lo sé (1 h)» (los botones en línea de Telegram ya existen para «Fui yo»). Dedupe por regla y por episodio.
- ⬜ B43.4 Pantalla: en el diálogo de reglas de casa, plantillas «Puerta abierta más de N min», «Enchufe/plancha encendido más de N min» y «Luz encendida sin nadie».
- Riesgos: sensores que reportan tarde (Tuya local vs nube); el aviso dice «según el último dato de hace X min». Esfuerzo: 1 sesión.

### ⬜ 41 · Clima de la casa por estancia
- ⬜ B41.1 Tabla `ResumenCasaDia` (aparato, clave, día, mín, media, máx), rellenada de noche desde `LecturaCasa` y con retención de 400 días (las lecturas crudas siguen en 30). Función pura de agregación con tests (huecos, un solo dato, cambio de hora).
- ⬜ B41.2 `GET /api/casa/clima?estancia&dias`: por estancia agrupa los sensores de temperatura y humedad y devuelve series, mín/media/máx y el «cuándo fue lo más frío/cálido».
- ⬜ B41.3 Pantalla «Clima» en Casa: una tarjeta por estancia con línea de 24 h/semana/mes (ECharts), comparativa entre estancias y marcas de «moho > 65 % de humedad» y «frío < 17 °».
- ⬜ B41.4 Avisos opcionales por umbral sostenido (reglas `SoloAvisar` precreadas desde la pantalla) y exportación CSV del rango.
- Riesgos: aparatos sin estancia → se muestran en «Sin asignar». Esfuerzo: 1,5 sesiones.

### ⬜ 40 · Salgo de casa con lista de comprobación
- ⬜ B40.1 Al llegar el evento `salgo` (ya existe), calcular la **lista**: sensores de puerta/ventana abiertos, aparatos encendidos que no deberían (según una lista que eliges: plancha, estufa, luces) y el PC (que ya se bloquea).
- ⬜ B40.2 Respuesta del atajo `POST /api/atajos/modo {modo:"salir"}`: «Hasta luego. Ojo: ventana del salón abierta; plancha encendida» (o «Todo cerrado»). Con «apagar automáticamente lo de la lista» opcional (nunca la nevera ni lo marcado como «siempre encendido»).
- ⬜ B40.3 Notificación de Telegram con botones «Apagar la plancha» por aparato.
- ⬜ B40.4 Pantalla: en Escenas solas, el editor de «qué comprobar al salir» y el historial de las últimas salidas.
- Riesgos: apagar algo importante → solo apaga lo explícitamente marcado. Esfuerzo: 1 sesión.

### ⬜ 39 · Modo «me voy a dormir»
- ⬜ B39.1 Evento `duermo` en `ReglaContexto` (y `despierto`) con las mismas condiciones de franja/días; el disparo viene del Atajo (automatización de la Concentración «Dormir» del iPhone), de Telegram (`/buenas noches`) o de un botón en Inicio.
- ⬜ B39.2 La rutina compone: **escena de casa** (apagar luces, calefacción a nocturna), **escena del PC** (bloquear, permitir suspender, volumen 0; las que apagan el PC nunca solas) y la **música** (pausa o temporizador de sueño).
- ⬜ B39.3 Mensaje de vuelta al atajo: «Buenas noches. Mañana: reunión a las 9:00. Despierta a las 7:30. Tiempo: 4 °.» con la agenda y la alarma sugerida; más las comprobaciones de la 40 (ventanas, estufa).
- ⬜ B39.4 «Despierto»: lo opuesto, a su hora o al desbloquear el PC por primera vez («Buenos días: tiempo, agenda y tareas de hoy»).
- ⬜ B39.5 Pantalla en Escenas solas, tests con reloj falso y receta de Atajos con texto para pegar.
- Riesgos: apagar cosas de noche por error → registro visible y un botón «deshacer» (reponer luces) durante 10 min. Esfuerzo: 1,5 sesiones.

### ⬜ 46 · Ropa tendida y lluvia
- ⬜ B46.1 Ajuste `ropa-tendida` (fecha de inicio) con tres maneras de marcarla: botón en Inicio, Atajo («Ropa tendida») y Telegram (`/ropa`). Caduca sola a las 10 h o al marcar «recogida».
- ⬜ B46.2 `ServicioTiempo` pide `minutely_15` y `precipitation_probability` de Open-Meteo para tu lugar. Pura `Lluvia.EnMinutos(datos, ahora)` → «empieza en ~25 min» o nada. Tests con series sintéticas (chubasco corto, lluvia ya empezada, falsa alarma).
- ⬜ B46.3 Vigilante cada 5 min mientras haya ropa tendida: aviso («Va a llover en ~25 min: recoge la ropa») por push + Telegram, con dedupe por episodio y un botón «Recogida».
- ⬜ B46.4 Chip en Inicio «Ropa tendida desde las 10:20» con el estado del cielo.
- Riesgos: predicción a 15 min es aproximada → el aviso dice «probable». Esfuerzo: 1 sesión.

---

## Fase C · PC y seguridad

### ⬜ 58 · Salud del disco: lo que falta
- ✅ Ya hay: `TendenciaDisco.cs` (días hasta llenarse), aviso de SMART distinto de «Healthy» en `Pc.cs`.
- ⬜ C58.1 El agente añade atributos útiles de `Get-StorageReliabilityCounter` (desgaste, temperatura, horas encendido, errores de lectura/escritura) al informe de salud (ya tiene permisos de administrador).
- ⬜ C58.2 Aviso proactivo «El disco C: se llena en ~N días» (umbral 45 y 14 días, dedupe por umbral y unidad) y «desgaste del SSD > 80 %».
- ⬜ C58.3 «Qué ocupa»: orden del agente `espacio` (solo lectura, con tope de profundidad y de tiempo) que devuelve las 15 carpetas más grandes de una raíz que elijas (nunca fuera de las unidades locales); pantalla con barras.
- Riesgos: el análisis puede tardar → tiempo límite de 60 s y resultado parcial marcado como tal. Esfuerzo: 1 sesión.

### ⬜ 49 · Dispositivos de confianza: «no lo reconozco» y mapa
- ✅ Ya hay: tabla de equipos con `Confianza`, aviso de equipo o país nuevo, ubicación por IP (`GeoIp`).
- ⬜ C49.1 Botón **«No lo reconozco»** en cada equipo: cierra sus sesiones, lo marca `Sospechoso` (el semáforo pasa a ámbar), avisa por Telegram con IP y ciudad, y pregunta si cambiar la contraseña (enlace a Perfil). Función pura de la acción con tests (equipo con sesiones abiertas, sin sesiones, el propio equipo → no se deja).
- ⬜ C49.2 Campo «última vez visto» legible («hace 3 h desde Madrid») y estado «en línea / dormido / hace tiempo» en la lista.
- ⬜ C49.3 **Mapa** de equipos y de redes de los atajos (`TokenAtajo.Redes`) con Leaflet y `GeoIp`, detrás del PIN; en otro color los de confianza.
- ⬜ C49.4 Tests y docs en `07-seguridad.md`.
- Riesgos: GeoIP impreciso con VPN → se dice «ubicación aproximada». Esfuerzo: 1,5 sesiones.

### ⬜ 60 · Abrir y cerrar aplicaciones desde la app
- ⬜ C60.1 **Catálogo de apps** elegidas por ti. El agente lista las aplicaciones del menú Inicio (`Get-StartApps`: nombre y AppID, que incluye Claude y las de la Tienda) y las manda a la API; en Seguridad/Mi PC eliges cuáles se pueden abrir y se guardan en una tabla `AppPc` (clave, nombre, AppID). La orden `abrir` acepta **solo una clave del catálogo** (nunca una ruta) y el agente comprueba que ese AppID sigue en `Get-StartApps` antes de abrirlo con `explorer.exe shell:AppsFolder\<AppID>`.
- ⬜ C60.2 Pantalla en Mi PC: botones con el nombre de cada app («Abrir Claude», «Abrir Código», «Abrir Chrome»…), con «Face ID» solo para las marcadas como delicadas. Se mantienen la calculadora, el bloc y el explorador.
- ⬜ C60.3 **Cerrar**: orden `procesos` (solo lectura) devuelve las aplicaciones con ventana (`MainWindowTitle`), su memoria y CPU; orden `cerrar` con el identificador de proceso **y su hora de inicio** (para no cerrar otro que reutilice el número). Primero `CloseMainWindow` (cierre amable); «Forzar» después de 10 s pide la contraseña otra vez (`Reautenticar`).
- ⬜ C60.4 Lista de **protegidos** que nunca se cierran (explorer, el propio lanzador, `Mda.Api`, `postgres`, `tailscale*`, servicios de Windows) y tope de 1 cierre por segundo.
- ⬜ C60.5 Atajo opcional «Modo trabajo terminado» que cierra la lista que elijas, y Telegram `/cerrar chrome`.
- ⬜ C60.6 Tests de la lógica pura (validación de la clave, protegidos, coincidencia de inicio) y docs.
- Riesgos: es lo más delicado del plan; por eso catálogo cerrado, verificación en el agente, protegidos y confirmación. Esfuerzo: 2 sesiones.

### ⬜ 59 · Bandeja en los dos sentidos
- ⬜ C59.1 **App → PC**: «Enviar al PC» en Mi PC sube el archivo (límite 100 MB, sin ejecutables, como `Archivos:PermitirEjecutables`) a un almacén temporal cifrado; el agente lo recoge (como `descargar`) y lo deja en `Zona privada\Bandeja\Recibidos` con su estado «en el PC».
- ⬜ C59.2 **PC → móvil**: lo que dejes en `Bandeja\Para el móvil` lo sube el agente (en trozos) al almacén temporal; la app lo ofrece con un **enlace que caduca en 24 h** (y se borra al descargar o al caducar). Detrás de «contraseña otra vez» si pesa más de 20 MB.
- ⬜ C59.3 La lista de la bandeja deja de ser «50 en memoria»: tabla `ElementoBandeja` (dirección, nombre, tamaño, estado, caduca) con limpieza diaria.
- ⬜ C59.4 Pantalla: dos pestañas «Para el PC» y «Para el móvil», progreso y errores claros; y en el iPhone, la hoja de compartir con un Atajo («Enviar al PC»).
- ⬜ C59.5 Tests (límites, tipos prohibidos, caducidad) y docs.
- Riesgos: espacio y antivirus → almacén con tope de 1 GB y limpieza; nada se ejecuta. Esfuerzo: 2 sesiones.

### ⬜ 85 · Panel de salud de la app
- ⬜ C85.1 `GET /api/sistema/app` con: tamaño de la base (`pg_database_size`), las 10 tablas más grandes (`pg_total_relation_size`), filas de cada una, antigüedad de la última copia y del último simulacro, errores 5xx recientes (ya hay contador), memoria de la API y tiempo en marcha.
- ⬜ C85.2 Tabla `TamanoTabla` con una foto diaria para ver **cuál crece más** (y su tendencia, como `TendenciaDisco`).
- ⬜ C85.3 Acciones de mantenimiento **con confirmación y vista previa**: purgar llamadas externas antiguas, regenerar miniaturas, limpiar lecturas viejas, `VACUUM ANALYZE` (solo si no hay otra tarea).
- ⬜ C85.4 Pantalla en Seguridad › Vigilancia (o «Sistema») con tarjetas y las recomendaciones («`lecturas` ocupa 400 MB: se pueden resumir»).
- ⬜ C85.5 Tests de la lógica pura y que funcione igual en el nativo y en Docker.
- Riesgos: `VACUUM FULL` bloquea → no se ofrece. Esfuerzo: 1,5 sesiones.

---

## Fase D · Música

### ⬜ 76 · Historial propio de lo que escuchas
- ⬜ D76.1 Servicio de fondo que cada 20 min pide `recently-played?after=<último>` y guarda en `Escucha` (id de la canción, nombre, artistas, álbum, portada, duración, `Fecha`); Spotify solo guarda las últimas 50, así que hay que ir recogiéndolas. Dedupe por (canción, fecha).
- ⬜ D76.2 Estadísticas puras con tests: top del mes y del año, horas del día (mapa de calor), racha de días, artista descubierto este mes, «canción de la semana».
- ⬜ D76.3 Pantalla «Lo que escuchas» en Música: tarjetas por periodo, gráfico de horas y lista de las más repetidas; reutiliza `/top` mientras no haya datos propios.
- ⬜ D76.4 Retención ilimitada pero pequeña (unos pocos MB al año); borrar el historial desde Ajustes.
- Riesgos: Spotify limita el ritmo → 20 min es de sobra. Esfuerzo: 2 sesiones.

### ⬜ 75 · Letras sincronizadas
- ⬜ D75.1 `ServicioLetras`: consulta a **LRCLIB** (`/api/get` por título, artista, álbum y duración; sin clave y con un `User-Agent` educado) y cachea el resultado por canción en una tabla `Letra` (para no repetir).
- ⬜ D75.2 Parser puro del formato LRC (`[mm:ss.xx]`, etiquetas, líneas repetidas) con tests, y búsqueda de «la línea actual» según el progreso.
- ⬜ D75.3 En el reproductor de escritorio (`mini-musica.ts`), un panel de letra con la línea actual resaltada y desplazamiento suave (el progreso ya se actualiza cada segundo); solo en ordenador y con interruptor.
- ⬜ D75.4 Si no hay letra, un mensaje discreto; si solo hay sin sincronizar, se enseña estática.
- Riesgos: no todas las canciones tienen letra y son de la comunidad; se muestra la fuente y se guarda solo caché local. Esfuerzo: 1,5 sesiones.

### ⬜ 77 · Listas por estado de ánimo
- ⬜ D77.1 Permiso extra de Spotify para leer tus listas (`playlist-read-private`); hay que volver a autorizar la conexión desde Seguridad (lo dice la pantalla, sin tocar el código de acceso).
- ⬜ D77.2 Tabla `MomentoMusical` (nombre, icono, `playlistUri`, franja opcional); los «momentos» de serie: Concentración, Cocina, Entrenar, Dormir, Fiesta; tú eliges la lista de cada uno.
- ⬜ D77.3 `POST /api/musica/momento/{id}`: empieza la lista (`/me/player/play` con `context_uri`, mezclar activado, volumen sugerido). Requiere Premium y un dispositivo; los errores se explican («abre Spotify en algún aparato»).
- ⬜ D77.4 Chips en Música y en el reproductor, y **sugerencia por hora** («son las 23:00: ¿Dormir?»). Se puede usar desde las escenas de contexto (39: al dormir) y desde Atajos.
- Riesgos: sin Premium no se puede controlar la reproducción; se detecta y se avisa. Esfuerzo: 1,5 sesiones.

### ⬜ 74 · Fondos que reaccionan
- ⬜ D74.1 Servicio `SenalesFondo` (signals): `musica` (suena o no y progreso), `carga` (CPU del PC, que ya llega por SignalR), `hora` y `tonoPortada` (el color de la portada que ya extrae `tonoDe`).
- ⬜ D74.2 `Opciones` gana `energia` (0–1) y `tono?`; la base de los fondos las lee cada fotograma. Cambios por escena, pocos y pequeños: la nebulosa y el plasma respiran con la música, la lluvia digital cae más rápido con carga alta, el reactor late a más velocidad, y con el tono de la portada la paleta se tiñe.
- ⬜ D74.3 Ajustes › Fondo: «Reaccionar a: música · carga del PC · hora · nada» (por dispositivo) con un deslizador de «cuánto».
- ⬜ D74.4 El latido es suave y aproximado (Spotify ya no da análisis de audio a apps nuevas): se basa en que suena y en el progreso, no en el ritmo real.
- ⬜ D74.5 Tests de las señales y de que ninguna escena rompe con `energia` en los extremos.
- Riesgos: gasto de batería en el móvil → en `ligero` solo reacciona la música, a media tasa. Esfuerzo: 1,5 sesiones.

---

## Fase E · Módulos de vida

### ⬜ 13 · Personas: cumpleaños, contactos y gustos
- ⬜ E13.1 Tablas `Persona` (nombre, apodo, relación, teléfonos y correos en JSON **cifrados**, dirección, cumpleaños día/mes y año opcional, **gustos** como etiquetas más texto, tallas, alergias, notas, `Avisar` y antelación por persona) y `RegaloPersona` (idea, precio estimado, ocasión, fecha, estado: idea · comprado · regalado, cómo sentó).
- ⬜ E13.2 **Importación elegida por ti** (nada se importa sin que lo marques):
  1. **Archivo vCard (.vcf)**: en el iPhone, Contactos → seleccionar → Compartir → «Guardar en Archivos» y subirlo aquí (o AirDrop al PC). Parser puro de vCard 3.0/4.0 (líneas plegadas, `QUOTED-PRINTABLE`, `BDAY` en `YYYYMMDD`, `YYYY-MM-DD` y `--MMDD`, varios teléfonos), con tests.
  2. **Uno a uno desde el iPhone**: un Atajo «Añadir a mi zona privada» en la hoja de compartir de la ficha de contacto (permiso `personas`).
  3. **Google Contactos** con la conexión de Google que ya tienes (hay que volver a autorizar con el permiso de lectura de contactos; solo si lo quieres).
- ⬜ E13.3 **Pantalla de importación**: lista con buscador y casillas, detección de duplicados (mismo teléfono/correo/nombre), y un selector «¿qué traigo?»: solo nombre y cumpleaños, o también teléfono, correo, dirección y notas. Vista previa antes de guardar.
- ⬜ E13.4 Pantalla «Personas»: tarjeta por persona con próxima fecha, gustos, ideas y regalos pasados; filtros «cumples del mes», «sin regalo pensado» y «sin hablar hace tiempo» (campo opcional «última vez que hablamos»).
- ⬜ E13.5 **Cumpleaños**: aviso escalonado (14, 7, 1 días y el día mismo) con dedupe, en Agenda como eventos virtuales (sin duplicar en la base) y una tarjeta «Próximos cumpleaños» en Inicio. Telegram `/cumples`. El asistente entiende «el cumple de María es el 3 de marzo».
- ⬜ E13.6 **Ideas de regalo**: reglas propias por **gustos × presupuesto × lo que ya se regaló** (nunca repite dentro de 3 años), con 5 sugerencias por persona y la opción de guardar una como idea. Sin IA externa.
- ⬜ E13.7 Tests (parser, duplicados, fechas bisiestas del 29 de febrero, avisos), receta del Atajo y sección en docs.
- Riesgos: datos de otras personas → cifrado, PIN opcional y borrado completo desde la pantalla. Esfuerzo: 3 sesiones.

### ⬜ 17 · Planificador de comidas
- ⬜ E17.1 Tablas `Receta` (nombre, raciones, tiempo, etiquetas, pasos, `Origen`), `IngredienteReceta` (nombre, cantidad, unidad) y `MenuDia` (fecha, comida/cena, receta o texto libre).
- ⬜ E17.2 **Importar receta desde una dirección** leyendo el marcado `schema.org/Recipe` (JSON-LD) que usan casi todas las webs de cocina, sin servicios externos. Parser puro con tests sobre ejemplos reales guardados.
- ⬜ E17.3 **Generador de menú semanal** (reglas): respeta etiquetas («rápida», «vegetariana»), alergias y gustos, no repite en 14 días y reparte «días de sobras». Siempre editable arrastrando.
- ⬜ E17.4 **Lista de la compra automática**: suma los ingredientes de la semana, quita lo que ya hay (`Compra` sabe lo que sueles tener) y la envía a `Compra` con la etiqueta de la receta. Tests de unidades (g/kg, ml/l, «un poco»).
- ⬜ E17.5 Pantalla «Cocina»: semana en cuadrícula, receta con raciones ajustables y «cocinar hoy» (pasos grandes, para el móvil en la cocina, sin que se apague la pantalla).
- ⬜ E17.6 Aviso diario «Hoy toca: …; descongela X» a la hora que elijas.
- Riesgos: unidades mezcladas → normalización con tabla pequeña y revisión manual. Esfuerzo: 3 sesiones.

### ⬜ 79 · Series y películas
- ⬜ E79.1 Tablas `Titulo` (tipo, id externo, nombre, estado: por ver · viendo · vista, temporada/episodio, nota, plataforma, fecha del próximo episodio o estreno).
- ⬜ E79.2 **Series con TVMaze** (sin clave): búsqueda, ficha y `next episode`; servicio de fondo diario que actualiza las fechas. **Películas**: si pones una clave gratuita de **TMDB** se activan estrenos y «dónde verlo» en España; sin clave, entrada manual.
- ⬜ E79.3 Avisos («Mañana sale el 3×05 de …», «Hoy se estrena …») con dedupe, y un resumen semanal «Esta semana sale…».
- ⬜ E79.4 Pantalla con tres columnas (por ver · viendo · vista), marcar el siguiente episodio con un toque y estadísticas del año (horas, géneros).
- ⬜ E79.5 Tests del cálculo de «próximo episodio», zonas horarias y series canceladas. Si se usa TMDB, el aviso de atribución que exige.
- Riesgos: horarios de emisión según país → se muestra la fecha local del estreno. Esfuerzo: 2,5 sesiones.

### ⬜ 19 · Bienestar: salud, ejercicio y un plan (módulo grande)
Nombre del módulo: **Bienestar** (la «Salud» que ya existe es la del PC). Es una **ayuda personal, no consejo médico**: la pantalla lo dice y manda consultar a un profesional ante dolor, mareos, enfermedad o embarazo.
- ⬜ E19.1 **Tablas** (todas cifradas con `Boveda`): `PerfilBienestar` (altura, año de nacimiento, objetivo, nivel, días por semana, equipo disponible, limitaciones), `Medida` (fecha, peso, cintura, pecho, brazo, % graso opcional), `Entreno` (fecha, tipo, duración, esfuerzo percibido 1–10, notas), `SerieEjercicio` (entreno, ejercicio, serie, repeticiones, peso), `RegistroDia` (agua, sueño, pasos, ánimo), `PlanEntreno` (semanas, semana actual, plantilla) y `SesionPlan` (día, ejercicios previstos, estado).
- ⬜ E19.2 **Cuestionario inicial** de 6 preguntas (objetivo: perder grasa, ganar fuerza o músculo, resistencia, movilidad o salud general; nivel; días; equipo: gimnasio, casa con material, sin nada; limitaciones y lesiones; tiempo por sesión).
- ⬜ E19.3 **Generador de plan** por reglas y plantillas, con la progresión explícita:
  - *Principiante, 3 días, cuerpo completo* (sentadilla o prensa, empuje, tirón, bisagra de cadera, core; 2–3 series de 8–12; subir carga cuando se hacen todas las repeticiones con buena técnica dos sesiones seguidas).
  - *Intermedio, 4 días, torso/pierna* y *empuje/tirón/pierna* a 5–6 días.
  - *Sin material*: calistenia con progresiones (flexión inclinada → normal → declinada; sentadilla → búlgara).
  - *Caminar → correr 0 a 5 km en 8 semanas* (intervalos que crecen) y *caminata progresiva* para sedentarios (de 20 a 45 min).
  - *Movilidad diaria de 10 min* para el que trabaja sentado.
  - Cada 4.ª semana, **descarga** (−40 % de volumen). Si faltas a más del 40 % de las sesiones, el plan se **simplifica** solo en lugar de acumular culpa.
- ⬜ E19.4 **Biblioteca de ejercicios** (nombre, músculos, pasos, errores típicos, variante fácil y difícil, vídeo opcional por enlace) y **tarjetas de ayuda**: hidratación, sueño (7–9 h como referencia general), proteínas (referencia por kg y objetivo, sin dietas), calentar, descansar, cómo medir esfuerzo, qué hacer si hay agujetas fuertes.
- ⬜ E19.5 **Seguimiento**: gráficas de peso con **media móvil de 7 días** (para no obsesionarse con el dato del día), tendencia por semana, medidas, volumen semanal, récords por ejercicio y racha de entrenos (se enlaza con **Hábitos** para reutilizar las rachas).
- ⬜ E19.6 **Entrada de datos fácil**: botón «Entrené hoy» en Inicio; registrar agua con un toque; y **Apple Salud por Atajos**: un atajo diario manda pasos, peso, sueño y entrenos al endpoint `POST /api/atajos/salud` (permiso `salud`) con receta para pegar.
- ⬜ E19.7 **Avisos** suaves y opcionales: «hoy toca pierna», «llevas 2 días sin moverte», «bebe agua» a horas elegidas. Telegram `/agua` y `/entreno`.
- ⬜ E19.8 **Resumen semanal** («esta semana: 3/3 entrenos, +2,5 kg en sentadilla, 7 h 12 min de sueño de media») en Inicio y en el resumen del domingo.
- ⬜ E19.9 Tests de la lógica pura: media móvil, generación de plan (cada combinación da un plan válido y sin dos días de lo mismo seguidos), progresión de cargas, descarga, adaptación por asistencia, y fechas/zonas horarias. Sin IA; todo determinista.
- Riesgos: salud y responsabilidad → textos prudentes, sin diagnósticos ni dietas milagro, límites sensatos (p. ej. no sugerir pérdidas de peso rápidas), y avisos de consultar a un profesional. Datos cifrados y con PIN opcional. Esfuerzo: 4 sesiones.

---

## Fase F · Interfaz

### ⬜ 67 · Temas completos
- ⬜ F67.1 Concepto `Tema` (aparte del color de acento): conjunto de variables (`--fondo`, `--tarjeta`, `--texto`, `--soft`, `--linea`, radios, sombras, tipografía) aplicado con `data-tema`. Temas: **Papel** (claro, serif suave), **Terminal** (oscuro, monoespaciado y verde), **Sepia** (cálido, descansa la vista), **Alto contraste** (negro y amarillo/blanco, bordes gruesos), **Medianoche OLED** (negro puro, para ahorrar batería) y **Cristal** (el de ahora, por defecto).
- ⬜ F67.2 Cada tema fija su modo (`data-mode`) para que los 3D (`pc-3d`, `habitacion-3d`, `candado-3d`) y los fondos sigan funcionando; el selector claro/oscuro se desactiva en los que son de un solo modo.
- ⬜ F67.3 **Comprobación de contraste automática** en los tests: función pura que calcula la razón WCAG de cada par texto/fondo de cada tema y exige AA (4,5:1 para texto normal; 7:1 en alto contraste).
- ⬜ F67.4 Ajustes › Tema con rejilla de tarjetas de vista previa en vivo y persistencia por dispositivo (con opción de sincronizar con el servidor).
- ⬜ F67.5 `theme-color` del navegador del móvil y los gráficos de ECharts siguen al tema (`resolverColor`).
- Riesgos: pantallas con colores fijos a mano → barrido y corrección de cada una a los tokens. Esfuerzo: 2 sesiones.

### ⬜ 70 · Tipografía y densidad
- ⬜ F70.1 Ajustes por dispositivo: **tamaño de letra** (90–130 %), **densidad** (compacta · cómoda · amplia → `--hueco`, rellenos y alto de filas) y **fuente** (la de siempre, del sistema, o una de lectura cómoda con licencia libre incluida en la app).
- ⬜ F70.2 Auditar que el tamaño se escala (`rem`) y corregir los `px` que no: barrido de `styles.css` y de los componentes más usados.
- ⬜ F70.3 Pruebas visuales a 90 %, 130 % y 375 px (sin desbordes ni textos cortados) y test de que los ajustes se guardan y se aplican.
- Riesgos: layouts de 3D y gráficas con medidas fijas → se prueban uno a uno. Esfuerzo: 1 sesión.

### ⬜ 68 · Inicio personalizable
- ⬜ F68.1 **Registro de tarjetas**: cada tarjeta de `panel.html` pasa a ser una entrada (`id`, título, componente, tamaños permitidos, condiciones: «solo si hay Spotify»). Las nuevas (cumpleaños, comidas, bienestar, series, garantías) se registran aquí.
- ⬜ F68.2 **Distribución** guardada en el servidor (JSON, como `PanelCasa`): orden, visibilidad y tamaño por tarjeta, y **una por tipo de dispositivo** (ordenador y móvil), con «Restablecer».
- ⬜ F68.3 **Modo edición**: arrastrar con `@angular/cdk/drag-drop` (con asa y con teclado para accesibilidad), ocultar y cambiar de tamaño S/M/L.
- ⬜ F68.4 **Vistas**: «Día», «Noche», «Trabajo»… con cambio manual o automático por hora/escena (reutiliza el contexto de la 39).
- ⬜ F68.5 `Modo pared` comparte el registro (mismas tarjetas, otra distribución).
- ⬜ F68.6 Tests del modelo de distribución (migrar una vieja, tarjeta que desaparece, tarjeta nueva sin hueco) y de que el orden por defecto es el actual.
- Riesgos: romper el mosaico fluido actual → se mantiene la lógica de `flex-wrap` y solo cambia el orden y la visibilidad. Esfuerzo: 3 sesiones.

### ⬜ 69 · Widget en la pantalla de bloqueo del iPhone
- ✅ Ya hay: `GET /api/atajos/hoy`, `herramientas/ios/hoy-widget.js` (Scriptable) y `LEEME.md`.
- ⬜ F69.1 `hoy-widget.js` pasa a leer `config.widgetFamily` y dibuja los **accesorios** de iOS 16+: **rectangular** (saldo y siguiente cita), **circular** (anillo con el % del presupuesto) y **en línea** («Disponible 523 €»). Se mantiene el widget grande de la pantalla de inicio.
- ⬜ F69.2 `/atajos/hoy` añade campos compactos (`saldoTexto`, `proximo`, `presupuestoPct`, `tareasHoy`, `cumple`, `entrenoHoy`) sin romper los antiguos (los usa el widget actual).
- ⬜ F69.3 Caché local de 15 min y modo sin conexión («hace 40 min»); el token (permiso `lectura`) en el llavero de Scriptable.
- ⬜ F69.4 Guía con capturas de dónde pegar el script y cómo añadir cada widget; prueba del script en Node con datos falsos para que no falle.
- Riesgos: iOS refresca los widgets cuando quiere; se dice en la guía. Un widget nativo propio **no** es posible con una PWA (ya está documentado). Esfuerzo: 1 sesión.

### ⬜ 89 · Bienvenida personal
- ⬜ F89.1 Generador puro `Bienvenida.Componer(contexto)` con **semilla por fecha** (la frase no cambia al recargar): estación, hora, día de la semana, festivos nacionales, cumpleaños de hoy (13), **algo de ayer** (gasto más alto, racha de hábitos, entreno, foto), el tiempo y la luna.
- ⬜ F89.2 Biblioteca de frases por situación (50–80, en español, sin cursilería), con variantes por hora y por estado de ánimo del día anterior si lo registras.
- ⬜ F89.3 Se muestra en el héroe de Inicio y como subtítulo del saludo; opción «bienvenida sencilla» para quien no la quiera.
- ⬜ F89.4 Tests: cada combinación produce una frase, sin repetir la de ayer, y el cumpleaños manda sobre lo demás.
- Esfuerzo: 1 sesión.

### ⬜ 81 · Constructor visual de reglas
- ⬜ F81.1 **Modelo unificado** de regla en el front (disparador → condiciones → acciones) que se traduce a lo que ya existe: `ReglaCasa`, `ReglaContexto` y `Automatizaciones` (`Disparadores`). No se rompe nada guardado; el constructor lee y escribe lo mismo.
- ⬜ F81.2 **Bloques** arrastrables: «Cuando…» (hora, llego/salgo/duermo, un aparato, el tiempo, el dinero, una tarea vencida, un papel que caduca, un estado sostenido de la 43), «Y si…» (franja, día, tiempo) y «Entonces…» (escena de casa o PC, avisar, crear tarea, apuntar algo, pausar música).
- ⬜ F81.3 **Lectura en español** de cada regla («Cuando llegues a casa, de noche y con lluvia, pon la escena Cine») y validación en vivo (qué falta, qué choca).
- ⬜ F81.4 **Simulación**: «con lo que pasó estas últimas 24 h, esta regla se habría disparado 3 veces» (pura, sobre las lecturas guardadas), sin ejecutar nada.
- ⬜ F81.5 Reglas con **plantillas** (las de las fases B y A) y exportar/importar como JSON.
- ⬜ F81.6 Tests de la traducción en los dos sentidos y de la simulación.
- Riesgos: es la pieza más grande de interfaz → se hace la última y por tramos (primero casa, luego contexto, luego automatizaciones). Esfuerzo: 3 sesiones.

---

## Lo que necesito de ti (decisiones y permisos)

1. **Contactos (13)**: ¿importo con archivo `.vcf`, con el Atajo uno a uno, con Google, o con los tres? (Recomiendo `.vcf` + Atajo; Google solo si quieres volver a autorizar.)
2. **Bienestar (19)**: tu objetivo principal, días por semana, material que tienes y si hay alguna lesión o limitación, para dejar bien la plantilla inicial. ¿Mandamos los datos de Apple Salud con un Atajo diario? ¿Quieres el módulo detrás del PIN?
3. **Comidas (17)**: personas en casa, alergias o cosas que no comes, y si quieres importar recetas de webs concretas.
4. **Series y películas (79)**: ¿pones una clave gratuita de TMDB (películas y «dónde verlo») o nos quedamos con series por TVMaze y películas a mano?
5. **Apps (60)**: qué aplicaciones quieres poder abrir desde la app (Claude, Código, Chrome…) y si permites **cerrar** aplicaciones (siempre con «contraseña otra vez» para forzar).
6. **Spotify (77 y 76)**: habrá que **volver a autorizar la conexión** para añadir permisos de listas y de reproducciones recientes.
7. **Temas (67)**: ¿te valen los seis (Papel, Terminal, Sepia, Alto contraste, Medianoche OLED, Cristal) o quieres otros?
8. **Gasolina (12)**: tu combustible, el radio en km y desde dónde buscar (casa u otro punto).

## Riesgos globales y cómo se cubren

- **Migraciones**: 12+ tablas nuevas. Todas con `dotnet ef`, probadas en el entorno de pruebas y con la copia automática previa en la real.
- **Agente del PC** (58, 59, 60): código nuevo con permisos de administrador. Cada orden con lista cerrada, validación en el propio agente y confirmación; revisión de seguridad aparte antes de desplegar.
- **Privacidad**: contactos, salud y ubicación cifrados o redondeados, tras el PIN y fuera de los registros.
- **Rendimiento del móvil**: lo nuevo con animación o canvas respeta el modo `ligero` y se queda quieto con `prefers-reduced-motion` y mientras haces scroll.
- **Alcance**: ~50 sesiones. Se puede parar al final de cualquier fase: cada una deja la app consistente y desplegable.
