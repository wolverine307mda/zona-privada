import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { PinService } from './pin.service';
import { SesionService } from './sesion.service';

// El panel solo con sesión; si no, a la pantalla de entrar
// (inject() siempre antes del primer await: después ya no hay contexto de inyección)
export const conSesion: CanActivateFn = async () => {
  const sesion = inject(SesionService);
  const router = inject(Router);
  return (await sesion.comprobar()) || router.parseUrl('/entrar');
};

// Si ya hay sesión, «entrar» lleva directo al panel
export const sinSesion: CanActivateFn = async () => {
  const sesion = inject(SesionService);
  const router = inject(Router);
  return !(await sesion.comprobar()) || router.parseUrl('/inicio');
};

// Una respuesta 401 fuera del login significa que la sesión caducó
export const sesionCaducada: HttpInterceptorFn = (req, next) => {
  const sesion = inject(SesionService);
  const router = inject(Router);
  const pin = inject(PinService);
  return next(req).pipe(
    catchError((e: unknown) => {
      // un 401 aquí es «contraseña o código mal», no «sesión caducada»: no se echa al usuario al login
      const login = req.url.startsWith('/api/auth/') || req.url.startsWith('/api/sesion') || req.url.startsWith('/api/seguridad/reautenticar') || req.url.startsWith('/api/seguridad/pin');
      if (e instanceof HttpErrorResponse && e.status === 423) sesion.bloqueada.set(true);
      if (e instanceof HttpErrorResponse && e.status === 401 && !login) {
        sesion.olvidar();
        router.navigateByUrl('/entrar');
      }
      // 403 «pin»: Documentos y lo suyo piden el PIN; se enseña el teclado y, si se acierta, se repite la petición
      if (e instanceof HttpErrorResponse && e.status === 403 && e.error?.pin && !req.url.startsWith('/api/seguridad/pin'))
        return from(pin.pedir()).pipe(switchMap((ok) => (ok ? next(req) : throwError(() => e))));
      return throwError(() => e);
    }),
  );
};
