import { difAngulo, elevacionDe, proyectar, rumboDe } from './ar-mat';

describe('AR del sol y la luna', () => {
  const W = 400, H = 800;
  it('lo que está justo delante cae en el centro', () => {
    const p = proyectar(90, 0, 90, 0, 60, W, H);
    expect(p.visible).toBe(true);
    expect(p.x).toBeCloseTo(200, 3);
    expect(p.y).toBeCloseTo(400, 3);
  });
  it('a medio campo de visión a la derecha toca el borde, y más alto sube en la pantalla', () => {
    expect(proyectar(120, 0, 90, 0, 60, W, H).x).toBeCloseTo(400, 0);
    expect(proyectar(90, 20, 90, 0, 60, W, H).y).toBeLessThan(400);
  });
  it('lo que queda detrás no se ve y al inclinar el móvil hacia arriba el horizonte baja', () => {
    expect(proyectar(270, 0, 90, 0, 60, W, H).visible).toBe(false);
    expect(proyectar(90, 0, 90, 30, 60, W, H).y).toBeGreaterThan(400);
  });
  it('el camino corto entre acimuts cruza el norte', () => {
    expect(difAngulo(10, 350)).toBe(20);
    expect(difAngulo(350, 10)).toBe(-20);
    const p = proyectar(5, 0, 355, 0, 60, W, H);   // 10° a la derecha del rumbo
    expect(p.x).toBeGreaterThan(200);
  });
  it('saca el rumbo de la brújula de iOS o del ángulo alfa y la inclinación de beta', () => {
    expect(rumboDe(null, 120)).toBe(120);
    expect(rumboDe(90, null)).toBe(270);
    expect(rumboDe(null, null)).toBeNull();
    expect(elevacionDe(90)).toBe(0);
    expect(elevacionDe(120)).toBe(30);
  });
});
