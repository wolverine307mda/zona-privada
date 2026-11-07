import { DENSIDADES, ESCALA_MAX, ESCALA_MIN, FUENTES, VARIABLES_ASPECTO, esDensidad, esFuente, limitarEscala, variablesAspecto } from './aspecto';

describe('tipografía y densidad', () => {
  it('el tamaño de la letra se queda entre 90 y 130 % y de 5 en 5, y lo raro vuelve a 100', () => {
    expect(limitarEscala(100)).toBe(100); expect(limitarEscala(50)).toBe(ESCALA_MIN); expect(limitarEscala(500)).toBe(ESCALA_MAX);
    expect(limitarEscala(103)).toBe(105); expect(limitarEscala('115')).toBe(115);
    expect(limitarEscala('abc')).toBe(100); expect(limitarEscala(undefined)).toBe(100); expect(limitarEscala(NaN)).toBe(100);
  });

  it('el tamaño raíz sale en porcentaje para que todo lo que va en rem se escale', () => {
    expect(variablesAspecto({ escala: 130, densidad: 'comoda', fuente: 'siempre' }).tamanoRaiz).toBe('130%');
    expect(variablesAspecto({ escala: 90, densidad: 'comoda', fuente: 'siempre' }).tamanoRaiz).toBe('90%');
  });

  it('la densidad cómoda deja los espacios de siempre y la compacta y la amplia los encogen y los estiran', () => {
    const c = variablesAspecto({ escala: 100, densidad: 'comoda', fuente: 'siempre' }).vars;
    expect(c['--esp-1']).toBe('4px'); expect(c['--esp-4']).toBe('16px'); expect(c['--esp-7']).toBe('48px'); expect(c['--hueco']).toBe('16px');
    const k = variablesAspecto({ escala: 100, densidad: 'compacta', fuente: 'siempre' }).vars;
    const a = variablesAspecto({ escala: 100, densidad: 'amplia', fuente: 'siempre' }).vars;
    for (let i = 1; i <= 7; i++) { expect(parseFloat(k[`--esp-${i}`])).toBeLessThan(parseFloat(c[`--esp-${i}`])); expect(parseFloat(a[`--esp-${i}`])).toBeGreaterThan(parseFloat(c[`--esp-${i}`])); }
    expect(k['--hueco']).toBe('13px'); expect(a['--hueco']).toBe('19px');
  });

  it('la fuente de siempre no toca la tipografía del tema y las otras la cambian en texto y títulos', () => {
    const s = variablesAspecto({ escala: 100, densidad: 'comoda', fuente: 'siempre' }).vars;
    expect(s['--font-text']).toBeUndefined(); expect(s['--font-display']).toBeUndefined();
    for (const f of ['sistema', 'lectura'] as const) {
      const v = variablesAspecto({ escala: 100, densidad: 'comoda', fuente: f }).vars;
      expect(v['--font-text']).toBe(FUENTES.find((x) => x.id === f)!.texto); expect(v['--font-display']).toBe(v['--font-text']);
    }
  });

  it('todas las variables que se ponen se pueden limpiar', () => {
    for (const d of DENSIDADES) for (const f of FUENTES) {
      for (const k of Object.keys(variablesAspecto({ escala: 100, densidad: d.id, fuente: f.id }).vars)) expect(VARIABLES_ASPECTO).toContain(k);
    }
  });

  it('solo se aceptan densidades y fuentes conocidas', () => {
    expect(esDensidad('amplia')).toBe(true); expect(esDensidad('enorme')).toBe(false); expect(esDensidad(null)).toBe(false);
    expect(esFuente('lectura')).toBe(true); expect(esFuente('comic')).toBe(false);
    expect(new Set(DENSIDADES.map((d) => d.id)).size).toBe(3); expect(DENSIDADES.map((d) => d.factor)).toEqual([0.8, 1, 1.2]);
  });
});
