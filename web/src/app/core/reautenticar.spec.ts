import { conLimite } from './reautenticar';

describe('espera límite de Face ID', () => {
  afterEach(() => vi.useRealTimers());

  it('devuelve el resultado si llega a tiempo', async () => {
    await expect(conLimite(Promise.resolve('ok'), 1000)).resolves.toBe('ok');
  });

  it('pasa el error de quien espera sin esconderlo', async () => {
    await expect(conLimite(Promise.reject(new Error('cancelado')), 1000)).rejects.toThrow('cancelado');
  });

  it('falla pasado el tiempo si la espera no termina nunca (llave que el móvil no encuentra)', async () => {
    vi.useFakeTimers();
    const espera = conLimite(new Promise<never>(() => { /* nunca responde */ }), 45_000);
    const resultado = expect(espera).rejects.toThrow('Face ID no responde');
    await vi.advanceTimersByTimeAsync(45_000);
    await resultado;
  });
});
