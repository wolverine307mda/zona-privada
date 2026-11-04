import { corteFiltro, frecuenciaBase, ganancia } from './sonido.service';

describe('sonificación', () => {
  it('la nota sube una octava con la carga y se queda en su rango', () => {
    expect(frecuenciaBase(0)).toBeCloseTo(110, 5);
    expect(frecuenciaBase(100)).toBeCloseTo(220, 5);
    expect(frecuenciaBase(50)).toBeGreaterThan(110);
    expect(frecuenciaBase(-20)).toBeCloseTo(110, 5);
    expect(frecuenciaBase(500)).toBeCloseTo(220, 5);
  });
  it('el filtro se abre con la carga', () => {
    expect(corteFiltro(0)).toBe(300);
    expect(corteFiltro(100)).toBe(2100);
  });
  it('el volumen es suave y nunca pasa de un tope bajo', () => {
    expect(ganancia(0)).toBe(0);
    expect(ganancia(100)).toBeCloseTo(0.12, 5);
    expect(ganancia(50)).toBeLessThan(ganancia(100) / 2);
    expect(ganancia(900)).toBeCloseTo(0.12, 5);
  });
});
