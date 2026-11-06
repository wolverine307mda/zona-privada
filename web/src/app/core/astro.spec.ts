import { describe, expect, it } from 'vitest';
import { faseLuna, momentoDelDia, nombreFase, posicionSol } from './astro';

const GRADOS = 180 / Math.PI;

describe('astro', () => {
  it('la luna nueva y la llena de enero de 2024 salen donde deben', () => {
    expect(faseLuna(new Date('2024-01-11T11:57:00Z')).fraccion).toBeLessThan(0.03);
    expect(faseLuna(new Date('2024-01-25T17:54:00Z')).fraccion).toBeGreaterThan(0.97);
  });

  it('crece hacia la llena y mengua después', () => {
    const creciente = faseLuna(new Date('2024-01-18T12:00:00Z'));   // cuarto creciente
    const menguante = faseLuna(new Date('2024-02-02T12:00:00Z'));   // cuarto menguante
    expect(creciente.fase).toBeGreaterThan(0.15);
    expect(creciente.fase).toBeLessThan(0.35);
    expect(menguante.fase).toBeGreaterThan(0.65);
    expect(menguante.fase).toBeLessThan(0.85);
  });

  it('el sol está casi en el cenit en el ecuador al mediodía del equinoccio', () => {
    const p = posicionSol(new Date('2024-03-20T12:07:00Z'), 0, 0);
    expect(p.altura * GRADOS).toBeGreaterThan(85);
  });

  it('es de noche en Madrid a medianoche en invierno y de día a mediodía en verano', () => {
    expect(posicionSol(new Date('2024-12-21T23:00:00Z'), 40.4, -3.7).altura * GRADOS).toBeLessThan(-40);
    expect(posicionSol(new Date('2024-06-21T10:30:00Z'), 40.4, -3.7).altura * GRADOS).toBeGreaterThan(60);
  });

  it('el sol sale por el este y se pone por el oeste', () => {
    const manana = posicionSol(new Date('2024-06-21T04:30:00Z'), 40.4, -3.7);   // poco después del amanecer
    const tarde = posicionSol(new Date('2024-06-21T18:00:00Z'), 40.4, -3.7);
    expect(manana.azimut).toBeLessThan(0);
    expect(tarde.azimut).toBeGreaterThan(0);
  });

  it('nombra las fases y el momento del día', () => {
    expect(nombreFase(0)).toBe('Luna nueva');
    expect(nombreFase(0.5)).toBe('Luna llena');
    expect(nombreFase(0.25)).toBe('Cuarto creciente');
    expect(momentoDelDia(-20)).toBe('noche');
    expect(momentoDelDia(0)).toBe('amanecer-atardecer');
    expect(momentoDelDia(40)).toBe('dia');
  });
});
