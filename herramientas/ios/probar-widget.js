// Prueba del widget en Node (sin iPhone): se le dan unos Scriptable de mentira y datos falsos y se comprueba que cada familia de widget se construye sin fallar,
// que la caché de 15 minutos funciona y que sin conexión se enseña lo último con su antigüedad. Uso:  node herramientas/ios/probar-widget.js
const fs = require("fs");
const path = require("path");
const assert = require("assert");

const codigo = fs.readFileSync(path.join(__dirname, "hoy-widget.js"), "utf8");
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;

const DATOS = {
  fecha: "2026-11-07", gastoHoy: 12.5, gastoHoyTexto: "12,50 €", presupuestos: [], agenda: "Hoy tienes: 📅 12:00 Cita", habitos: { total: 2, hechos: 1, pendientes: [{ id: 1, nombre: "Leer" }] },
  lineas: ["Gastado hoy: 12,50 €", "Hoy tienes: 📅 12:00 Cita", "Hábitos: 1 de 2"],
  compacto: { saldoTexto: "Disponible 523 €", proximo: "12:00 Cita", presupuestoPct: 62, tareasHoy: 2, cumple: "Cumple hoy: María", entrenoHoy: false },
};

// los Scriptable de mentira: cada clase apunta lo que se le pide para poder mirarlo después
function entorno({ familia, runsInWidget = true, respuesta, ficheros = {}, ahora = Date.now() }) {
  const escrito = {}; const peticiones = []; const salida = { widget: null, textos: [], imagenes: 0 };
  class Color { constructor(h, a) { this.h = h; this.a = a; } static white() { return new Color("#fff"); } }
  const Font = { boldSystemFont: (n) => ({ n }), systemFont: (n) => ({ n }) };
  class Text { constructor(t) { this.t = t; salida.textos.push(t); } centerAlignText() {} }
  class ListWidget {
    constructor() { salida.widget = this; this.hijos = []; }
    addText(t) { const x = new Text(t); this.hijos.push(x); return x; }
    addSpacer() {} setPadding() {} async presentMedium() {}
    addImage() { salida.imagenes++; return { centerAlignImage() {} }; }
  }
  class DrawContext { setLineWidth() {} setStrokeColor() {} strokeEllipse() {} fillEllipse() {} setFont() {} setTextColor() {} setTextAlignedCenter() {} drawTextInRect() {} getImage() { return {}; } }
  class Rect { constructor(x, y, w, h) { Object.assign(this, { x, y, w, h }); } }
  class Point { constructor(x, y) { this.x = x; this.y = y; } }
  class Size { constructor(w, h) { this.w = w; this.h = h; } }
  const Keychain = { contains: () => true, get: (k) => (k === "mda-url" ? "https://casa.example.ts.net/" : "mda_token_falso"), set() {} };
  class Request {
    constructor(url) { this.url = url; peticiones.push(this); }
    async loadJSON() { if (respuesta instanceof Error) throw respuesta; return respuesta; }
  }
  const FileManager = { local: () => ({
    documentsDirectory: () => "/docs", joinPath: (a, b) => a + "/" + b,
    fileExists: (r) => r in ficheros || r in escrito, readString: (r) => (r in escrito ? escrito[r] : ficheros[r]), writeString: (r, v) => { escrito[r] = v; },
  }) };
  const Script = { setWidget() {}, complete() {} };
  const config = { widgetFamily: familia, runsInWidget };
  const Alert = class {};
  return { salida, escrito, peticiones, globales: { Color, Font, ListWidget, DrawContext, Rect, Point, Size, Keychain, Request, FileManager, Script, config, Alert, Date: class extends Date { constructor(...a) { super(...(a.length ? a : [ahora])); } static now() { return ahora; } }, console } };
}

async function ejecutar(opciones) {
  const e = entorno(opciones);
  const nombres = Object.keys(e.globales);
  await new AsyncFunction(...nombres, codigo)(...nombres.map((n) => e.globales[n]));
  return e;
}

