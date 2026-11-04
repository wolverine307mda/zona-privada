# 2. Backend (API ASP.NET Core 10)

Proyecto: `api/Mda.Api` · tests: `api/Mda.Api.Tests` · solución: `ZonaPrivada.slnx`.

## Organización

- **Minimal APIs**. Cada carpeta es un módulo con una extensión `MapXxx(this IEndpointRouteBuilder app)` que crea un
  `MapGroup("/api/…").RequireAuthorization()` y sus rutas. `Program.cs` las llama todas.
- Lógica compartida en **métodos estáticos `internal`** que devuelven `IResult` (p. ej. `GastosEndpoints.Crear`,
  `AgendaEndpoints.Crear/Borrar/Apariciones`, `Cartera.Aportar`, `Horario.Poner`): los usan a la vez la ruta HTTP,
  el asistente y el bot de Telegram, así la validación es una sola.
- Validación: `Results.ValidationProblem(diccionario campo → mensajes)`; el front muestra esos mensajes bajo cada campo.
- Tiempo: se inyecta `TimeProvider` (en los tests se falsea). Zona horaria de la casa: `Zona` (`App:Zona`, por defecto `Europe/Madrid`).
- Nombres y comentarios **en español** (`Movimiento`, `Recurrente`, `Hucha`, `Aviso`…).

## Módulos

### Auth (`Auth/`)

- `AuthEndpoints.cs`: `MapIdentityApi` bajo `/api/auth` con *rate limiting* y rutas de registro/recuperación cerradas;
  `/api/sesion` (quién soy, salir).
- `SemillaAdmin.cs`: migraciones + usuario administrador sincronizado con la configuración (ver arquitectura).

### Sistema (`Sistema/`)

- `LectorSistema.cs`: interfaz `ILectorSistema` con implementaciones para **Linux** (`/proc/stat`, `/proc/meminfo`,
  `/proc/uptime`, `/proc/cpuinfo`), **Windows** (P/Invoke `GetSystemTimes`, `GlobalMemoryStatusEx`) y genérica.
  CPU y red se calculan como media desde la lectura anterior.
- `MonitorSistema.cs`: `BackgroundService` cada 2 s → `EstadoSistema` (historial ~2 min) → SignalR `SistemaHub`
  (`/hubs/sistema`, evento `metricas`). `EstadoAgente` guarda lo último del agente de Windows.
- Rutas: `GET /api/sistema` (último + historial), `POST /api/sistema/agente` (solo existe si hay `Agente:Token`).

### Cartera / Gastos (`Gastos/`)

**Modelo** (`Modelo.cs`):

- `Movimiento`: gasto o ingreso de un día. `Categoria` (id), `Concepto`, `Notas`, enlaces opcionales a `Recurrente`
  y `Deuda`, datos de **gasolina** (`Litros`, `PrecioLitro`, `Kilometros`) y `TieneTicket` (foto en `adjuntos`).
- `Recurrente`: fijo mensual/trimestral/anual (alquiler, Netflix, nómina). `Variable` = el importe cambia y se pide al aplicarlo.
- `Deuda`: algo que se paga poco a poco (el coche); lo pagado es la suma de movimientos con `DeudaId`.
- `Hucha` (objetivo, ahorrado, color, emoji, fecha), `Presupuesto` (límite mensual por categoría, con avisos 80 %/100 %),
  `Mantenimiento` (cada X km o meses).
- `Categorias.Todas`: id → (nombre, tipo, grupo).

| Gastos | id | | Ingresos | id |
|---|---|---|---|---|
| Vivienda | `vivienda` | | Nómina | `nomina` |
| Luz, agua e internet | `suministros` | | Extra | `extra` |
| Supermercado | `supermercado` | | Ventas | `ventas` |
| Restaurantes | `restaurantes` | | Otros ingresos | `otros-ingresos` |
| Ocio, Ropa, Salud, Tecnología, Transporte | `ocio`, `ropa`, `salud`, `tecnologia`, `transporte` | | | |
| Suscripciones | `suscripciones` | | | |
| Gasolina, Seguro, Mecánico, Cuota del coche, Papeleo y extras | `gasolina`, `seguro`, `mecanico`, `cuota-coche`, `coche-extras` | | | |
| Otros gastos | `otros` | | | |

