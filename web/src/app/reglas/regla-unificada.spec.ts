import { PLANTILLAS, ReglaContextoDto, ReglaCasaDto, ReglaUnificada, Suceso, aCasa, aContexto, choques, desdeCasa, desdeContexto, exportar, importar, leer, nombreDias, simular, validar } from './regla-unificada';

const ctx: ReglaContextoDto = { nombre: 'Cine', activa: true, evento: 'llego', desde: '20:00', hasta: '23:59', dias: 127, tiempo: 'lluvia', soloEnCasa: false, escenaCasaId: 3, escenaPcId: null, aviso: '', esperaMinutos: 60 };
const casa: ReglaCasaDto = { nombre: 'Aire', activa: true, sensorId: 7, sensorClave: 'temperatura', operador: '>', umbral: 28, minutos: 10, destinoId: 2, accion: 'encender' };

describe('constructor de reglas · parte pura', () => {
  it('contexto → unificada → contexto no pierde nada', () => {
    expect(aContexto(desdeContexto(ctx))).toEqual(ctx);
    const con = { ...ctx, soloEnCasa: true, escenaPcId: 4, aviso: 'hola', desde: null, hasta: null, tiempo: '', dias: 31, esperaMinutos: 15 };
    expect(aContexto(desdeContexto(con))).toEqual(con);
  });
  it('casa → unificada → casa no pierde nada', () => {
    expect(aCasa(desdeCasa(casa), 7)).toEqual(casa);
    const apaga = { ...casa, operador: '<', accion: 'apagar' };
    expect(aCasa(desdeCasa(apaga), 7)).toEqual(apaga);
  });
  it('no mezcla tipos: un sensor no se guarda como contexto y al revés', () => {
    expect(() => aContexto(desdeCasa(casa))).toThrow();
    expect(() => aCasa(desdeContexto(ctx), 1)).toThrow();
  });
  it('se lee en español', () => {
    expect(leer(desdeContexto(ctx), { 3: 'Cine' })).toBe('Cuando llegues a casa, entre las 20:00 y las 23:59, con lluvia, pon la escena Cine en casa.');
    expect(leer(desdeCasa(casa))).toContain('temperatura supere 28 durante 10 min');
    expect(leer({ nombre: 'x', activa: true, cuando: { tipo: 'salgo' }, si: { dias: 31, soloEnCasa: true }, entonces: { aviso: 'Adiós' } })).toBe('Cuando salgas de casa, de lunes a viernes, si estás en casa, avísame: «Adiós».');
    expect(nombreDias(96)).toBe('los fines de semana'); expect(nombreDias(5)).toBe('los lunes y miércoles'); expect(nombreDias(2)).toBe('los martes');
  });
  it('valida en vivo y dice qué falta', () => {
    const r: ReglaUnificada = { nombre: '', activa: true, cuando: { tipo: 'hora' }, si: { dias: 0, tiempo: 'niebla' }, entonces: {} };
    expect(validar(r)).toEqual(expect.arrayContaining(['Falta el nombre.', 'Una regla «a una hora» necesita la hora de inicio (HH:mm).', 'Elige al menos un día de la semana.', 'El tiempo elegido no existe.', 'Falta qué hacer: una escena o un aviso.']));
    expect(validar(desdeContexto(ctx))).toEqual([]); expect(validar(desdeCasa(casa))).toEqual([]);
    expect(validar({ ...desdeCasa(casa), entonces: {} })).toContain('Elige el aparato que se enciende o apaga.');
  });
  it('detecta reglas que se pisan y no marca las que no coinciden', () => {
    const a = desdeContexto(ctx), b = { ...desdeContexto(ctx), nombre: 'Otra' };
    expect(choques([a, b])).toEqual(['«Cine» y «Otra» se disparan a la vez.']);
    expect(choques([a, { ...b, si: { ...b.si, tiempo: 'seco' } }])).toEqual([]);
    expect(choques([a, { ...b, si: { ...b.si, dias: 0 } }])).toEqual([]);
    expect(choques([a, { ...b, activa: false }])).toEqual([]);
  });
  it('simula sobre lo ocurrido: respeta tiempo, franja, día, espera y ventana de 24 h', () => {
    const ahora = new Date(2026, 10, 7, 12, 0);              // sábado
    const s = (h: number, m: number, extra: Partial<Suceso> = {}, d = 7): Suceso => ({ cuando: new Date(2026, 10, d, h, m), evento: 'llego', tiempo: 'lluvia', ...extra });
    const r = desdeContexto({ ...ctx, desde: '08:00', hasta: '23:00', esperaMinutos: 60 });
    const sucesos = [s(9, 0), s(9, 30), s(10, 45), s(7, 0), s(10, 0, { tiempo: 'seco' }), s(9, 0, {}, 4), s(11, 0, { evento: 'salgo' })];
    expect(simular(r, sucesos, ahora)).toBe(2);              // 9:00 y 10:45 (las 9:30 caen en la espera; fuera de franja, sin lluvia, de hace 3 días o de otro evento, no)
    expect(simular({ ...r, activa: false }, sucesos, ahora)).toBe(0);
    expect(simular({ ...r, si: { ...r.si, soloEnCasa: true } }, sucesos, ahora)).toBe(0);
    expect(simular({ ...r, si: { ...r.si, dias: 31 } }, sucesos, ahora)).toBe(0);      // hoy es sábado
  });
  it('simula reglas de sensor por umbral', () => {
    const ahora = new Date(2026, 10, 7, 12, 0);
    const lec = (h: number, v: number): Suceso => ({ cuando: new Date(2026, 10, 7, h, 0), evento: 'sensor', sensor: 'temperatura', valor: v });
    expect(simular({ ...desdeCasa(casa), esperaMinutos: 30 }, [lec(8, 25), lec(9, 29), lec(10, 30), lec(11, 20)], ahora)).toBe(2);
  });
  it('las plantillas son válidas y se exportan e importan sin pérdida', () => {
    for (const p of PLANTILLAS) expect(validar(p.regla)).toEqual([]);
    const { reglas, errores } = importar(exportar(PLANTILLAS.map((p) => p.regla)));
    expect(errores).toEqual([]); expect(reglas).toEqual(PLANTILLAS.map((p) => p.regla));
  });
  it('importar descarta lo roto con el motivo y no se cae con basura', () => {
    expect(importar('no es json').errores[0]).toContain('JSON');
    expect(importar('{"a":1}').errores[0]).toContain('reglas');
    const r = importar(JSON.stringify({ reglas: [PLANTILLAS[0].regla, { nombre: 'x' }, { nombre: '', cuando: { tipo: 'llego' } }] }));
    expect(r.reglas.length).toBe(1); expect(r.errores.length).toBe(2);
  });
});
