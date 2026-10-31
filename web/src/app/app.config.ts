import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { sesionCaducada } from './core/guards';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors([sesionCaducada])),
    // fundido entre pantallas, salvo al entrar o salir de la sesión: ahí cambia toda la página
    // (el candado se abre con su propia animación) y el fundido enseñaba las dos pantallas superpuestas
    provideRouter(routes, withComponentInputBinding(), withViewTransitions({
      skipInitialTransition: true,
      onViewTransitionCreated: ({ transition, from, to }) => {
        const ruta = (r: typeof from) => r.firstChild?.routeConfig?.path ?? '';
        if (ruta(from) === 'entrar' || ruta(to) === 'entrar') transition.skipTransition();
      },
    })),
  ],
};
