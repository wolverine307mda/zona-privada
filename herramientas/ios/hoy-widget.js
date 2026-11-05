// Widget «Hoy» de la zona privada para el iPhone, con Scriptable (app gratuita de la App Store).
// Enseña lo mismo que la pantalla «Hoy»: gasto de hoy, presupuestos que aprietan, agenda, hábitos pendientes y la mejor salida de la semana.
// Cómo ponerlo: mira LEEME.md. La dirección y el token se guardan en el llavero del iPhone la primera vez (no quedan escritos en este archivo).

const CLAVE_URL = "mda-url";
const CLAVE_TOKEN = "mda-token";

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

async function cargar() {
  const base = (await pedirDato(CLAVE_URL, "Dirección de tu zona privada", "Por ejemplo https://mi-equipo.tailnet.ts.net (sin barra final)")).replace(/\/+$/, "");
  const token = await pedirDato(CLAVE_TOKEN, "Token de Atajos", "Créalo en Seguridad → Atajos con el permiso «lectura» (empieza por mda_).");
  const r = new Request(base + "/api/atajos/hoy");
  r.headers = { Authorization: "Bearer " + token };
  r.timeoutInterval = 15;
  return await r.loadJSON();
}

function construir(j, error) {
  const w = new ListWidget();
  w.backgroundColor = new Color("#0b1217");
  w.setPadding(14, 14, 14, 14);
  const t = w.addText(error ? "Zona privada" : new Date().toLocaleDateString("es-ES", { weekday: "long", day: "numeric", month: "long" }));
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
  w.refreshAfterDate = new Date(Date.now() + 15 * 60 * 1000);   // iOS decide cuándo, pero lo pide cada 15 minutos
  return w;
}

let widget;
try { widget = construir(await cargar(), null); }
catch (e) { widget = construir(null, String(e.message || e)); }

if (config.runsInWidget) Script.setWidget(widget);
else await widget.presentMedium();
Script.complete();
