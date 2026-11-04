import { crearEscena, paletaDe } from './fondos';
import type { FondoId } from '../core/tema.service';

// un contexto 2D de mentira: acepta cualquier llamada y solo da datos donde hace falta
function contexto() {
  return new Proxy({} as Record<string, unknown>, {
    get: (_, k) => (k === 'createImageData' ? (w: number, h: number) => ({ data: new Uint8ClampedArray(w * h * 4) }) : () => undefined),
    set: () => true,
  });
}
const lienzo = () => ({ width: 0, height: 0, getContext: () => contexto() }) as unknown as HTMLCanvasElement;

describe('fondos animados', () => {
  const p = paletaDe('#ff7a1f', true);

  it('entiende el color de acento en hexadecimal y en rgb, y si no, usa el naranja', () => {
    expect(paletaDe('#c8f53c', true)).toMatchObject({ r: 200, g: 245, b: 60 });
    expect(paletaDe(' rgb(10, 20, 30)', false)).toMatchObject({ r: 10, g: 20, b: 30, oscuro: false });
    expect(paletaDe('algo raro', true)).toMatchObject({ r: 255, g: 122, b: 31 });
  });

  for (const id of ['liquido', 'isometrico', 'comic', 'pixel', 'particulas'] as FondoId[]) {
    it(`${id}: se crea, cambia de tamaño y dibuja varios fotogramas sin fallar`, () => {
      const e = crearEscena(id, lienzo(), p, { intensidad: 0.6, velocidad: 1 })!;
      expect(e).toBeTruthy();
      e.cambiar(1280, 720);
      e.puntero(400, 300);
      for (let i = 0; i < 5; i++) e.dibujar(i * 0.5, 0.033);
      e.cambiar(375, 667); // y al girar el móvil
      e.dibujar(3, 0.033);
    });
  }

  it('«aura» y «nada» no dibujan nada (los pinta el CSS)', () => {
    expect(crearEscena('aura', lienzo(), p, { intensidad: 1, velocidad: 1 })).toBeNull();
    expect(crearEscena('ninguno', lienzo(), p, { intensidad: 1, velocidad: 1 })).toBeNull();
  });
});