**Rutas** (`GastosEndpoints.cs`): resumen de un mes (totales, comparación con el anterior, reparto por categoría,
gasto por día, fijos pendientes), estadísticas de N meses, gasolina (consumo por **método del depósito lleno**),
CRUD de movimientos y recurrentes, «registrar» un recurrente (crea el movimiento del periodo), ticket (subir/ver/borrar).
Crear un movimiento con `repite` lo convierte en el primero de un recurrente nuevo.

**Cartera** (`CarteraEndpoints.cs`, clase `Cartera`):

- **Saldo** = `saldo-base` + todo lo ingresado − todo lo gastado hasta hoy (puede ser negativo). «Ahora mismo tengo X»
  recalcula la base. También da lo previsto a fin de mes con los fijos que faltan y lo **libre** (saldo − huchas).
- **Huchas**: CRUD + aportar/sacar (`Cartera.Aportar`).
- **Presupuestos**: por categoría; al apuntar un gasto, `Cartera.ComprobarPresupuesto` avisa al cruzar el 80 % y el 100 % (una vez por nivel y mes).
- **Consejos** (`ConsejosDe`): ritmo diario hasta fin de mes, categorías que suben respecto a la media, coste anual de
  suscripciones, % de ahorro frente a la regla 50/30/20, colchón de meses.

**Deudas y coche** (`DeudasEndpoints.cs`, `CocheEndpoints.cs`): pagado, restante y fecha estimada de fin al ritmo
medio; pagos que crean movimientos; mantenimientos con «hecho» (fecha y km) y aviso cuando toca.

`Calculos.cs`: DTOs de respuesta y cálculos puros (resumen de mes, serie de meses, consumo, progreso de deuda).

### Agenda (`Agenda/`)

- `Modelo.cs` → `Evento`: `Tipo` (`Evento` con duración / `Recordatorio` puntual), `Titulo`, `Notas`, `Lugar`, `Inicio`,
  `Fin`, `TodoElDia`, `Color`, `Repetir` (`No/Diaria/Semanal/Mensual/Anual`) + `RepetirHasta`, `AvisoMinutos`
  (antes del inicio; negativo = después, para «el mismo día a las 9» en los de todo el día), canales `PorCorreo`,
  `PorWhatsApp`, `PorPush`, `PorTelegram`, `ProximoAviso` precalculado, `Hecho`, `EnGoogle`/`GoogleId`.
  Las repeticiones se calculan en **hora local** (`Zona`) con saltos iniciales para no recorrer años.
- `AgendaEndpoints.cs`: `GET /api/agenda?desde&hasta` devuelve **apariciones** (cada repetición en su fecha, más los
  eventos de Google si está enlazado), próximos, CRUD, marcar hecho, `/estado` (canales disponibles, Google, horario).
- `Horario.cs` → `record Horario(Activo, Nombre, int[] Dias, Entrada, Salida)` guardado en el ajuste `horario`
  (JSON camelCase). Días 1 = lunes … 7 = domingo. Métodos: `Trabaja(dia)`, `Pisa(inicio, fin)` (¿una cita cae en el
  horario?), `Texto()` («de lunes a viernes, de 8:30 a 17:30»), `Errores()`, `Leer/Guardar/Poner`.
  Rutas `GET/PUT /api/agenda/horario`.
- `Recordatorios.cs`: cada 30 s envía los avisos vencidos por los canales marcados y recalcula `ProximoAviso`.
  El estado de envío va en el evento (`CanalesEnviados`, `CanalesRendidos`, `Intentos`, `ReintentoDesde`): WhatsApp (CallMeBot, que tarda) sale
  5 minutos antes de la hora y los demás canales a la hora; un canal que falla se reintenta a 1, 5 y 15 minutos sin repetir los que ya llegaron; se anota
  `ReintentoDesde` antes de enviar para no repetir si el proceso cae; `UltimaOcurrenciaAvisada` evita que editar un evento ya avisado lo avise otra vez.
  Si no llega por ningún canal, o el aviso tiene más de 12 h de retraso, lo cuenta por Telegram (o push). WhatsApp: texto de 300 letras como máximo, 3 s entre mensajes y el error del cuerpo de CallMeBot cuenta como fallo.
- `GoogleCalendar.cs`: lee eventos del calendario principal y copia allí los de la agenda marcados (crea/actualiza;
  si se borró en Google, lo recrea).

### Asistente (`Asistente/`) — intérprete de frases en español

