import { crearEscena, paletaDe } from './fondos';
import { col, fija } from './fondos-base';
import { FONDOS, GRUPOS_FONDO, type FondoId } from '../core/tema.service';

// un contexto 2D de mentira: acepta cualquier llamada, devuelve degradados que aceptan paradas de color y solo da datos donde hace falta
function contexto() {
  const degradado = { addColorStop: () => undefined };
  return new Proxy({} as Record<string, unknown>, {
    get: (_, k) => (k === 'createImageData' ? (w: number, h: number) => ({ data: new Uint8ClampedArray(w * h * 4) }) : () => degradado),
    set: () => true,
  });
}
const lienzo = () => ({ width: 0, height: 0, dataset: {}, getContext: () => contexto() }) as unknown as HTMLCanvasElement;
const alfa = (c: string) => +c.slice(c.lastIndexOf(',') + 1, -1);

describe('fondos animados', () => {
  const p = paletaDe('#ff7a1f', true);

  it('entiende el color de acento en hexadecimal y en rgb, y si no, usa el naranja', () => {
    expect(paletaDe('#c8f53c', true)).toMatchObject({ r: 200, g: 245, b: 60 });
    expect(paletaDe(' rgb(10, 20, 30)', false)).toMatchObject({ r: 10, g: 20, b: 30, oscuro: false });
    expect(paletaDe('algo raro', true)).toMatchObject({ r: 255, g: 122, b: 31 });
  });

  // todos los fondos con dibujo (los clásicos, los nuevos y los de superhéroes), en ordenador y en móvil
  const conDibujo = FONDOS.filter((f) => f.id !== 'aura' && f.id !== 'ninguno').map((f) => f.id);
  for (const id of conDibujo) {
    for (const ligero of [false, true]) {
      it(`${id}${ligero ? ' (móvil)' : ''}: se crea, cambia de tamaño y dibuja varios fotogramas sin fallar`, async () => {
        const e = (await crearEscena(id, lienzo(), { ...p }, { intensidad: 0.6, velocidad: 1, ligero }))!;
        expect(e).toBeTruthy();
        e.cambiar(1280, 720);
        e.puntero(400, 300);
        for (let i = 0; i < 6; i++) e.dibujar(i * 0.5, 0.033);
        e.cambiar(375, 667); // y al girar el móvil
        e.dibujar(3, 0.033);
      });
    }
  }

  it('funcionan también con el modo claro y con la fuerza del color al máximo y al mínimo', async () => {
    for (const [oscuro, fuerza] of [[false, 2], [true, 0.5]] as const) {
      for (const id of conDibujo) {
        const e = (await crearEscena(id, lienzo(), { ...paletaDe('#c8f53c', oscuro), fuerza }, { intensidad: 1, velocidad: 2 }))!;
        e.cambiar(800, 600);
        for (let i = 0; i < 3; i++) e.dibujar(i, 0.05);
      }
    }
  });

  it('«aura» y «nada» no dibujan nada (los pinta el CSS)', async () => {
    expect(await crearEscena('aura', lienzo(), p, { intensidad: 1, velocidad: 1 })).toBeNull();
    expect(await crearEscena('ninguno', lienzo(), p, { intensidad: 1, velocidad: 1 })).toBeNull();
  });

  it('la lista de fondos no repite identificadores y cada uno está en un grupo que existe', () => {
    const ids = FONDOS.map((f) => f.id);
    expect(new Set(ids).size).toBe(ids.length);
    const grupos = new Set(GRUPOS_FONDO.map((g) => g.id));
    expect(FONDOS.every((f) => grupos.has(f.grupo))).toBe(true);
    expect(ids.length).toBeGreaterThanOrEqual(30);
    expect(FONDOS.filter((f) => f.grupo === 'marvel').length).toBeGreaterThanOrEqual(6);
    expect(ids).toContain('multiverso' as FondoId);
  });

  describe('la fuerza del color', () => {
    const base = { r: 200, g: 100, b: 50, oscuro: true };
    it('sin fuerza el color sale tal cual', () => expect(col(base, 0.5)).toBe('rgba(200,100,50,0.500)'));
    it('con más fuerza es más opaco y más saturado, y con menos, más tenue', () => {
      const normal = col(base, 0.3), fuerte = col({ ...base, fuerza: 2 }, 0.3), flojo = col({ ...base, fuerza: 0.5 }, 0.3);
      expect(alfa(fuerte)).toBeGreaterThan(alfa(normal));
      expect(alfa(flojo)).toBeLessThan(alfa(normal));
      const rojo = (c: string) => +/rgba\((\d+),/.exec(c)![1], azul = (c: string) => +/rgba\(\d+,\d+,(\d+),/.exec(c)![1];
      expect(rojo(fuerte) - azul(fuerte)).toBeGreaterThan(rojo(normal) - azul(normal)); // más lejos del gris
    });
    it('la opacidad nunca pasa de 1', () => expect(alfa(col({ ...base, fuerza: 2 }, 0.9))).toBeLessThanOrEqual(1));
    it('los colores fijos de los fondos de superhéroes siguen leyendo la fuerza del tema en vivo', () => {
      const tema = { ...base, fuerza: 1 }, q = fija(tema, 10, 20, 30);
      expect([q.r, q.g, q.b]).toEqual([10, 20, 30]);
      tema.fuerza = 2;
      expect(q.fuerza).toBe(2);
    });
  });
});
