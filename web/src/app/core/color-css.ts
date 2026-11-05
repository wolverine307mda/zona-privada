// Las escenas 3D y las gráficas (Three, ECharts) leen los colores del tema con getPropertyValue. Pero una variable como `--fondo` vale ahora una mezcla
// (`color-mix(...)`), y eso lo entienden el navegador y el CSS, no Three ni ECharts: sin esto salían en blanco, en negro o directamente sin dibujar.
// `resolverColor` le pide al navegador el color ya resuelto y lo devuelve como `#rrggbb`.

const hex2 = (x: number): string => Math.round(Math.min(255, Math.max(0, x))).toString(16).padStart(2, '0');

/** Pasa a `#rrggbb` lo que devuelve getComputedStyle: `rgb(…)`, `rgba(…)` o `color(srgb r g b)` (componentes de 0 a 1). Si no lo entiende, null. */
export function aHex(color: string): string | null {
  const c = color.trim();
  let m = c.match(/^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)/i);
  if (m) return '#' + hex2(+m[1]) + hex2(+m[2]) + hex2(+m[3]);
  m = c.match(/^color\(\s*srgb\s+([\d.e-]+)\s+([\d.e-]+)\s+([\d.e-]+)/i);
  if (m) return '#' + hex2(+m[1] * 255) + hex2(+m[2] * 255) + hex2(+m[3] * 255);
  return null;
}

/** El valor de una variable CSS (o cualquier color) como `#rrggbb`; `defecto` si está vacío o el navegador no lo entiende. */
export function resolverColor(valor: string, defecto: string): string {
  const v = valor.trim();
  if (!v) return defecto;
  if (/^#[0-9a-f]{6}$/i.test(v)) return v;
  const el = document.createElement('span');
  el.style.color = v;
  if (!el.style.color) return defecto; // un valor que el navegador no reconoce como color
  el.style.display = 'none';
  document.body.appendChild(el);
  const calculado = getComputedStyle(el).color;
  el.remove();
  return aHex(calculado) ?? defecto;
}
