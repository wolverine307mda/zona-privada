import { alturas, destacadas, sentido, textoVariacion } from './barras';

describe('tarjeta de métrica', () => {
  it('la barra mayor llena el alto y las demás van en proporción, sin bajar del mínimo', () => {
    expect(alturas([50, 100, 25])).toEqual([50, 100, 25]);
    expect(alturas([0, 100], 6)).toEqual([6, 100]);
    expect(alturas([0, 0, 0], 6)).toEqual([6, 6, 6]);
    expect(alturas([])).toEqual([]);
    expect(alturas([-5, 10], 6)[0]).toBe(6); // un negativo no hace una barra al revés
  });

  it('elige las barras más altas y gana la primera en un empate', () => {
    expect(destacadas([3, 9, 1, 9, 5], 2)).toEqual([1, 3]);
    expect(destacadas([3, 9, 1, 8, 5], 2)).toEqual([1, 3]);
    expect(destacadas([4, 4, 4], 1)).toEqual([0]);
    expect(destacadas([1, 2], 0)).toEqual([]);
  });

  it('escribe la variación con el signo menos de verdad y coma decimal', () => {
    expect(textoVariacion(-18)).toBe('−18 %');
    expect(textoVariacion(4.5, 1)).toBe('+4,5 %');
    expect(textoVariacion(0)).toBe('0 %');
    expect(textoVariacion(0.04, 0)).toBe('0 %'); // lo que se redondea a cero no lleva signo
  });

  it('dice si baja, sube o se queda igual', () => {
    expect(sentido(-18)).toBe('baja');
    expect(sentido(2)).toBe('sube');
    expect(sentido(0.2)).toBe('igual');
    expect(sentido(0.2, 1)).toBe('sube');
  });
});
