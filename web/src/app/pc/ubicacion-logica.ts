// La parte de «Ubicación del iPhone» que no necesita pantalla ni red: leer coordenadas pegadas, distancias y tiempos.

export interface Punto { lat: number; lon: number; }

export const MODOS = [
  { id: 'andar', texto: 'Andando', icono: '🚶', kmh: 5 },
  { id: 'bici', texto: 'En bici', icono: '🚲', kmh: 15 },
  { id: 'coche', texto: 'En coche', icono: '🚗', kmh: 50 },
] as const;
export type Modo = (typeof MODOS)[number]['id'];

// Cuánto tiempo se mantiene (luego vuelve la ubicación real sola)
export const DURACIONES = [
  { min: 30, texto: '30 min' }, { min: 60, texto: '1 h' }, { min: 120, texto: '2 h' }, { min: 360, texto: '6 h' }, { min: 720, texto: '12 h' },
] as const;

export const CIUDADES: { nombre: string; lat: number; lon: number }[] = [
  { nombre: 'Madrid', lat: 40.41683, lon: -3.70379 },
  { nombre: 'Barcelona', lat: 41.38741, lon: 2.16864 },
  { nombre: 'París · Torre Eiffel', lat: 48.85837, lon: 2.29448 },
  { nombre: 'Londres · Big Ben', lat: 51.50073, lon: -0.12462 },
  { nombre: 'Roma · Coliseo', lat: 41.89021, lon: 12.49223 },
  { nombre: 'Nueva York · Times Square', lat: 40.75798, lon: -73.98554 },
  { nombre: 'Tokio · Shibuya', lat: 35.65951, lon: 139.70053 },
  { nombre: 'Sídney · Ópera', lat: -33.85679, lon: 151.21525 },
  { nombre: 'Ciudad de México · Zócalo', lat: 19.43261, lon: -99.13321 },
  { nombre: 'Buenos Aires · Obelisco', lat: -34.60371, lon: -58.38156 },
];

export function coordenadasValidas(lat: number, lon: number): boolean {
  return Number.isFinite(lat) && Number.isFinite(lon) && lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;
}

const NUM = '(-?\\d{1,3}(?:\\.\\d+)?)';

// Lo que se puede pegar: «40.4168, -3.7038», «40.4168 -3.7038», «40.4168;-3.7038», y enlaces de Google Maps (@lat,lon · !3dlat!4dlon · ?q=lat,lon · ll=lat,lon)
// o de Apple Maps (ll=lat,lon). Devuelve null si no hay un punto válido.
export function leerCoordenadas(texto: string): Punto | null {
  const t = (texto ?? '').trim();
  if (!t || t.length > 600) return null;
  const candidatos: RegExp[] = [
    new RegExp(`!3d${NUM}!4d${NUM}`),
    new RegExp(`@${NUM},${NUM}`),
    new RegExp(`[?&](?:q|ll|query|center|sll)=${NUM}(?:,|%2C)${NUM}`),
    new RegExp(`^${NUM}\\s*[,;]?\\s+${NUM}$`),
    new RegExp(`^${NUM}\\s*[,;]\\s*${NUM}$`),
  ];
  for (const re of candidatos) {
    const m = re.exec(t);
    if (!m) continue;
    const lat = Number(m[1]), lon = Number(m[2]);
    if (coordenadasValidas(lat, lon)) return { lat, lon };
  }
  return null;
}

export function formato(n: number): string { return (Math.round(n * 1e5) / 1e5).toFixed(5).replace(/\.?0+$/, ''); }
export function texto(p: Punto): string { return `${formato(p.lat)}, ${formato(p.lon)}`; }

// Distancia en metros (haversine)
export function distancia(a: Punto, b: Punto): number {
  const r = (g: number) => (g * Math.PI) / 180;
  const dLat = r(b.lat - a.lat), dLon = r(b.lon - a.lon);
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(r(a.lat)) * Math.cos(r(b.lat)) * Math.sin(dLon / 2) ** 2;
  return 2 * 6_371_000 * Math.asin(Math.min(1, Math.sqrt(h)));
}

export function segundos(metros: number, modo: Modo): number {
  const kmh = MODOS.find((m) => m.id === modo)!.kmh;
  return Math.max(1, Math.ceil(metros / (kmh / 3.6)));
}

export function duracion(s: number): string {
  if (s >= 3600) { const h = Math.floor(s / 3600), m = Math.round((s % 3600) / 60); return m ? `${h} h ${m} min` : `${h} h`; }
  if (s >= 60) return `${Math.round(s / 60)} min`;
  return `${s} s`;
}

export function kilometros(metros: number): string {
  return metros < 1000 ? `${Math.round(metros)} m` : `${(metros / 1000).toFixed(metros < 10_000 ? 1 : 0)} km`;
}

// Cuánto queda hasta la hora de fin, en palabras («1 h 20 min», «45 min», «menos de 1 min»)
export function queda(hasta: Date, ahora: Date): string {
  const s = Math.floor((hasta.getTime() - ahora.getTime()) / 1000);
  if (s <= 0) return 'terminando';
  if (s < 60) return 'menos de 1 min';
  return duracion(Math.round(s / 60) * 60);
}

// Dónde está ahora lo simulado: si va de camino, por el tiempo que lleva (línea recta, igual que el agente)
export function posicionAhora(s: { lat: number; lon: number; desde: string; destinoLat: number | null; destinoLon: number | null; segundos: number }, ahora: number): Punto {
  if (s.destinoLat == null || s.destinoLon == null || s.segundos <= 0) return { lat: s.lat, lon: s.lon };
  const f = Math.min(1, Math.max(0, (ahora - new Date(s.desde).getTime()) / 1000 / s.segundos));
  let dLon = s.destinoLon - s.lon;
  if (dLon > 180) dLon -= 360; else if (dLon < -180) dLon += 360;
  let lon = s.lon + dLon * f;
  if (lon > 180) lon -= 360; else if (lon < -180) lon += 360;
  return { lat: s.lat + (s.destinoLat - s.lat) * f, lon };
}
