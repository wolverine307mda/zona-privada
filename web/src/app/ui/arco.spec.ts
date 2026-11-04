import { ajustar, decimalesDe, fraccion, fraccionDePunto, marcasDelArco, punto, trazoArco, valorDeFraccion } from './arco';

describe('geometría del arco', () => {
  it('la fracción queda entre 0 y 1 y un rango vacío da 0', () => {
    expect(fraccion(5, 0, 10)).toBe(0.5);
    expect(fraccion(-3, 0, 10)).toBe(0);
    expect(fraccion(99, 0, 10)).toBe(1);
    expect(fraccion(5, 10, 10)).toBe(0);
  });

  it('ajusta al paso más cercano y respeta los límites', () => {
    expect(ajustar(22.26, 16, 30, 0.5)).toBe(22.5);
    expect(ajustar(22.2, 16, 30, 0.5)).toBe(22);
    expect(ajustar(31, 16, 30, 0.5)).toBe(30);
    expect(ajustar(10, 16, 30, 0.5)).toBe(16);
    expect(ajustar(0.30000000000000004, 0, 1, 0.1)).toBe(0.3); // sin restos de coma flotante
    expect(ajustar(7.3, 0, 10, 0)).toBe(7.3); // sin paso, solo se limita
  });

  it('cuenta los decimales del paso', () => {
    expect(decimalesDe(1)).toBe(0);
    expect(decimalesDe(0.5)).toBe(1);
    expect(decimalesDe(0.25)).toBe(2);
  });

  it('convierte una fracción en valor con su paso', () => {
    expect(valorDeFraccion(0.5, 16, 30, 0.5)).toBe(23);
    expect(valorDeFraccion(2, 16, 30, 0.5)).toBe(30);
  });

  it('coloca los puntos con 0° arriba y en sentido horario', () => {
    const arriba = punto(100, 100, 50, 0);
    const derecha = punto(100, 100, 50, 90);
    const abajo = punto(100, 100, 50, 180);
    const izquierda = punto(100, 100, 50, -90);
    expect([arriba.x, arriba.y]).toEqual([100, 50]);
    expect(derecha.x).toBeCloseTo(150);
    expect(derecha.y).toBeCloseTo(100);
    expect(abajo.x).toBeCloseTo(100);
    expect(abajo.y).toBeCloseTo(150);
    expect(izquierda.x).toBeCloseTo(50);
  });

  it('reparte las marcas y activa las que quedan hasta el valor', () => {
    const m = marcasDelArco(5, -90, 90, 0, 0, 8, 10, 0.5);
    expect(m).toHaveLength(5);
    expect(m.map((x) => x.activa)).toEqual([true, true, true, false, false]);
    expect(m[0].x2).toBeCloseTo(-10); // la primera, en el extremo izquierdo
    expect(m[4].x2).toBeCloseTo(10);
    expect(marcasDelArco(1, -90, 90, 0, 0, 8, 10, 0)).toHaveLength(2); // nunca menos de dos
  });

  it('dibuja el tramo del arco para el resplandor', () => {
    expect(trazoArco(0, 0, 10, -90, 0)).toBe('M -10 0 A 10 10 0 0 1 0 -10');
    expect(trazoArco(0, 0, 10, -120, 120)).toContain(' 0 1 1 '); // más de media vuelta: arco grande
  });

  it('pasa de un punto tocado a su fracción, y fuera del arco se queda en el extremo cercano', () => {
    const en = (grados: number) => { const p = punto(0, 0, 10, grados); return fraccionDePunto(p.x, p.y, 0, 0, -110, 110); };
    expect(en(0)).toBeCloseTo(0.5);
    expect(en(-110)).toBeCloseTo(0);
    expect(en(110)).toBeCloseTo(1);
    expect(en(55)).toBeCloseTo(0.75);
    expect(en(170)).toBe(1); // por debajo, hacia la derecha → máximo
    expect(en(-170)).toBe(0); // por debajo, hacia la izquierda → mínimo
  });
});
