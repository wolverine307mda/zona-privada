import { lineaActual } from './mini-musica';

describe('lineaActual (letra sincronizada)', () => {
  const l = [{ ms: 10000, texto: 'A' }, { ms: 20000, texto: 'B' }, { ms: 30000, texto: 'C' }];

  it('es -1 antes de la primera línea y la última que ha empezado después', () => {
    expect(lineaActual(l, 0)).toBe(-1);
    expect(lineaActual(l, 9999)).toBe(-1);
    expect(lineaActual(l, 10000)).toBe(0);
    expect(lineaActual(l, 19999)).toBe(0);
    expect(lineaActual(l, 20000)).toBe(1);
    expect(lineaActual(l, 999999)).toBe(2);
  });

  it('con una letra vacía no hay línea', () => {
    expect(lineaActual([], 5000)).toBe(-1);
  });
});
