// Dónde están el Sol y la Luna y cómo está la Luna, con las fórmulas aproximadas de siempre (el método de SunCalc, sin dependencias ni red).
// Sirven para dibujar el cielo de verdad en la pantalla de entrar: la precisión es de unos minutos de arco para el Sol y de medio grado para la Luna, de sobra para un fondo.
const RAD = Math.PI / 180;
const DIA_MS = 86_400_000;
const J1970 = 2_440_588;
const J2000 = 2_451_545;
const OBLICUIDAD = RAD * 23.4397;

const aDias = (f: Date): number => f.valueOf() / DIA_MS - 0.5 + J1970 - J2000;
const ascensionRecta = (l: number, b: number) => Math.atan2(Math.sin(l) * Math.cos(OBLICUIDAD) - Math.tan(b) * Math.sin(OBLICUIDAD), Math.cos(l));
const declinacion = (l: number, b: number) => Math.asin(Math.sin(b) * Math.cos(OBLICUIDAD) + Math.cos(b) * Math.sin(OBLICUIDAD) * Math.sin(l));
const azimutDe = (h: number, fi: number, dec: number) => Math.atan2(Math.sin(h), Math.cos(h) * Math.sin(fi) - Math.tan(dec) * Math.cos(fi));
const alturaDe = (h: number, fi: number, dec: number) => Math.asin(Math.sin(fi) * Math.sin(dec) + Math.cos(fi) * Math.cos(dec) * Math.cos(h));
const tiempoSidereo = (d: number, lw: number) => RAD * (280.16 + 360.9856235 * d) - lw;
// la atmósfera dobla la luz: cerca del horizonte los astros se ven algo más altos de lo que están
const refraccion = (h: number) => 0.0002967 / Math.tan(Math.max(h, 0) + 0.00312536 / (Math.max(h, 0) + 0.08901179));

function coordenadasSol(d: number) {
  const m = RAD * (357.5291 + 0.98560028 * d);
  const c = RAD * (1.9148 * Math.sin(m) + 0.02 * Math.sin(2 * m) + 0.0003 * Math.sin(3 * m));
  const l = m + c + RAD * 102.9372 + Math.PI;
  return { dec: declinacion(l, 0), ra: ascensionRecta(l, 0) };
}

function coordenadasLuna(d: number) {
  const l = RAD * (218.316 + 13.176396 * d);
  const m = RAD * (134.963 + 13.064993 * d);
  const f = RAD * (93.272 + 13.22935 * d);
  const lon = l + RAD * 6.289 * Math.sin(m);
  const lat = RAD * 5.128 * Math.sin(f);
  return { ra: ascensionRecta(lon, lat), dec: declinacion(lon, lat), dist: 385_001 - 20_905 * Math.cos(m) };
}

export interface PosicionAstro {
  /** Radianes desde el sur, positivos hacia el oeste: −π/2 es el este y π/2 el oeste. */
  azimut: number;
  /** Radianes sobre el horizonte (negativo = bajo el horizonte). */
  altura: number;
}

export function posicionSol(fecha: Date, lat: number, lon: number): PosicionAstro {
  const d = aDias(fecha), lw = RAD * -lon, fi = RAD * lat, c = coordenadasSol(d);
  const h = tiempoSidereo(d, lw) - c.ra;
  return { azimut: azimutDe(h, fi, c.dec), altura: alturaDe(h, fi, c.dec) };
}

export function posicionLuna(fecha: Date, lat: number, lon: number): PosicionAstro {
  const d = aDias(fecha), lw = RAD * -lon, fi = RAD * lat, c = coordenadasLuna(d);
  const h = tiempoSidereo(d, lw) - c.ra;
  const altura = alturaDe(h, fi, c.dec);
  return { azimut: azimutDe(h, fi, c.dec), altura: altura + refraccion(altura) };
}

export interface FaseLuna {
  /** Parte del disco iluminada, de 0 (nueva) a 1 (llena). */
  fraccion: number;
  /** Ciclo: 0 nueva · 0,25 cuarto creciente · 0,5 llena · 0,75 cuarto menguante. */
  fase: number;
}

export function faseLuna(fecha: Date): FaseLuna {
  const d = aDias(fecha), s = coordenadasSol(d), m = coordenadasLuna(d);
  const distSol = 149_598_000;
  const fi = Math.acos(Math.sin(s.dec) * Math.sin(m.dec) + Math.cos(s.dec) * Math.cos(m.dec) * Math.cos(s.ra - m.ra));
  const inc = Math.atan2(distSol * Math.sin(fi), m.dist - distSol * Math.cos(fi));
  const angulo = Math.atan2(Math.cos(s.dec) * Math.sin(s.ra - m.ra), Math.sin(s.dec) * Math.cos(m.dec) - Math.cos(s.dec) * Math.sin(m.dec) * Math.cos(s.ra - m.ra));
  return { fraccion: (1 + Math.cos(inc)) / 2, fase: 0.5 + (0.5 * inc * (angulo < 0 ? -1 : 1)) / Math.PI };
}

export function nombreFase(fase: number): string {
  const f = ((fase % 1) + 1) % 1;
  if (f < 0.03 || f > 0.97) return 'Luna nueva';
  if (f < 0.22) return 'Luna creciente';
  if (f < 0.28) return 'Cuarto creciente';
  if (f < 0.47) return 'Gibosa creciente';
  if (f < 0.53) return 'Luna llena';
  if (f < 0.72) return 'Gibosa menguante';
  if (f < 0.78) return 'Cuarto menguante';
  return 'Luna menguante';
}

/** Qué momento del día es según la altura del Sol (en grados). */
export function momentoDelDia(alturaSolGrados: number): 'noche' | 'crepusculo' | 'amanecer-atardecer' | 'dia' {
  if (alturaSolGrados < -12) return 'noche';
  if (alturaSolGrados < -3) return 'crepusculo';
  if (alturaSolGrados < 8) return 'amanecer-atardecer';
  return 'dia';
}