(async () => {
  // cada familia se construye sin fallar y con lo que le toca
  for (const familia of ["small", "medium", "large", "accessoryRectangular", "accessoryCircular", "accessoryInline", undefined]) {
    const e = await ejecutar({ familia, respuesta: DATOS, runsInWidget: familia !== undefined });
    assert.ok(e.salida.widget, "sin widget en " + familia);
    assert.ok(e.salida.textos.length + e.salida.imagenes > 0, "widget vacío en " + familia);
    if (familia === "accessoryInline") assert.ok(e.salida.textos[0].includes("Disponible 523 €"), "la línea no dice el saldo");
    if (familia === "accessoryRectangular") { assert.ok(e.salida.textos.includes("Disponible 523 €")); assert.ok(e.salida.textos.includes("12:00 Cita")); assert.ok(e.salida.textos.some((t) => t.includes("María"))); }
    if (familia === "accessoryCircular") assert.strictEqual(e.salida.imagenes, 1, "el anillo no se dibujó");
    if (familia === "medium") assert.ok(e.salida.textos.includes("Gastado hoy: 12,50 €"));
    console.log("ok  " + (familia ?? "dentro de Scriptable"));
  }

  // un servidor viejo (sin bloque «compacto») no rompe los accesorios
  const viejo = { ...DATOS }; delete viejo.compacto;
  for (const familia of ["accessoryRectangular", "accessoryCircular", "accessoryInline"]) assert.ok((await ejecutar({ familia, respuesta: viejo })).salida.widget);
  assert.ok((await ejecutar({ familia: "accessoryInline", respuesta: viejo })).salida.textos[0].includes("12,50 €"));
  console.log("ok  servidor antiguo");

  // caché: con una respuesta de hace 5 minutos no se pide nada; con una de hace 40, sí
  const ahora = 1_800_000_000_000;
  const rutaCache = "/docs/mda-hoy-cache.json";
  const reciente = await ejecutar({ familia: "medium", respuesta: new Error("no debería llamarse"), ficheros: { [rutaCache]: JSON.stringify({ hora: ahora - 5 * 60000, datos: DATOS }) }, ahora });
  assert.strictEqual(reciente.peticiones.length, 0); assert.ok(reciente.salida.textos.includes("Gastado hoy: 12,50 €"));
  const vieja = await ejecutar({ familia: "medium", respuesta: DATOS, ficheros: { [rutaCache]: JSON.stringify({ hora: ahora - 40 * 60000, datos: { ...DATOS, lineas: ["viejo"] } }) }, ahora });
  assert.strictEqual(vieja.peticiones.length, 1); assert.ok(vieja.peticiones[0].url.endsWith("/api/atajos/hoy") && !vieja.peticiones[0].url.includes("//api"));
  assert.ok(vieja.escrito[rutaCache], "no guardó la caché nueva");
  console.log("ok  caché de 15 minutos");

  // sin conexión: lo último con «hace 40 min»; sin nada que enseñar, un mensaje y no un fallo
  const sinRed = await ejecutar({ familia: "accessoryInline", respuesta: new Error("Sin conexión"), ficheros: { [rutaCache]: JSON.stringify({ hora: ahora - 40 * 60000, datos: DATOS }) }, ahora });
  assert.ok(sinRed.salida.textos[0].includes("hace 40 min"), sinRed.salida.textos[0]);
  const grande = await ejecutar({ familia: "large", respuesta: new Error("Sin conexión"), ficheros: { [rutaCache]: JSON.stringify({ hora: ahora - 40 * 60000, datos: DATOS }) }, ahora });
  assert.ok(grande.salida.textos[0].includes("hace 40 min"));
  const nada = await ejecutar({ familia: "medium", respuesta: new Error("Sin conexión"), ahora });
  assert.ok(nada.salida.textos.some((t) => t.includes("No se pudo cargar")));
  const nadaCirc = await ejecutar({ familia: "accessoryRectangular", respuesta: new Error("Sin conexión"), ahora });
  assert.ok(nadaCirc.salida.textos.some((t) => t.includes("sin datos")));
  const rara = await ejecutar({ familia: "medium", respuesta: { cosa: 1 }, ahora });
  assert.ok(rara.salida.textos.some((t) => t.includes("No se pudo cargar")));
  console.log("ok  sin conexión y respuestas raras");
  console.log("\nWidget: todo bien.");
})().catch((e) => { console.error("FALLÓ:", e.message); process.exit(1); });
