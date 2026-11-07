import { describe, expect, it } from 'vitest';
import { coordenadasValidas, distancia, duracion, formato, kilometros, leerCoordenadas, posicionAhora, queda, segundos, texto } from './ubicacion-logica';

describe('leerCoordenadas', () => {
  it('lee los formatos de pegar más comunes', () => {
    expect(leerCoordenadas('40.4168, -3.7038')).toEqual({ lat: 40.4168, lon: -3.7038 });
    expect(leerCoordenadas('40.4168 -3.7038')).toEqual({ lat: 40.4168, lon: -3.7038 });
    expect(leerCoordenadas('  -33.8688;151.2093 ')).toEqual({ lat: -33.8688, lon: 151.2093 });
    expect(leerCoordenadas('-33.8688,151.2093')).toEqual({ lat: -33.8688, lon: 151.2093 });
  });

  it('lee los enlaces de Google Maps y de Apple Maps', () => {
    expect(leerCoordenadas('https://www.google.com/maps/place/Madrid/@40.4167754,-3.7037902,15z/data=x')).toEqual({ lat: 40.4167754, lon: -3.7037902 });
    expect(leerCoordenadas('https://www.google.com/maps/place/X/data=!3d48.8583701!4d2.2944813')).toEqual({ lat: 48.8583701, lon: 2.2944813 });
    expect(leerCoordenadas('https://maps.google.com/?q=41.38741,2.16864')).toEqual({ lat: 41.38741, lon: 2.16864 });
    expect(leerCoordenadas('https://maps.apple.com/?ll=35.65951,139.70053&z=14')).toEqual({ lat: 35.65951, lon: 139.70053 });
  });

  it('rechaza lo que no es un punto de la Tierra', () => {
    expect(leerCoordenadas('')).toBeNull();
    expect(leerCoordenadas('hola')).toBeNull();
    expect(leerCoordenadas('91, 0')).toBeNull();
    expect(leerCoordenadas('0, 181')).toBeNull();
    expect(leerCoordenadas('40.4')).toBeNull();
    expect(leerCoordenadas('x'.repeat(700))).toBeNull();
  });
});

describe('cálculos', () => {
  it('valida el rango', () => {
    expect(coordenadasValidas(90, 180)).toBe(true);
    expect(coordenadasValidas(-90.01, 0)).toBe(false);
    expect(coordenadasValidas(NaN, 0)).toBe(false);
  });

  it('Madrid–Barcelona ronda los 500 km', () => {
    const km = distancia({ lat: 40.4168, lon: -3.7038 }, { lat: 41.3874, lon: 2.1686 }) / 1000;
    expect(km).toBeGreaterThan(495);
    expect(km).toBeLessThan(510);
  });

  it('el tiempo sale de la velocidad del modo', () => {
    expect(segundos(5000, 'andar')).toBe(3600);
    expect(segundos(15000, 'bici')).toBe(3600);
    expect(segundos(50000, 'coche')).toBe(3600);
    expect(segundos(0.1, 'coche')).toBe(1);
  });

  it('da formato a números, duraciones y distancias', () => {
    expect(formato(40.416829999)).toBe('40.41683');
    expect(formato(2)).toBe('2');
    expect(texto({ lat: 1.5, lon: -2 })).toBe('1.5, -2');
    expect(duracion(45)).toBe('45 s');
    expect(duracion(600)).toBe('10 min');
    expect(duracion(7200)).toBe('2 h');
    expect(duracion(5400)).toBe('1 h 30 min');
    expect(kilometros(350)).toBe('350 m');
    expect(kilometros(2500)).toBe('2.5 km');
    expect(kilometros(25000)).toBe('25 km');
  });

  it('cuenta lo que queda', () => {
    const a = new Date('2026-01-01T10:00:00Z');
    expect(queda(new Date('2026-01-01T11:20:00Z'), a)).toBe('1 h 20 min');
    expect(queda(new Date('2026-01-01T10:45:10Z'), a)).toBe('45 min');
    expect(queda(new Date('2026-01-01T10:00:30Z'), a)).toBe('menos de 1 min');
    expect(queda(new Date('2026-01-01T09:00:00Z'), a)).toBe('terminando');
  });
});

describe('posicionAhora', () => {
  const desde = '2026-01-01T10:00:00Z';
  const t0 = new Date(desde).getTime();
  it('una ubicación fija no se mueve', () => {
    expect(posicionAhora({ lat: 10, lon: 20, desde, destinoLat: null, destinoLon: null, segundos: 0 }, t0 + 5000)).toEqual({ lat: 10, lon: 20 });
  });
  it('de camino avanza proporcionalmente y al llegar se queda', () => {
    const s = { lat: 40, lon: -3, desde, destinoLat: 41, destinoLon: -3, segundos: 1000 };
    expect(posicionAhora(s, t0).lat).toBe(40);
    expect(posicionAhora(s, t0 + 500_000).lat).toBeCloseTo(40.5, 6);
    expect(posicionAhora(s, t0 + 9_000_000).lat).toBe(41);
  });
});
