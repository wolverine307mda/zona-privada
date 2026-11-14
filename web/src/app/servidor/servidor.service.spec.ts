import { describe, expect, it } from 'vitest';
import { Suceso, conPermisoSiHaceFalta, hace, pasoNuevo, porDia } from './servidor.service';

const suceso = (hora: string, titulo = 'x'): Suceso => ({ hora, tipo: 'acceso', titulo, detalle: '', gravedad: 'info' });

describe('servidor', () => {
  it('un paso nuevo trae lo que necesita su tipo', () => {
    expect(pasoNuevo('espera').segundos).toBe(5);
    expect(pasoNuevo('comprobar').valor).toBe('base');
    expect(pasoNuevo('casa').valor).toBe('llegada');
    expect(pasoNuevo('aviso').texto).toBe('');
    expect(pasoNuevo('orden')).toMatchObject({ valor: 'bloquear', aprobar: false, continuar: false });
  });

  it('las órdenes que cortan lo que haces piden permiso solas; las demás no', () => {
    for (const valor of ['suspender', 'apagar', 'reiniciar']) expect(conPermisoSiHaceFalta({ ...pasoNuevo('orden'), valor }).aprobar).toBe(true);
    expect(conPermisoSiHaceFalta({ ...pasoNuevo('orden'), valor: 'bloquear' }).aprobar).toBe(false);
    expect(conPermisoSiHaceFalta(pasoNuevo('aviso')).aprobar).toBe(false);
  });

  it('la línea se agrupa por día: hoy, ayer y los anteriores con su nombre', () => {
    const ahora = new Date(2026, 10, 13, 12, 0);
    const grupos = porDia([suceso(new Date(2026, 10, 13, 9, 0).toISOString()), suceso(new Date(2026, 10, 13, 8, 0).toISOString()),
      suceso(new Date(2026, 10, 12, 23, 0).toISOString()), suceso(new Date(2026, 10, 9, 10, 0).toISOString())], ahora);
    expect(grupos.map((g) => g.dia)).toEqual(['Hoy', 'Ayer', expect.stringContaining('9 de noviembre')]);
    expect(grupos[0].sucesos).toHaveLength(2);
    expect(porDia([], ahora)).toEqual([]);
  });

  it('«hace» cuenta minutos, horas y días', () => {
    const ahora = new Date('2026-11-13T12:00:00Z').getTime();
    expect(hace('2026-11-13T11:59:50Z', ahora)).toBe('ahora mismo');
    expect(hace('2026-11-13T11:45:00Z', ahora)).toBe('hace 15 min');
    expect(hace('2026-11-13T09:00:00Z', ahora)).toBe('hace 3 h');
    expect(hace('2026-11-10T12:00:00Z', ahora)).toBe('hace 3 d');
  });
});
