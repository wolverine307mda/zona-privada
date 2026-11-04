// Geometría del control radial. Sin Angular: solo números, para poder probarla.
// Los ángulos van en grados, con 0° arriba y creciendo en sentido horario (−90° es la izquierda, 90° la derecha).

export interface Marca { x1: number; y1: number; x2: number; y2: number; activa: boolean; }

export const limitar = (v: number, min: number, max: number): number => Math.min(max, Math.max(min, v));

/** Cuántos decimales tiene un paso (0,5 → 1; 0,25 → 2; 1 → 0): así el valor no arrastra restos como 0,30000000000000004. */
export function decimalesDe(paso: number): number {
  const s = String(paso);
  const i = s.indexOf('.');
  return i < 0 ? 0 : s.length - i - 1;
}

/** Dónde cae el valor entre min y max, de 0 a 1. */
export function fraccion(valor: number, min: number, max: number): number {
  return max <= min ? 0 : limitar((valor - min) / (max - min), 0, 1);
}

/** Valor más cercano que cae en un paso exacto y dentro de [min, max]. */
export function ajustar(valor: number, min: number, max: number, paso: number): number {
  if (!(paso > 0)) return limitar(valor, min, max);
  const v = min + Math.round((valor - min) / paso) * paso;
  return limitar(+v.toFixed(decimalesDe(paso)), min, max);
}

export function valorDeFraccion(f: number, min: number, max: number, paso: number): number {
  return ajustar(min + limitar(f, 0, 1) * (max - min), min, max, paso);
}

export const anguloDeFraccion = (f: number, ini: number, fin: number): number => ini + limitar(f, 0, 1) * (fin - ini);

export function punto(cx: number, cy: number, r: number, grados: number): { x: number; y: number } {
  const a = (grados * Math.PI) / 180;
  return { x: cx + r * Math.sin(a), y: cy - r * Math.cos(a) };
}

/** Las marcas finas del arco: `n` rayitas radiales; las que quedan hasta la fracción `f` son las activas. */
export function marcasDelArco(n: number, ini: number, fin: number, cx: number, cy: number, rInt: number, rExt: number, f: number): Marca[] {
  const total = Math.max(2, Math.floor(n));
  return Array.from({ length: total }, (_, i) => {
    const t = i / (total - 1);
    const ang = anguloDeFraccion(t, ini, fin);
    const a = punto(cx, cy, rInt, ang);
    const b = punto(cx, cy, rExt, ang);
    return { x1: a.x, y1: a.y, x2: b.x, y2: b.y, activa: t <= f + 1e-9 };
  });
}

/** Trazo SVG (atributo d) de un tramo del arco entre dos ángulos, para el resplandor bajo las marcas activas. */
export function trazoArco(cx: number, cy: number, r: number, desde: number, hasta: number): string {
  const a = punto(cx, cy, r, desde);
  const b = punto(cx, cy, r, hasta);
  const grande = Math.abs(hasta - desde) > 180 ? 1 : 0;
  const n = (v: number) => +v.toFixed(2);
  return `M ${n(a.x)} ${n(a.y)} A ${n(r)} ${n(r)} 0 ${grande} 1 ${n(b.x)} ${n(b.y)}`;
}

const distanciaAngular = (a: number, b: number): number => Math.abs((((a - b) % 360) + 540) % 360 - 180);

/** Fracción (0–1) que corresponde a un punto tocado: se calcula por el ángulo respecto al centro; fuera del arco se queda en el extremo más cercano. */
export function fraccionDePunto(x: number, y: number, cx: number, cy: number, ini: number, fin: number): number {
  const ang = (Math.atan2(x - cx, -(y - cy)) * 180) / Math.PI;
  if (ang >= ini && ang <= fin) return (ang - ini) / (fin - ini);
  return distanciaAngular(ang, ini) <= distanciaAngular(ang, fin) ? 0 : 1;
}
