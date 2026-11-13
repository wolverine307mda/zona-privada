import { describe, expect, it } from 'vitest';
import { CuentaGoogle, ajustesGoogle, colorEvento, destinosGoogle } from './agenda.service';

const cal = (id: string, mostrar: boolean, escribible = true) => ({ id, nombre: id, color: null, principal: false, escribible, mostrar });

const cuentas: CuentaGoogle[] = [
  { clave: 'google', cuenta: 'yo@gmail.com', calendarios: [cal('yo@gmail.com', true), cal('trabajo', false), cal('festivos', true, false)] },
  { clave: 'google-a1b2c3', cuenta: 'pareja@gmail.com', calendarios: [cal('casa', true)] },
];

describe('cuentas de Google en la agenda', () => {
  it('los ajustes llevan los calendarios marcados de cada cuenta y el de por defecto', () => {
    expect(ajustesGoogle(cuentas, { cuenta: 'google', calendario: 'trabajo' })).toEqual({
      defecto: { cuenta: 'google', calendario: 'trabajo' },
      mostrar: { google: ['yo@gmail.com', 'festivos'], 'google-a1b2c3': ['casa'] },
    });
  });

  it('una cuenta sin ningún calendario marcado se manda vacía (no vuelve a salir el principal)', () => {
    const sin = [{ ...cuentas[0], calendarios: cuentas[0].calendarios.map((c) => ({ ...c, mostrar: false })) }];
    expect(ajustesGoogle(sin, null).mostrar).toEqual({ google: [] });
  });

  it('solo se puede copiar un evento en calendarios escribibles, con el nombre de la cuenta si hay varias', () => {
    expect(destinosGoogle(cuentas)).toEqual([
      { valor: 'google|yo@gmail.com', texto: 'yo@gmail.com · yo@gmail.com' },
      { valor: 'google|trabajo', texto: 'yo@gmail.com · trabajo' },
      { valor: 'google-a1b2c3|casa', texto: 'pareja@gmail.com · casa' },
    ]);
    expect(destinosGoogle([cuentas[1]]).map((d) => d.texto)).toEqual(['casa']);
  });

  it('el color de un evento de Google es el de su calendario', () => {
    expect(colorEvento('#9fe1e7')).toBe('#9fe1e7');
    expect(colorEvento('google')).toBe('#4285f4');
    expect(colorEvento('rojo')).toBe('#ff5a5f');
    expect(colorEvento('no-hex')).toBe('var(--brand)');
  });
});
