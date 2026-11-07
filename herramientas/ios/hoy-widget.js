// Widget «Hoy» de la zona privada para el iPhone, con Scriptable (app gratuita de la App Store).
// Pantalla de inicio (pequeño, mediano, grande): lo mismo que la pantalla «Hoy». Pantalla de bloqueo (iOS 16+): tres accesorios —rectangular (saldo y siguiente cita),
// circular (anillo con el % del presupuesto) y en línea («Disponible 523 €»)—.
// Cómo ponerlo: mira LEEME.md. La dirección y el token se guardan en el llavero del iPhone la primera vez (no quedan escritos en este archivo).
// La respuesta se guarda 15 minutos en el propio iPhone: sin conexión se enseña lo último con «hace 40 min».

const CLAVE_URL = "mda-url";
const CLAVE_TOKEN = "mda-token";
const CACHE = "mda-hoy-cache.json";
const VALIDEZ_MS = 15 * 60 * 1000;

async function pedirDato(clave, titulo, ayuda) {
  if (Keychain.contains(clave)) return Keychain.get(clave);
  const a = new Alert();
  a.title = titulo;
  a.message = ayuda;
  a.addTextField("");
  a.addAction("Guardar");
  a.addCancelAction("Cancelar");
  if ((await a.present()) === -1) throw new Error("Cancelado");
  const valor = a.textFieldValue(0).trim();
  Keychain.set(clave, valor);
  return valor;
}

// ---- caché local: 15 minutos y, si no hay red, lo último con su antigüedad ----
function leerCache(fm) {
  try {
    const ruta = fm.joinPath(fm.documentsDirectory(), CACHE);
    if (!fm.fileExists(ruta)) return null;
    const c = JSON.parse(fm.readString(ruta));
    return c && c.datos && typeof c.hora === "number" ? c : null;
  } catch (e) { return null; }
}
function guardarCache(fm, datos) {
  try { fm.writeString(fm.joinPath(fm.documentsDirectory(), CACHE), JSON.stringify({ hora: Date.now(), datos })); } catch (e) { /* sin caché */ }
}
function haceTexto(ms) {
  const min = Math.max(0, Math.round(ms / 60000));
  return min < 1 ? "ahora" : min < 60 ? "hace " + min + " min" : "hace " + Math.round(min / 60) + " h";
}

async function cargar() {
  const fm = FileManager.local();
  const cache = leerCache(fm);
  if (cache && Date.now() - cache.hora < VALIDEZ_MS) return { datos: cache.datos, hace: null };
  try {
    const base = (await pedirDato(CLAVE_URL, "Dirección de tu zona privada", "Por ejemplo https://mi-equipo.tailnet.ts.net (sin barra final)")).replace(/\/+$/, "");
    const token = await pedirDato(CLAVE_TOKEN, "Token de Atajos", "Créalo en Seguridad → Atajos con el permiso «lectura» (empieza por mda_).");
    const r = new Request(base + "/api/atajos/hoy");
    r.headers = { Authorization: "Bearer " + token };
    r.timeoutInterval = 15;
    const datos = await r.loadJSON();
    if (!datos || typeof datos !== "object" || !Array.isArray(datos.lineas)) throw new Error("respuesta rara");
    guardarCache(fm, datos);
    return { datos, hace: null };
  } catch (e) {
    // sin red (o el equipo está apagado): lo último que hubo, diciendo de cuándo es
    if (cache) return { datos: cache.datos, hace: haceTexto(Date.now() - cache.hora) };
    throw e;
  }
}

// ---- textos cortos para los accesorios ----
function lineaCorta(j) {
  const c = j.compacto || {};
  return c.saldoTexto || j.gastoHoyTexto || "Zona privada";
}
function anilloPct(j) {
  const c = j.compacto || {};
  return typeof c.presupuestoPct === "number" ? Math.max(0, Math.min(100, c.presupuestoPct)) : null;
}

function accesorioEnLinea(w, j, hace) {
  const t = w.addText(lineaCorta(j) + (hace ? " · " + hace : ""));
  t.font = Font.systemFont(12);
}

