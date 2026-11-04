import { AparatoCasa, alternarOculto, aparatosDe, hace, leerPanel, mover, texto, trazo } from './modelo';

const ap = (id: number, estanciaId: number | null): AparatoCasa => ({ id, nombre: 'A' + id, tipo: 'luz', tipoNombre: 'Lámpara', driver: 'demo', estanciaId, entidades: [] });
const todos = [ap(1, 10), ap(2, 10), ap(3, 11), ap(4, null)];

describe('casa · panel', () => {
  it('el panel guardado roto o vacío no rompe nada', () => {
    expect(leerPanel(null)).toEqual({ orden: {}, ocultos: [] });
    expect(leerPanel('{no es json')).toEqual({ orden: {}, ocultos: [] });
    expect(leerPanel('{"orden":{"10":[2,"x",1]},"ocultos":[3,"y"]}')).toEqual({ orden: { '10': [2, 1] }, ocultos: [3] });
  });

  it('enseña los aparatos de la pestaña en el orden guardado y los nuevos al final', () => {
    const panel = { orden: { '10': [2] }, ocultos: [] };
    expect(aparatosDe(todos, 10, panel, false).map((a) => a.id)).toEqual([2, 1]);
    expect(aparatosDe(todos, 'todo', { orden: {}, ocultos: [] }, false).map((a) => a.id)).toEqual([1, 2, 3, 4]);
  });

  it('los ocultos solo salen al editar', () => {
    const panel = alternarOculto({ orden: {}, ocultos: [] }, 2);
    expect(aparatosDe(todos, 10, panel, false).map((a) => a.id)).toEqual([1]);
    expect(aparatosDe(todos, 10, panel, true).map((a) => a.id)).toEqual([1, 2]);
    expect(alternarOculto(panel, 2).ocultos).toEqual([]);
  });

  it('subir y bajar cambia el orden sin tocar el panel original y respeta los extremos', () => {
    const panel = { orden: {}, ocultos: [] };
    const bajado = mover(todos, 10, panel, 1, 1);
    expect(aparatosDe(todos, 10, bajado, true).map((a) => a.id)).toEqual([2, 1]);
    expect(panel.orden).toEqual({});
    expect(mover(todos, 10, panel, 1, -1)).toBe(panel);
    expect(mover(todos, 10, panel, 2, 1)).toBe(panel);
  });
});

describe('casa · textos y gráfica', () => {
  it('da formato a los valores', () => {
    expect(texto(23.4, '°C')).toBe('23,4 °C');
    expect(texto(52, '%')).toBe('52 %');
    expect(texto(null, '°C')).toBe('—');
  });

  it('dibuja la línea entre dos y más lecturas y nada con menos', () => {
    expect(trazo([{ fecha: '2026-11-04T10:00:00Z', valor: 20 }])).toBe('');
    const t = trazo([{ fecha: '2026-11-04T10:00:00Z', valor: 20 }, { fecha: '2026-11-04T11:00:00Z', valor: 24 }], 100, 30, 0);
    expect(t).toBe('M0.0 30.0 L100.0 0.0');
  });

  it('dice cuánto hace de una lectura', () => {
    const ahora = new Date('2026-11-04T12:00:00Z').getTime();
    expect(hace('2026-11-04T11:59:40Z', ahora)).toBe('ahora');
    expect(hace('2026-11-04T11:55:00Z', ahora)).toBe('hace 5 min');
    expect(hace('2026-11-04T09:00:00Z', ahora)).toBe('hace 3 h');
    expect(hace(null, ahora)).toBe('sin datos');
  });
});
