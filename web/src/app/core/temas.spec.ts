import { PARES, TEMAS, TEMA_POR_DEFECTO, VARIABLES_TEMAS, esTema, luminancia, razonContraste, temaDe } from './temas';

describe('temas completos', () => {
  it('la razón de contraste cuadra con los valores de referencia de WCAG', () => {
    expect(razonContraste('#000000', '#ffffff')).toBeCloseTo(21, 1);
    expect(razonContraste('#ffffff', '#ffffff')).toBeCloseTo(1, 5);
    expect(razonContraste('#767676', '#ffffff')).toBeCloseTo(4.54, 1);          // el gris más claro que pasa AA sobre blanco
    expect(razonContraste('#fff', '#000')).toBeCloseTo(21, 1);                  // también en formato corto
    expect(luminancia('#000000')).toBe(0);
    expect(razonContraste('#123456', '#abcdef')).toBeCloseTo(razonContraste('#abcdef', '#123456'), 10);
  });

  for (const t of TEMAS.filter((x) => Object.keys(x.vars).length > 0)) {
    for (const [txt, fondo] of PARES) {
      it(`${t.id}: ${txt} sobre ${fondo} llega a ${t.minimo}:1`, () => {
        expect(razonContraste(t.vars[txt], t.vars[fondo])).toBeGreaterThanOrEqual(t.minimo);
      });
    }
  }

  it('cada tema trae todas las variables de color que necesita y los de un solo modo lo dicen', () => {
    for (const t of TEMAS.filter((x) => x.id !== 'cristal')) {
      for (const v of ['--fondo', '--tarjeta', '--tarjeta-2', '--linea', '--texto', '--soft']) expect(t.vars[v], `${t.id} ${v}`).toMatch(/^#[0-9a-f]{6}$/i);
      expect(t.modo, t.id).not.toBeNull();
      // un tema claro tiene el fondo más claro que el texto y uno oscuro, al revés
      const f = luminancia(t.vars['--fondo']), x = luminancia(t.vars['--texto']);
      expect(t.modo === 'light' ? f > x : f < x, t.id).toBe(true);
    }
    expect(temaDe('cristal').modo).toBeNull();
    expect(Object.keys(temaDe('cristal').vars)).toHaveLength(0);
  });

  it('los identificadores no se repiten, el de por defecto existe y lo raro se rechaza', () => {
    expect(new Set(TEMAS.map((t) => t.id)).size).toBe(TEMAS.length);
    expect(TEMAS).toHaveLength(6);
    expect(esTema(TEMA_POR_DEFECTO)).toBe(true);
    expect(esTema('verde-fosforito')).toBe(false); expect(esTema(null)).toBe(false);
    expect(temaDe('inventado' as never).id).toBe('cristal');
  });

  it('las variables que tocan los temas se pueden limpiar todas', () => {
    expect(VARIABLES_TEMAS).toEqual(expect.arrayContaining(['--fondo', '--texto', '--font-text']));
    expect(new Set(VARIABLES_TEMAS).size).toBe(VARIABLES_TEMAS.length);
  });

  it('alto contraste exige 7:1 y lo cumple con el texto principal', () => {
    const c = temaDe('contraste');
    expect(c.minimo).toBe(7);
    expect(razonContraste(c.vars['--texto'], c.vars['--fondo'])).toBeGreaterThanOrEqual(7);
  });
});
