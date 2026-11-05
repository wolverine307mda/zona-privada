import { aHex } from './color-css';

describe('color del tema para Three y ECharts', () => {
  it('pasa rgb y rgba a hexadecimal', () => {
    expect(aHex('rgb(255, 122, 31)')).toBe('#ff7a1f');
    expect(aHex('rgba(7, 13, 18, 0.5)')).toBe('#070d12');
    expect(aHex('rgb(7 13 18 / 50%)')).toBe('#070d12');
  });

  it('pasa color(srgb …) (lo que devuelve el navegador para un color-mix) a hexadecimal', () => {
    expect(aHex('color(srgb 1 0.4784 0.1216)')).toBe('#ff7a1f');
    expect(aHex('color(srgb 0 0 0)')).toBe('#000000');
    expect(aHex('color(srgb 0.0275 0.051 0.0706)')).toBe('#070d12');
  });

  it('no inventa nada con lo que no entiende', () => {
    expect(aHex('color-mix(in srgb, #000 10%, #fff)')).toBeNull();
    expect(aHex('')).toBeNull();
    expect(aHex('naranja')).toBeNull();
  });
});
