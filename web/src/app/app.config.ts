import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, ErrorHandler, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withNavigationErrorHandler, withViewTransitions } from '@angular/router';
import { sesionCaducada } from './core/guards';
import { AvisoDeErrores } from './core/errores';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: AvisoDeErrores },
    provideHttpClient(withFetch(), withInterceptors([sesionCaducada])),
    // fundido entre pantallas, salvo al entrar o salir de la sesión: ahí cambia toda la página
    // (el candado se abre con su propia animación) y el fundido enseñaba las dos pantallas superpuestas
    provideRouter(routes, withComponentInputBinding(),
      // tras un despliegue los archivos con huella cambian: si falta el de una pantalla, se recarga una vez para coger los nuevos
      withNavigationErrorHandler(() => {
        try {
          if (sessionStorage.getItem('mda-recargado') === '1') return;
          sessionStorage.setItem('mda-recargado', '1');
          setTimeout(() => sessionStorage.removeItem('mda-recargado'), 10_000);
        } catch { /* sin almacenamiento */ }
        location.reload();
      }),
      withViewTransitions({
      skipInitialTransition: true,
      onViewTransitionCreated: ({ transition, from, to }) => {
        const ruta = (r: typeof from) => r.firstChild?.routeConfig?.path ?? '';
        if (ruta(from) === 'entrar' || ruta(to) === 'entrar') transition.skipTransition();
      },
    })),
  ],
};