**No usa IA externa**: es un intérprete por reglas (`Interprete.cs`, ~650 líneas) con expresiones regulares generadas
(`[GeneratedRegex]`). Entrada: frase + `Contexto` (ahora, zona, conceptos aprendidos → categoría, huchas, horario).
Salida: `Propuesta` (clase, resumen, campos para rellenar el formulario, dudas, `Pregunta` opcional, `Respuesta`).

Mecánica:

- `Frase`: texto original + versión «plana» (`Aplanar`: minúsculas, sin tildes) + **máscara de caracteres usados**.
  Cada regla «coge» su trozo (`Coger`) y lo marca; lo que sobra (`Resto`) es el concepto/título.
- **Clases**: `movimiento` (gasto/ingreso), `agenda` (evento/recordatorio), `hucha` (aportar/sacar), `horario`,
  `pregunta`, `nada`.
- Orden de decisión en `Entender`: preguntas de agenda («¿qué tengo mañana?») y horario («curro de 8:30 a 17:30 de
  lunes a viernes») van **antes** que el tiempo y el dinero. Una frase con rango horario o con hora + palabra médica
  es una cita, aunque mencione una categoría (evita que «médico de 10 a 11:30» sea un gasto de salud).
- Entiende: importes («12,50 €», «15 pavos»), fechas relativas (hoy, ayer, anteayer, el martes, el 3 de noviembre,
  mañana, pasado…), horas («a las 5 de la tarde», «16:00»), rangos («de 10 a 11:30»), «hasta las 12», duraciones
  («durante una hora y media»), lugares («en el centro de salud»), repetición («cada lunes»), avisos («avísame una hora
  antes»), gasolina («40 litros a 1,499»), huchas por nombre/emoji.
- **Citas médicas** (médico, dentista, fisio, análisis…): color verde y aviso 60 min antes. Si la cita cae dentro del
  horario de trabajo añade la duda «Cae en tu horario de trabajo (8:30–17:30)».
- **Aprendizaje**: `ContextoDe` mira los últimos 1000 movimientos y asocia concepto → categoría más usada.
- **Preguntas** (`QuePregunta`: `Saldo`, `Gastado`, `Ingresado`, `Tiempo`, `Agenda`): las contesta
  `AsistenteEndpoints.Responder` con datos reales («Este mes llevas 312,40 € en restaurantes…»).
  `AsistenteEndpoints.Agenda` lista el día: «💼 trabajo de 8:30 a 17:30», eventos y recordatorios.
- Ruta: `POST /api/asistente/entender { texto }` (máx. 300 caracteres). Guardar lo hace el front con las rutas normales.

### Telegram (`Telegram/`)

- `Bot.cs` (singleton): token en `Telegram:Token`. Chat vinculado en ajustes (`telegram-chat`, `telegram-nombre`).
  `NuevoCodigo()` crea un código de un solo uso; `Vincular`, `Desvincular`, `Usuario()` (getMe),
  `Enviar(texto, teclado?, html?)` y `Llamar(metodo, cuerpo)`. HttpClients `telegram` y `telegram-largo` con
  **`RemoveAllLoggers()`** porque el token va en la URL y acabaría en los registros.
- `Sondeo.cs` (`BackgroundService`): `deleteWebhook`, `setMyCommands` (hoy, tiempo, saldo, ayuda), descripción y bucle
  `getUpdates` (long polling). Cada actualización se procesa en un *scope* con `Conversacion`.
- `Conversacion.cs`: **solo atiende al chat vinculado**. `/start <código>` vincula; `/ayuda`, `/hoy` (tiempo + agenda
  del día + saldo), `/tiempo`, `/saldo`. Cualquier otro texto → `Interprete.Entender`: preguntas se contestan;
  propuestas se guardan 2 h en memoria con botones *inline* **Guardar / Cancelar** y, tras guardar, **Deshacer**
  (callbacks `si:`, `no:`, `dm:`, `de:`, `dh:`). Los eventos creados desde Telegram avisan por Telegram.
- `TelegramEndpoints.cs`: `GET /api/telegram` (estado), `POST /vincular` (devuelve `https://t.me/<bot>?start=<código>`), `DELETE`.

### El tiempo (`Tiempo/`)

- `ServicioTiempo`: **Open-Meteo** (previsión y geocodificación, sin clave), caché 15 min (`IMemoryCache`).
  Lugar en el ajuste `tiempo-lugar`. Códigos WMO → emoji y texto. `Frase(previsión, día)` para el asistente;
  `AvisoDe(previsión, mañana)` detecta tormenta, lluvia ≥ 60 %, máxima ≥ 35 °C o mínima ≤ 0 °C.
