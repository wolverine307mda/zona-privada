import { formatear, interpolar, suavizar } from './numero';

describe('número animado', () => {
  it('la curva empieza en 0, acaba en 1 y se queda dentro', () => {
    expect(suavizar(0)).toBe(0);
    expect(suavizar(1)).toBe(1);
    expect(suavizar(-1)).toBe(0);
    expect(suavizar(7)).toBe(1);
    expect(suavizar(0.5)).toBeGreaterThan(0.5); // sale rápido y frena al llegar
  });

  it('interpola entre dos valores, también hacia abajo', () => {
    expect(interpolar(10, 20, 0)).toBe(10);
    expect(interpolar(10, 20, 1)).toBe(20);
    expect(interpolar(20, 10, 1)).toBe(10);
    expect(interpolar(10, 20, 0.5)).toBeGreaterThan(15);
  });

  it('formatea con coma decimal y los decimales pedidos', () => {
    expect(formatear(22.5, 1)).toBe('22,5');
    expect(formatear(22, 1)).toBe('22,0');
    expect(formatear(12.44, 0)).toBe('12');
    expect(formatear(0, 2)).toBe('0,00');
  });
});
