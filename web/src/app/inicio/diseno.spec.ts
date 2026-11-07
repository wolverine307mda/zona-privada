import { IDS_POR_DEFECTO, TARJETAS, alternarOculta, disenoPorDefecto, esPorDefecto, idDeTarjeta, mover, normalizar, ponerTamano } from './diseno';

describe('distribución de Inicio', () => {
  it('el orden por defecto es el de siempre y no tiene identificadores repetidos', () => {
    expect(disenoPorDefecto().orden).toEqual(IDS_POR_DEFECTO);
    expect(new Set(IDS_POR_DEFECTO).size).toBe(IDS_POR_DEFECTO.length);
    expect(TARJETAS.every((t) => t.titulo.length > 0)).toBe(true);
    expect(esPorDefecto(disenoPorDefecto())).toBe(true);
  });

  it('migra una distribución vieja o rara: nada, vacío, una lista suelta o basura', () => {
    expect(normalizar(null)).toEqual(disenoPorDefecto());
    expect(normalizar(undefined)).toEqual(disenoPorDefecto());
    expect(normalizar({})).toEqual(disenoPorDefecto());
    expect(normalizar('hola')).toEqual(disenoPorDefecto());
    expect(normalizar(['tiempo', 'dinero']).orden.slice(0, 2)).toEqual(['tiempo', 'dinero']);          // una versión vieja guardaba solo la lista
    expect(normalizar({ orden: 7, ocultas: 'x', tamanos: 3 })).toEqual(disenoPorDefecto());
  });

  it('una tarjeta que ya no existe desaparece de todo y no deja huecos', () => {
    const d = normalizar({ orden: ['fantasma', 'tiempo', 'dinero', 'tiempo'], ocultas: ['fantasma', 'pc'], tamanos: { fantasma: 'L', tiempo: 'M', dinero: 'XL' } });
    expect(d.orden).not.toContain('fantasma'); expect(d.orden.filter((x) => x === 'tiempo')).toHaveLength(1);
    expect(d.orden.slice(0, 2)).toEqual(['tiempo', 'dinero']);
    expect(d.ocultas).toEqual(['pc']);
    expect(d.tamanos).toEqual({ tiempo: 'M' });
  });

  it('una tarjeta nueva se añade al final, en su orden, sin pisar lo elegido', () => {
    const d = normalizar({ orden: ['tiempo', 'dinero'] }, ['dinero', 'tiempo', 'nueva1', 'nueva2']);
    expect(d.orden).toEqual(['tiempo', 'dinero', 'nueva1', 'nueva2']);
    expect(normalizar({ orden: ['b', 'a'] }, ['a', 'b']).orden).toEqual(['b', 'a']);
    expect(normalizar({ orden: [] }, ['a', 'b', 'c']).orden).toEqual(['a', 'b', 'c']);
  });

  it('mover sube y baja de una en una y no se sale por los extremos', () => {
    const d = normalizar({}, ['a', 'b', 'c']);
    expect(mover(d, 'b', -1).orden).toEqual(['b', 'a', 'c']); expect(mover(d, 'b', 1).orden).toEqual(['a', 'c', 'b']);
    expect(mover(d, 'a', -1)).toBe(d); expect(mover(d, 'c', 1)).toBe(d); expect(mover(d, 'zzz', 1)).toBe(d);
  });

  it('ocultar, mostrar y cambiar el tamaño no tocan el orden y se pueden deshacer', () => {
    let d = disenoPorDefecto();
    d = alternarOculta(d, 'pc'); expect(d.ocultas).toEqual(['pc']); expect(esPorDefecto(d)).toBe(false);
    d = alternarOculta(d, 'pc'); expect(d.ocultas).toEqual([]);
    d = ponerTamano(d, 'dinero', 'L'); expect(d.tamanos).toEqual({ dinero: 'L' });
    d = ponerTamano(d, 'dinero', null); expect(d.tamanos).toEqual({}); expect(esPorDefecto(d)).toBe(true);
    expect(esPorDefecto({ ...disenoPorDefecto(), orden: [...IDS_POR_DEFECTO].reverse() })).toBe(false);
  });

  it('reconoce cada tarjeta por su etiqueta, sus clases y su enlace, y distingue agenda de recordatorios', () => {
    const t = (etiqueta: string, clases: string[] = [], href: string | null = null, repeticion = 0) => idDeTarjeta({ etiqueta, clases, href, repeticion });
    expect(t('APP-ROPA-CHIP')).toBe('ropa'); expect(t('app-cumples-chip')).toBe('cumples'); expect(t('app-tiempo', ['caja', 'tiempo'])).toBe('tiempo');
    expect(t('a', ['caja', 'dinero'], '/gastos')).toBe('dinero'); expect(t('div', ['caja', 'musica'])).toBe('musica'); expect(t('a', ['caja', 'coche'])).toBe('coche'); expect(t('div', ['carpetas'])).toBe('carpetas');
    expect(t('a', ['caja', 'lista'], '/agenda', 0)).toBe('agenda'); expect(t('a', ['caja', 'lista'], '/agenda', 1)).toBe('recordatorios');
    expect(t('a', ['caja', 'lista'], '/notas')).toBe('notas');
    for (const [h, id] of [['/habitos', 'habitos'], ['/fotografia', 'fotografia'], ['/pc', 'pc'], ['/casa', 'casa'], ['/seguridad', 'seguridad'], ['/gastos/gasolina', 'gasolina']]) expect(t('a', ['caja', 'mini'], h)).toBe(id);
    expect(t('div', ['asis-inicio'])).toBeNull(); expect(t('a', ['caja'], '/algo-raro')).toBeNull();
    // todos los identificadores que reconoce existen en la lista de tarjetas
    for (const id of ['ropa', 'cumples', 'tiempo', 'dinero', 'musica', 'coche', 'carpetas', 'agenda', 'recordatorios', 'notas', 'habitos', 'fotografia', 'pc', 'casa', 'seguridad', 'gasolina']) expect(IDS_POR_DEFECTO).toContain(id);
  });
});
