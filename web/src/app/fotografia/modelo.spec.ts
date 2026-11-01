import { Punto, cardinal, colorCielo, creciente, hhmm, instante, pathFaseLuna } from './modelo';

const punto = (min: number, alt: number, az: number, total = 0): Punto => ({
  min, etapa: alt > 6 ? 'dia' : 'noche', sol: { alt, az }, luna: { alt: -10, az: 10 }, nucleo: { alt: 0, az: 120 }, sombra: alt > 1 ? 1 / Math.tan((alt * Math.PI) / 180) : null,
  nubes: { total, baja: 0, media: 0, alta: 0, lluvia: 0, vis: 20, viento: 5 }, nota: 5,
});

describe('modelo de la pantalla de fotografía', () => {
  it('interpola entre dos puntos de 10 minutos', () => {
    const s = [punto(0, 10, 100, 0), punto(10, 20, 110, 100), punto(20, 30, 120, 100)];
    const i = instante(s, 5);
    expect(i.sol.alt).toBeCloseTo(15);
    expect(i.sol.az).toBeCloseTo(105);
    expect(i.nubes!.total).toBeCloseTo(50);
  });

  it('el acimut toma el camino corto al pasar por el norte', () => {
    const s = [punto(0, 10, 350), punto(10, 10, 10), punto(20, 10, 10)];
    const az = instante(s, 5).sol.az;
    expect(az === 0 || az < 1 || az > 359).toBe(true);
  });

  it('no se sale de la serie por los extremos', () => {
    const s = [punto(0, 1, 1), punto(10, 2, 2), punto(20, 3, 3)];
    expect(instante(s, -50).sol.alt).toBeCloseTo(1);
    expect(instante(s, 9999).sol.alt).toBeCloseTo(3);
  });

  it('sin sombra mientras el sol está bajo el horizonte', () => {
    const s = [punto(0, -5, 10), punto(10, -4, 11), punto(20, -3, 12)];
    expect(instante(s, 5).sombra).toBeNull();
  });

  it('puntos cardinales y horas', () => {
    expect(cardinal(0)).toBe('N');
    expect(cardinal(90)).toBe('E');
    expect(cardinal(180)).toBe('S');
    expect(cardinal(271)).toBe('O');
    expect(cardinal(359)).toBe('N');
    expect(hhmm(0)).toBe('00:00');
    expect(hhmm(1065)).toBe('17:45');
    expect(hhmm(1440)).toBe('00:00');
  });

  it('el cielo es oscuro de noche y azul de día, y la fase crece o mengua', () => {
    const noche = colorCielo(-30), dia = colorCielo(60);
    expect(parseInt(noche.arriba.slice(1, 3), 16)).toBeLessThan(10);
    expect(parseInt(dia.arriba.slice(5, 7), 16)).toBeGreaterThan(180);
    expect(creciente('cuarto creciente')).toBe(true);
    expect(creciente('gibosa menguante')).toBe(false);
    expect(creciente('luna llena')).toBe(false);
  });

  it('la fase llena la mitad con 50 % y deja un terminador recto', () => {
    expect(pathFaseLuna(0, 0, 10, 0.5)).toContain('A 0.00 10 0 0 1');
    expect(pathFaseLuna(0, 0, 10, 0.1)).toContain('0 0 0 0 -10');
  });
});
