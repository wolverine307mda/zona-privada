export interface EntradaNpf { focal: number; recorte: number; apertura: number; pixelUm: number; declinacion: number; }
export interface ResultadoNpf { regla500: number; npf: number; }

// Cuántos segundos puedes exponer antes de que las estrellas dejen de ser puntos.
// Regla 500: 500 / (focal × recorte). NPF (más fiel con sensores de muchos megapíxeles): (35·N + 30·p) / f, con N la apertura, p el píxel en µm y f la focal real;
// cerca del polo celeste las estrellas se mueven menos, así que se alarga dividiendo por el coseno de la declinación.
export function calcular(e: EntradaNpf): ResultadoNpf | null {
  const { focal, recorte, apertura, pixelUm, declinacion } = e;
  if (![focal, recorte, apertura, pixelUm].every((v) => Number.isFinite(v) && v > 0)) return null;
  const dec = Math.min(Math.abs(Number.isFinite(declinacion) ? declinacion : 0), 85);
  const factor = 1 / Math.cos((dec * Math.PI) / 180);
  return { regla500: (500 / (focal * recorte)) * factor, npf: ((35 * apertura + 30 * pixelUm) / focal) * factor };
}

// el paso del píxel (µm) a partir del ancho del sensor (mm) y los megapíxeles, suponiendo 3:2
export function pixelDesdeSensor(anchoMm: number, megapixeles: number): number | null {
  if (!(anchoMm > 0) || !(megapixeles > 0)) return null;
  const columnas = Math.sqrt(megapixeles * 1e6 * 1.5);
  return (anchoMm / columnas) * 1000;
}

export function texto(s: number): string { return s >= 10 ? `${Math.round(s)} s` : `${s.toFixed(1)} s`; }
