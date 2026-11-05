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
