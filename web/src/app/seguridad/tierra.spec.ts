import { esTierra, puntosDeTierra } from './tierra';

describe('tierra del globo', () => {
  it('reconoce ciudades en tierra', () => {
    expect(esTierra(40.4, -3.7)).toBe(true);    // Madrid
    expect(esTierra(40.7, -74)).toBe(true);     // Nueva York
    expect(esTierra(-23.5, -46.6)).toBe(true);  // São Paulo
    expect(esTierra(35.7, 139.7)).toBe(true);   // Tokio
    expect(esTierra(-33.9, 151)).toBe(true);    // Sídney
    expect(esTierra(30, 31)).toBe(true);        // El Cairo
  });

  it('deja el mar sin puntos', () => {
    expect(esTierra(30, -40)).toBe(false);   // Atlántico
    expect(esTierra(0, -150)).toBe(false);   // Pacífico
    expect(esTierra(-30, 80)).toBe(false);   // Índico
  });

  it('da una cantidad razonable de puntos', () => {
    const n = puntosDeTierra(3).length;
    expect(n).toBeGreaterThan(1200);
    expect(n).toBeLessThan(5000);
  });
});

describe('textura de los continentes', () => {
  it('la columna de la textura empieza en el meridiano −90° y siempre cae entre 0 y 1', async () => {
    const { columnaTextura } = await import('./tierra');
    expect(columnaTextura(-90)).toBeCloseTo(0, 6); expect(columnaTextura(0)).toBeCloseTo(0.25, 6); expect(columnaTextura(90)).toBeCloseTo(0.5, 6);
    expect(columnaTextura(-180)).toBeCloseTo(0.75, 6); expect(columnaTextura(180)).toBeCloseTo(0.75, 6);
    for (let lon = -180; lon <= 180; lon += 7) { const c = columnaTextura(lon); expect(c).toBeGreaterThanOrEqual(0); expect(c).toBeLessThan(1); }
  });
});