- Rutas: `GET /api/tiempo`, `GET /buscar?q=`, `PUT /lugar`, `PUT /avisos`.
- `Vigilante.Tiempo`: desde las 20:00, una vez al día, push + Telegram si mañana hay algo que avisar.

### Avisos (`Avisos/`)

- `IAvisos` / `Avisos`: `PorCorreo(asunto, texto, ruta)`, `PorCorreoHtml`, `PorWhatsApp`, `PorTelegram`, `Canales`
  (qué está configurado). Todos los correos pasan por `Plantilla.Correo` (HTML con tablas y estilos en línea, cabecera
  con marca, botón a la ruta de la app, pie con enlaces); `Plantilla.DeTexto` convierte líneas «Clave: valor» en ficha.
- `Push`: Web Push estándar con **claves VAPID propias** generadas y guardadas en ajustes; borra suscripciones caducadas (404/410).
- `Vigilante`: ver tareas de fondo (resumen semanal, coche, copias, tiempo).

### Notas (`Notas/`)

`Nota`: título, contenido Markdown (las líneas `- [ ]` son tareas), color, etiquetas (texto separado), fijada,
archivada, fechas. CRUD en `/api/notas`.

### Documentos (`Archivos/`)

- `Carpetas`: raíces = carpetas montadas en `Archivos:Base/<id>` (documentos, escritorio, descargas, imagenes, musica,
  videos). Una no montada no aparece. `Resolver(raíz, ruta)` impide salir de la raíz, entrar en la papelera o seguir
  **enlaces simbólicos**. Nombres válidos también en Windows; colisiones → «foto (2).jpg».
- **Papelera**: lo borrado va a una carpeta oculta dentro de cada raíz y se puede recuperar a su sitio.
- Rutas: raíces, lista, buscar (200 resultados), archivo (con **rangos** para adelantar audio/vídeo; el texto se sirve
  como texto plano y lo abierto en línea no puede ejecutar nada), subir (multipart a disco con nombre temporal),
  carpeta, renombrar, mover, borrar, papelera (listar, recuperar, vaciar).

### Conexiones (`Conexiones/`)

- OAuth 2 de **código de autorización en el servidor**: el secreto y los tokens nunca llegan al navegador; los tokens
  se guardan **cifrados** (Data Protection) en `conexiones`. El `state` se guarda en memoria y vale una vez (la vuelta
  llega sin cookie porque la cookie es `SameSite=Strict`).
- Proveedores: **Spotify** (música) y **Google Calendar**. `GET /api/conexiones/{id}/conectar` redirige al proveedor;
  `/vuelta` recibe el código. `ServicioConexiones` renueva el token si caducó.
- `MusicaEndpoints`: ahora suena, recientes, cola, top, control (play/pause/next/previous).

### Seguridad y perfil (`Seguridad/`)

- `Seguridad.cs`: `IpCliente.De` (respeta `CF-Connecting-IP`/`X-Forwarded-For`), `UseCabecerasSeguras` (CSP, etc.).
- `Sesiones.cs`: `SesionAbierta`, `Acceso`, `Ajuste`, registro de intentos de login, descripción del dispositivo
  («iPhone · Safari»), aviso de inicio de sesión por correo/push/Telegram (con hora local y segundos).
- `SeguridadEndpoints.cs`: sesiones (con «en línea»), latido, cerrar una / todas menos esta, accesos, estado
  (2FA, canales), ajustes de avisos, aviso de prueba por canal (`correo`, `whatsapp`, `push`, `telegram`).
- `Perfil.cs`: nombre, apellidos, correo, teléfono, foto (`adjuntos/avatar`), cambio de contraseña.

### Galería y estado público (`Galeria/`)

- `Galeria.cs`: `FotoPublica` (tabla `fotos_publicas`) y `Derivados` (SkiaSharp: decodifica, aplica la orientación EXIF, reduce a 480/1600 px, codifica WebP y lo guarda en `Galeria:Derivados`; hasta dos a la vez). Solo JPG, PNG, WebP, GIF y BMP; HEIC hay que convertirlo.
- `GaleriaEndpoints.cs`: gestión con sesión (`/api/galeria`) y lectura pública (`/api/publico/galeria`).
- `EstadoPublico.cs`: ajustes `publico-estado` y `publico-cpu` y los endpoints públicos `/api/publico/estado` (+ SSE `/vivo`).
