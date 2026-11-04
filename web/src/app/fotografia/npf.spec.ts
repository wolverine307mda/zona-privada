import { calcular, pixelDesdeSensor, texto } from './npf';

describe('regla 500 y NPF', () => {
  it('la regla 500 divide entre focal por recorte', () => {
    expect(calcular({ focal: 20, recorte: 1, apertura: 2.8, pixelUm: 4.3, declinacion: 0 })!.regla500).toBeCloseTo(25, 5);
    expect(calcular({ focal: 20, recorte: 1.5, apertura: 2.8, pixelUm: 4.3, declinacion: 0 })!.regla500).toBeCloseTo(16.67, 1);
  });
  it('el NPF usa apertura y píxel: píxeles más grandes, más tiempo', () => {
    const a = calcular({ focal: 20, recorte: 1, apertura: 2.8, pixelUm: 4.3, declinacion: 0 })!.npf;   // (98 + 129) / 20
    const b = calcular({ focal: 20, recorte: 1, apertura: 2.8, pixelUm: 8.4, declinacion: 0 })!.npf;
    expect(a).toBeCloseTo(11.35, 2);
    expect(b).toBeGreaterThan(a);
  });
  it('cerca del polo se puede exponer más y con datos inválidos no calcula', () => {
    const ecuador = calcular({ focal: 24, recorte: 1, apertura: 2, pixelUm: 4, declinacion: 0 })!.npf;
    expect(calcular({ focal: 24, recorte: 1, apertura: 2, pixelUm: 4, declinacion: 60 })!.npf).toBeCloseTo(ecuador * 2, 1);
    expect(calcular({ focal: 0, recorte: 1, apertura: 2, pixelUm: 4, declinacion: 0 })).toBeNull();
  });
  it('saca el píxel del sensor y da el texto', () => {
    expect(pixelDesdeSensor(36, 45)!).toBeCloseTo(4.38, 1);
    expect(pixelDesdeSensor(0, 24)).toBeNull();
    expect(texto(9.5)).toBe('9.5 s');
    expect(texto(25)).toBe('25 s');
  });
});
