import { describe, expect, it, vi } from 'vitest';
import { crearAcciones, esComando, filtrarAcciones, textoComando } from './acciones';
import type { TemaService } from '../core/tema.service';

const tema = () => ({ ponerModo: vi.fn(), ponerColor: vi.fn(), ponerFondo: vi.fn() }) as unknown as TemaService;
const deps = (extra: Partial<Parameters<typeof crearAcciones>[0]> = {}) => ({
  tema: tema(), orden: vi.fn(async () => 'hecho'), escenas: () => [{ id: 7, nombre: 'Cine' }], aplicarEscena: vi.fn(async () => 'escena puesta'), avisar: vi.fn(), ...extra,
});

describe('buscador · acciones', () => {
  it('reconoce los comandos con «>»', () => {
    expect(esComando('> color rojo')).toBe(true);
    expect(esComando('  >bloquear')).toBe(true);
    expect(esComando('12 café')).toBe(false);
    expect(textoComando('>  color rojo')).toBe('color rojo');
    expect(textoComando('>')).toBe('');
  });

  it('filtra por todas las palabras, sin tildes ni mayúsculas', () => {
    const lista = crearAcciones(deps());
    expect(filtrarAcciones(lista, 'COLOR Roj').map((a) => a.id)).toEqual(['a-color-rojo']);
    expect(filtrarAcciones(lista, 'bloquear pc').map((a) => a.id)).toEqual(['a-bloquear-pc']);
    expect(filtrarAcciones(lista, 'escena cine')).toHaveLength(1);
    expect(filtrarAcciones(lista, 'zzzz')).toHaveLength(0);
    expect(filtrarAcciones(lista, '')).toHaveLength(lista.length);
  });

  it('cada acción hace lo suyo', async () => {
    const d = deps();
    const lista = crearAcciones(d);
    lista.find((a) => a.id === 'a-color-turquesa')!.hacer();
    expect(d.tema.ponerColor).toHaveBeenCalledWith('turquesa');
    lista.find((a) => a.id === 'a-modo-dark')!.hacer();
    expect(d.tema.ponerModo).toHaveBeenCalledWith('dark');
    await lista.find((a) => a.id === 'a-bloquear-pc')!.hacer();
    expect(d.orden).toHaveBeenCalledWith('bloquear');
    expect(d.avisar).toHaveBeenCalledWith('hecho');
    await lista.find((a) => a.id === 'a-despierto-off')!.hacer();
    expect(d.orden).toHaveBeenCalledWith('despierto', 'off');
    await lista.find((a) => a.id === 'a-escena-7')!.hacer();
    expect(d.aplicarEscena).toHaveBeenCalledWith(7);
  });

  it('las órdenes al PC y las escenas dejan el buscador abierto para enseñar el resultado; el tema, no', () => {
    const lista = crearAcciones(deps());
    expect(lista.find((a) => a.id === 'a-bloquear-pc')!.mantener).toBe(true);
    expect(lista.find((a) => a.id === 'a-escena-7')!.mantener).toBe(true);
    expect(lista.find((a) => a.id === 'a-color-rojo')!.mantener).toBeFalsy();
  });
});
