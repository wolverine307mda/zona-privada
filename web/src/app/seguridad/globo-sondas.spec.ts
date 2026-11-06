import { arcoGrande, sobreEsfera, tamanoPunto } from './globo-sondas';

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

  it('el arco va de un sitio a otro, sube en medio y nunca se hunde en la esfera', () => {
    const arco = arcoGrande(55.75, 37.62, 40.4, -3.7, 40, 1.5, 0.3);
    expect(arco.length).toBe(41);
    const a = sobreEsfera(55.75, 37.62, 1.5), b = sobreEsfera(40.4, -3.7, 1.5);
    arco[0].forEach((v, i) => expect(v).toBeCloseTo(a[i], 6));
    arco[40].forEach((v, i) => expect(v).toBeCloseTo(b[i], 6));
    const radios = arco.map((p) => Math.hypot(...p));
    expect(Math.min(...radios)).toBeGreaterThan(1.499);
    expect(radios[20]).toBeCloseTo(1.5 * 1.3, 2);
  });
  it('un arco entre dos puntos iguales no revienta', () => {
    const arco = arcoGrande(10, 10, 10, 10, 8, 1.5, 0.3);
    expect(arco.every((p) => p.every(Number.isFinite))).toBe(true);
  });
});
