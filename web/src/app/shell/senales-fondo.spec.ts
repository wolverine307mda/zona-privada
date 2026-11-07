import { energiaCarga, energiaHora, energiaMusica, factorIntensidad, factorVelocidad, reaccionPermitida } from './senales-fondo';
import { crearEscena, paletaDe } from './fondos';
import { FONDOS } from '../core/tema.service';

describe('señales de los fondos que reaccionan', () => {
  it('la música solo respira mientras suena y siempre entre 0 y 1', () => {
    expect(energiaMusica(false, 3.3)).toBe(0);
    for (let t = 0; t < 10; t += 0.13) { const e = energiaMusica(true, t); expect(e).toBeGreaterThanOrEqual(0); expect(e).toBeLessThanOrEqual(1); }
    expect(energiaMusica(true, 0)).toBeCloseTo(0.5, 5);
  });

  it('la carga pasa de porcentaje a 0–1 y aguanta datos raros', () => {
    expect(energiaCarga(0)).toBe(0);
    expect(energiaCarga(50)).toBe(0.5);
    expect(energiaCarga(100)).toBe(1);
    expect(energiaCarga(250)).toBe(1);
    expect(energiaCarga(-5)).toBe(0);
    expect(energiaCarga(null)).toBe(0);
    expect(energiaCarga(undefined)).toBe(0);
  });

  it('la hora es mínima de madrugada y máxima a media tarde', () => {
    expect(energiaHora(2)).toBeCloseTo(0, 5);
    expect(energiaHora(14)).toBeCloseTo(1, 5);
    expect(energiaHora(8)).toBeGreaterThan(energiaHora(4));
    for (let h = 0; h <= 24; h += 0.5) { expect(energiaHora(h)).toBeGreaterThanOrEqual(0); expect(energiaHora(h)).toBeLessThanOrEqual(1); }
  });

  it('los factores van de 1 hasta su tope y no se salen aunque la energía o el «cuánto» se pasen', () => {
    expect(factorVelocidad(0, 1)).toBe(1);
    expect(factorVelocidad(1, 0)).toBe(1);
    expect(factorVelocidad(1, 1)).toBeCloseTo(1.8, 5);
    expect(factorVelocidad(5, 9)).toBeCloseTo(1.8, 5);
    expect(factorVelocidad(-3, 1)).toBe(1);
    expect(factorIntensidad(1, 1)).toBeCloseTo(1.35, 5);
    expect(factorIntensidad(0.5, 0.5)).toBeCloseTo(1.0875, 5);
  });

  it('en el modo ligero solo reacciona la música', () => {
    expect(reaccionPermitida('musica', true)).toBe('musica');
    expect(reaccionPermitida('carga', true)).toBe('nada');
    expect(reaccionPermitida('hora', true)).toBe('nada');
    expect(reaccionPermitida('hora', false)).toBe('hora');
  });
});

describe('ninguna escena se rompe con la energía en los extremos', () => {
  // un lienzo de mentira como el de fondos.spec: acepta cualquier llamada
  const ctx = new Proxy({}, { get: (_o, p) => (p === 'createLinearGradient' || p === 'createRadialGradient' ? () => ({ addColorStop() {} }) : p === 'getImageData' || p === 'createImageData' ? (w: number, h: number) => ({ data: new Uint8ClampedArray(Math.max(1, w * h * 4)), width: w, height: h }) : p === 'measureText' ? () => ({ width: 10 }) : () => undefined), set: () => true });
  const lienzo = { getContext: () => ctx, width: 100, height: 100, style: {} } as unknown as HTMLCanvasElement;

  for (const e of [0, 1]) {
    it(`todas las escenas dibujan con intensidad y velocidad al máximo con energía ${e}`, async () => {
      for (const f of FONDOS.filter((x) => x.id !== 'aura' && x.id !== 'ninguno').slice(0, 12)) {
        const paleta = paletaDe('#ff7a1f', true);
        const op = { intensidad: Math.min(1, 0.6 * factorIntensidad(e, 1)), velocidad: factorVelocidad(e, 1), ligero: false };
        const escena = await crearEscena(f.id, lienzo, paleta, op);
        if (!escena) continue;
        escena.cambiar(320, 200);
        expect(() => { escena.dibujar(1.5, 0.033 * op.velocidad); escena.dibujar(9999, 0); }).not.toThrow();
      }
    });
  }
});
