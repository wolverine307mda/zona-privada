// Lógica pura de la tarjeta de métrica (se prueba en barras.spec.ts). Sin Angular.

/** Alturas en % (de 0 a 100) para una mini gráfica de barras: la mayor llena el alto y ninguna baja de `minimo` para que se vea. */
export function alturas(valores: readonly number[], minimo = 6): number[] {
  const max = Math.max(0, ...valores);
  return valores.map((v) => (max <= 0 ? minimo : Math.max(minimo, (Math.max(0, v) / max) * 100)));
}

/** Posiciones de las `cuantas` barras más altas (las que se pintan con rayado); en empate gana la primera. */
export function destacadas(valores: readonly number[], cuantas = 2): number[] {
  return valores
    .map((v, i) => ({ v, i }))
    .sort((a, b) => b.v - a.v || a.i - b.i)
    .slice(0, Math.max(0, cuantas))
    .map((x) => x.i)
    .sort((a, b) => a - b);
}

/** «−18 %» / «+4,5 %» / «0 %», con el signo menos de verdad (U+2212) y coma decimal. */
export function textoVariacion(pct: number, decimales = 0, idioma = 'es-ES'): string {
  const n = Math.abs(pct).toLocaleString(idioma, { minimumFractionDigits: decimales, maximumFractionDigits: decimales });
  const redondeado = Number(Math.abs(pct).toFixed(decimales));
  return `${redondeado === 0 ? '' : pct < 0 ? '−' : '+'}${n} %`;
}

export type Sentido = 'baja' | 'sube' | 'igual';

export function sentido(pct: number, decimales = 0): Sentido {
  const r = Number(pct.toFixed(decimales));
  return r === 0 ? 'igual' : r < 0 ? 'baja' : 'sube';
}
