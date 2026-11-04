import { sobreEsfera, tamanoPunto } from './globo-sondas';

describe('globo de sondas', () => {
  it('el punto 0,0 queda de cara a la cámara y los polos arriba y abajo', () => {
    const [x, y, z] = sobreEsfera(0, 0, 2);
    expect([x, y, z].map((v) => Math.round(v * 1000) / 1000)).toEqual([0, 0, 2]);
    expect(sobreEsfera(90, 0, 1)[1]).toBeCloseTo(1, 6);
    expect(sobreEsfera(-90, 0, 1)[1]).toBeCloseTo(-1, 6);
  });
  it('todos los puntos están a la misma distancia del centro', () => {
    for (const [la, lo] of [[40.4, -3.7], [-33.9, 151.2], [52.5, 13.4]]) {
      const [x, y, z] = sobreEsfera(la, lo, 1.5);
      expect(Math.hypot(x, y, z)).toBeCloseTo(1.5, 6);
    }
  });
  it('el tamaño crece con las sondas pero tiene tope', () => {
    expect(tamanoPunto(1, 100)).toBeLessThan(tamanoPunto(100, 100));
    expect(tamanoPunto(100, 100)).toBeCloseTo(0.085, 6);
    expect(tamanoPunto(0, 0)).toBeGreaterThan(0);
  });
});
