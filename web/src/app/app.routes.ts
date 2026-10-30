import { Routes } from '@angular/router';
import { conSesion, sinSesion } from './core/guards';

// Cada pantalla se carga aparte: Three.js solo en «entrar» y ECharts solo en el panel
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'panel' },
  { path: 'entrar', title: 'Entrar · MDA', canActivate: [sinSesion], loadComponent: () => import('./entrar/entrar').then((m) => m.Entrar) },
  { path: 'panel', title: 'Panel · MDA', canActivate: [conSesion], loadComponent: () => import('./panel/panel').then((m) => m.Panel) },
  { path: '**', redirectTo: 'panel' },
];
