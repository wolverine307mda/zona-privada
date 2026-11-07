import { FRASES, ContextoBienvenida, clave, componer, esLunaLlena, esLunaNueva, faseLuna, festivo, hash, pascua, situaciones } from './bienvenida';

const base = (fecha: Date, extra: Partial<ContextoBienvenida> = {}): ContextoBienvenida => ({ fecha, nombre: 'Mario', cumples: [], ...extra });
const d = (a: number, m: number, dia: number, h = 10) => new Date(a, m - 1, dia, h, 0, 0);

describe('bienvenida personal', () => {
  it('la biblioteca tiene entre 50 y 100 frases, todas con texto y sin repetir', () => {
    const todas = Object.values(FRASES).flat();
    expect(todas.length).toBeGreaterThanOrEqual(50); expect(todas.length).toBeLessThanOrEqual(100);
    expect(new Set(todas).size).toBe(todas.length);
    expect(todas.every((f) => f.trim().length > 15)).toBe(true);
    expect(todas.some((f) => /!{2,}|✨|💖/.test(f))).toBe(false);          // sin cursilería ni exclamaciones a voces
  });

  it('el cumpleaños manda sobre todo lo demás y nombra a quien cumple', () => {
    const f = componer(base(d(2026, 12, 25), { cumples: ['María', 'Luis'], gastoAyer: { concepto: 'Cena', importe: '45 €' }, tiempo: { temperatura: 3, lluvia: true, texto: 'Lluvia' } }));
    expect(f.categoria).toBe('cumple'); expect(f.texto).toContain('María y Luis');
  });

  it('sin cumpleaños, un festivo manda sobre los datos del día', () => {
    const f = componer(base(d(2026, 11, 1), { gastoAyer: { concepto: 'Cena', importe: '45 €' } }));
    expect(f.categoria).toBe('festivo'); expect(f.texto).toContain('Todos los Santos');
  });

  it('cada combinación de día, hora y datos produce una frase con texto y sin llaves sin sustituir', () => {
    const extras: Partial<ContextoBienvenida>[] = [{}, { cumples: ['Ana'] }, { gastoAyer: { concepto: 'Tienda', importe: '20 €' } }, { racha: { nombre: 'Leer', dias: 5 } }, { entrenoAyer: true },
      { animoAyer: 1 }, { animoAyer: 5 }, { tiempo: { temperatura: 36, lluvia: false, texto: '' } }, { tiempo: { temperatura: 2, lluvia: false, texto: '' } }, { tiempo: { temperatura: 15, lluvia: true, texto: '' } }];
    let n = 0;
    for (let dia = 0; dia < 400; dia += 7) for (const h of [3, 9, 15, 22]) for (const e of extras) {
      const f = componer(base(new Date(2026, 0, 1 + dia, h), e));
      expect(f.texto.length).toBeGreaterThan(10); expect(f.texto).not.toMatch(/[{}]/); expect(f.id).toMatch(/^[a-zA-Z]+:\d+$/); n++;
    }
    expect(n).toBeGreaterThan(2000);
  });

  it('la misma fecha y el mismo contexto dan siempre la misma frase, aunque se recargue', () => {
    const c = base(d(2026, 6, 3), { gastoAyer: { concepto: 'Gasolina', importe: '50 €' } });
    expect(componer(c)).toEqual(componer({ ...c, fecha: d(2026, 6, 3, 18) }));          // otra hora del mismo día con el mismo tipo de situación
    expect(componer(c)).toEqual(componer(c));
  });

  it('nunca repite la frase que habría salido ayer (en 300 días seguidos)', () => {
    let anterior = '';
    let repetidas = 0;
    for (let i = 0; i < 300; i++) {
      const f = componer(base(new Date(2026, 0, 1 + i, 10)));
      if (f.id === anterior) repetidas++;
      anterior = f.id;
    }
    expect(repetidas).toBe(0);
  });

  it('la semilla es estable y reparte: hash no cambia y fechas distintas dan hashes distintos', () => {
    expect(hash('2026-06-03|Mario')).toBe(hash('2026-06-03|Mario'));
    expect(new Set(Array.from({ length: 100 }, (_, i) => hash(`2026-01-${i}`) % 5)).size).toBeGreaterThan(2);
    expect(clave(d(2026, 3, 5))).toBe('2026-03-05');
  });

  it('calcula bien la Pascua y el Viernes Santo y reconoce los festivos fijos', () => {
    expect(clave(pascua(2026))).toBe('2026-04-05'); expect(clave(pascua(2025))).toBe('2025-04-20'); expect(clave(pascua(2024))).toBe('2024-03-31');
    expect(festivo(d(2026, 4, 3))).toBe('Viernes Santo'); expect(festivo(d(2026, 4, 5))).toBeNull();
    expect(festivo(d(2026, 1, 1))).toBe('Año Nuevo'); expect(festivo(d(2026, 12, 25))).toBe('Navidad'); expect(festivo(d(2026, 10, 12))).toBe('la Fiesta Nacional');
    expect(festivo(d(2026, 7, 14))).toBeNull();
  });

  it('la fase de la luna cuadra con lunas conocidas', () => {
    expect(esLunaLlena(d(2026, 1, 3))).toBe(true);                // luna llena el 3 de enero de 2026
    expect(esLunaNueva(d(2026, 1, 18))).toBe(true);               // luna nueva el 18 de enero de 2026
    expect(esLunaLlena(d(2026, 1, 10))).toBe(false);
    for (let i = 0; i < 100; i++) { const f = faseLuna(new Date(2026, 0, 1 + i)); expect(f).toBeGreaterThanOrEqual(0); expect(f).toBeLessThan(1); }
  });

  it('las situaciones siempre acaban con día, estación y hora, y la lluvia y los extremos de temperatura se reconocen', () => {
    const s = situaciones(base(d(2026, 7, 15, 16), { tiempo: { temperatura: 35, lluvia: false, texto: '' } })).map((x) => x.categoria);
    expect(s).toEqual(expect.arrayContaining(['calor', 'semana', 'verano', 'tarde']));
    expect(situaciones(base(d(2026, 1, 12, 8), { tiempo: { temperatura: 4, lluvia: false, texto: '' } })).map((x) => x.categoria)).toContain('frio');
    expect(situaciones(base(d(2026, 3, 9, 8), { tiempo: { temperatura: 12, lluvia: true, texto: '' } })).map((x) => x.categoria)).toContain('lluvia');
    expect(situaciones(base(d(2026, 3, 9, 8))).map((x) => x.categoria)).toEqual(['lunes', 'primavera', 'manana']);
    expect(situaciones(base(d(2026, 1, 20, 8), { racha: { nombre: 'Leer', dias: 2 } })).map((x) => x.categoria)).not.toContain('racha');    // una racha de 2 días aún no es noticia
  });
});
