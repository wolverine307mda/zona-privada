import { describe, expect, it } from 'vitest';
import { RECETAS, TOKEN_PENDIENTE, cuerpoDe, esDireccionLocal, generarPrompt, normalizarOrigen, recetasPara } from './atajos-prompt';

const receta = (id: string) => RECETAS.find((r) => r.id === id)!;

describe('recetas de atajos', () => {
  it('cada receta tiene un id único y una ruta de la API', () => {
    expect(new Set(RECETAS.map((r) => r.id)).size).toBe(RECETAS.length);
    for (const r of RECETAS) expect(r.ruta).toMatch(/^\/api\/atajos\/[a-z]+$/);
  });

  it('solo se ofrecen las recetas que el permiso del token permite', () => {
    expect(recetasPara(['nota']).map((r) => r.id)).toEqual(['nota']);
    expect(recetasPara(['casa']).map((r) => r.id)).toEqual(['llegada', 'salida', 'dormir']);
    expect(recetasPara([])).toEqual([]);
  });

  it('el cuerpo de ejemplo lleva los campos obligatorios y los fijos, y deja fuera los opcionales', () => {
    expect(cuerpoDe(receta('gasto'))).toEqual({ importe: 12.5, concepto: 'Café' });
    expect(cuerpoDe(receta('llegada'))).toEqual({ estado: 'llegada' });
    expect(cuerpoDe(receta('estado'))).toBeNull();
  });
});

describe('generarPrompt', () => {
  it('lleva la dirección completa, el token, el método y el cuerpo', () => {
    const p = generarPrompt(receta('gasto'), 'mda_abc123', 'https://app.wolvem.dev/');
    expect(p).toContain('https://app.wolvem.dev/api/atajos/movimiento');
    expect(p).toContain('Bearer mda_abc123');
    expect(p).toContain('Método: POST');
    expect(p).toContain('{"importe":12.5,"concepto":"Café"}');
    expect(p).not.toContain(TOKEN_PENDIENTE);
  });

  it('sin token deja la marca y explica qué sustituir', () => {
    const p = generarPrompt(receta('nota'), null, 'https://app.wolvem.dev');
    expect(p).toContain(`Bearer ${TOKEN_PENDIENTE}`);
    expect(p).toContain(`sustituye ${TOKEN_PENDIENTE}`);
  });

  it('una lectura es GET, sin cuerpo, y se pronuncia en voz alta', () => {
    const p = generarPrompt(receta('estado'), 'mda_x', 'https://app.wolvem.dev');
    expect(p).toContain('Método: GET');
    expect(p).toContain('Sin cuerpo');
    expect(p).toContain('Pronunciar texto');
    expect(p).not.toContain('Ejemplo del cuerpo');
  });

  it('el pánico avisa de que revoca los tokens', () => {
    expect(generarPrompt(receta('panico'), 'mda_x', 'https://app.wolvem.dev')).toContain('revoca todos los tokens');
  });

  it('un valor fijo se explica como siempre el mismo', () => {
    expect(generarPrompt(receta('salida'), 'mda_x', 'https://app.wolvem.dev')).toContain('estado (texto) = «salida», siempre ese valor');
  });
});

describe('dirección', () => {
  it('se queda solo con el origen', () => {
    expect(normalizarOrigen(' https://app.wolvem.dev/ajustes?x=1 ')).toBe('https://app.wolvem.dev');
    expect(normalizarOrigen('app.wolvem.dev/')).toBe('app.wolvem.dev');
  });

  it('detecta las direcciones que el iPhone no alcanza desde fuera', () => {
    for (const h of ['http://localhost:4200', 'http://127.0.0.1:8090', 'http://192.168.1.20:8090', 'http://10.0.0.5', 'http://172.20.1.1', 'http://mipc.local', 'no es una url']) expect(esDireccionLocal(h)).toBe(true);
    for (const h of ['https://app.wolvem.dev', 'http://172.32.0.1', 'https://8.8.8.8']) expect(esDireccionLocal(h)).toBe(false);
  });
});
