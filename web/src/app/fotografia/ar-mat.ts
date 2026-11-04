export interface Proy { x: number; y: number; visible: boolean; }

const RAD = Math.PI / 180;
// el camino corto entre dos acimuts, de -180 a 180
export const difAngulo = (a: number, b: number): number => ((((a - b) % 360) + 540) % 360) - 180;

// Dónde cae en la pantalla un punto del cielo (acimut y altura) si el móvil mira hacia `rumbo` con la cámara inclinada `elev` grados sobre el horizonte.
// `fovH` es el campo de visión horizontal de la cámara. Se supone el móvil derecho (sin girar sobre su eje).
export function proyectar(az: number, alt: number, rumbo: number, elev: number, fovH: number, ancho: number, alto: number): Proy {
  const dAz = difAngulo(az, rumbo) * RAD, a = alt * RAD, e = elev * RAD;
  const dx = Math.cos(a) * Math.sin(dAz), dy = Math.cos(a) * Math.cos(dAz), dz = Math.sin(a);
  const zc = Math.cos(e) * dy + Math.sin(e) * dz;               // hacia donde mira la cámara
  const yc = -Math.sin(e) * dy + Math.cos(e) * dz;              // hacia arriba en la pantalla
  const f = ancho / 2 / Math.tan((fovH * RAD) / 2);
  if (zc <= 0.01) return { x: 0, y: 0, visible: false };
  return { x: ancho / 2 + (f * dx) / zc, y: alto / 2 - (f * yc) / zc, visible: true };
}

// el rumbo (0 = norte) y la inclinación sobre el horizonte a partir de los ángulos del sensor; beta 90° = móvil derecho mirando al horizonte
export function rumboDe(alfa: number | null, brujulaIos: number | null): number | null {
  if (brujulaIos !== null && Number.isFinite(brujulaIos)) return (brujulaIos + 360) % 360;
  if (alfa !== null && Number.isFinite(alfa)) return (360 - alfa + 360) % 360;
  return null;
}
export const elevacionDe = (beta: number): number => Math.max(-90, Math.min(90, beta - 90));
