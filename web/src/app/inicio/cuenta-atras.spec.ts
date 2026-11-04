import { Aparicion } from '../agenda/agenda.service';
import { cuentas, partes } from './cuenta-atras';

const ev = (clave: string, inicio: string, fin: string | null, extra: Partial<Aparicion> = {}): Aparicion => ({
  clave, id: 1, origen: 'propio', tipo: 'Evento', titulo: clave, notas: null, lugar: null, inicio, fin, todoElDia: false, color: 'azul',
  repetir: 'No', avisoMinutos: null, porCorreo: false, porWhatsApp: false, porPush: false, hecho: false, enGoogle: false, url: null, ...extra,
});
const t = (s: string) => new Date(s).getTime();

describe('cuenta atrás', () => {
  it('reparte los milisegundos en días, horas, minutos y segundos', () => {
    expect(partes(((1 * 24 + 2) * 3600 + 3 * 60 + 4) * 1000)).toEqual({ d: 1, h: 2, m: 3, s: 4 });
    expect(partes(-5000)).toEqual({ d: 0, h: 0, m: 0, s: 0 });
    expect(partes(999)).toEqual({ d: 0, h: 0, m: 0, s: 0 });
  });

  it('cuenta hacia el fin de lo que está pasando y hacia el inicio del siguiente', () => {
    const ahora = t('2026-11-04T10:00:00');
    const l = cuentas([ev('reunión', '2026-11-04T09:00:00', '2026-11-04T11:30:00'), ev('cena', '2026-11-04T21:00:00', null), ev('mañana', '2026-11-05T09:00:00', null)], ahora);
    expect(l.map((c) => [c.clave, c.modo])).toEqual([['reunión', 'termina'], ['cena', 'empieza']]);
    expect(l[0].ms).toBe(90 * 60 * 1000);
    expect(l[1].ms).toBe(11 * 3600 * 1000);
  });

  it('cambia de «empieza» a «termina» en el segundo exacto', () => {
    const e = ev('clase', '2026-11-04T10:00:00', '2026-11-04T11:00:00');
    expect(cuentas([e], t('2026-11-04T09:59:59'))[0].modo).toBe('empieza');
    expect(cuentas([e], t('2026-11-04T10:00:00'))[0].modo).toBe('termina');
    expect(cuentas([e], t('2026-11-04T11:00:00'))).toEqual([]);
  });

  it('un evento de todo el día sin fin cuenta hasta las 24:00', () => {
    const l = cuentas([ev('cumple', '2026-11-04T00:00:00', null, { todoElDia: true })], t('2026-11-04T18:00:00'));
    expect(l[0].modo).toBe('termina');
    expect(l[0].ms).toBe(6 * 3600 * 1000);
  });

  it('sin hora de fin no hay «termina en» y los recordatorios no cuentan', () => {
    expect(cuentas([ev('llamada', '2026-11-04T09:00:00', null)], t('2026-11-04T10:00:00'))).toEqual([]);
    expect(cuentas([ev('pastilla', '2026-11-04T12:00:00', null, { tipo: 'Recordatorio' })], t('2026-11-04T10:00:00'))).toEqual([]);
  });
});