function accesorioRectangular(w, j, hace) {
  const c = j.compacto || {};
  const a = w.addText(lineaCorta(j));
  a.font = Font.boldSystemFont(13);
  const b = w.addText(c.proximo || (c.tareasHoy ? c.tareasHoy + " tareas para hoy" : "Sin citas próximas"));
  b.font = Font.systemFont(12);
  b.lineLimit = 1;
  const pie = c.cumple || (hace ? "Datos de " + hace : null);
  if (pie) { const d = w.addText(pie); d.font = Font.systemFont(10); d.lineLimit = 1; }
}

// El anillo: un arco hecho con un DrawContext (Scriptable no tiene gráficos de anillo)
function accesorioCircular(w, j, hace) {
  const pct = anilloPct(j);
  const lado = 120;
  const ctx = new DrawContext();
  ctx.size = new Size(lado, lado);
  ctx.opaque = false;
  ctx.respectScreenScale = true;
  const centro = new Point(lado / 2, lado / 2), radio = lado / 2 - 8;
  ctx.setLineWidth(10);
  ctx.setStrokeColor(new Color("#ffffff", 0.25));
  ctx.strokeEllipse(new Rect(8, 8, lado - 16, lado - 16));
  if (pct !== null) {
    ctx.setStrokeColor(new Color("#ffffff"));
    const pasos = Math.max(1, Math.round(pct * 1.2));
    for (let i = 0; i < pasos; i++) {
      const ang = -Math.PI / 2 + (i / 120) * 2 * Math.PI * (120 / 120);
      ctx.fillEllipse(new Rect(centro.x + radio * Math.cos(ang) - 5, centro.y + radio * Math.sin(ang) - 5, 10, 10));
    }
  }
  ctx.setFont(Font.boldSystemFont(26));
  ctx.setTextColor(new Color("#ffffff"));
  ctx.setTextAlignedCenter();
  ctx.drawTextInRect(pct === null ? "—" : pct + "%", new Rect(0, lado / 2 - 16, lado, 36));
  const img = w.addImage(ctx.getImage());
  img.centerAlignImage();
  if (hace) { const t = w.addText(hace); t.font = Font.systemFont(8); t.centerAlignText(); }
}

// ---- la pantalla de inicio (como siempre) ----
function construirInicio(j, error, hace) {
  const w = new ListWidget();
  w.backgroundColor = new Color("#0b1217");
  w.setPadding(14, 14, 14, 14);
  const t = w.addText(error ? "Zona privada" : new Date().toLocaleDateString("es-ES", { weekday: "long", day: "numeric", month: "long" }) + (hace ? " · " + hace : ""));
  t.font = Font.boldSystemFont(13);
  t.textColor = new Color("#9fb4c0");
  w.addSpacer(6);
  const lineas = error ? ["No se pudo cargar: " + error] : j.lineas;
  const max = config.widgetFamily === "small" ? 4 : 7;
  for (const l of lineas.slice(0, max)) {
    const x = w.addText(l);
    x.font = Font.systemFont(config.widgetFamily === "small" ? 11 : 12);
    x.textColor = Color.white();
    x.lineLimit = 2;
    w.addSpacer(3);
  }
  return w;
}

function construir(j, error, hace) {
  const familia = config.widgetFamily;
  let w;
  if (familia === "accessoryRectangular" || familia === "accessoryCircular" || familia === "accessoryInline") {
    w = new ListWidget();
    if (error) { const t = w.addText("Zona privada: sin datos"); t.font = Font.systemFont(11); }
    else if (familia === "accessoryRectangular") accesorioRectangular(w, j, hace);
    else if (familia === "accessoryCircular") accesorioCircular(w, j, hace);
    else accesorioEnLinea(w, j, hace);
  } else w = construirInicio(j, error, hace);
  w.refreshAfterDate = new Date(Date.now() + VALIDEZ_MS);   // iOS decide cuándo, pero lo pide cada 15 minutos
  return w;
}

let widget;
try { const r = await cargar(); widget = construir(r.datos, null, r.hace); }
catch (e) { widget = construir(null, String(e.message || e), null); }

if (config.runsInWidget) Script.setWidget(widget);
else await widget.presentMedium();
Script.complete();
