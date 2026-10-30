import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SesionService } from './sesion.service';

describe('SesionService', () => {
  let sesion: SesionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    sesion = TestBed.inject(SesionService);
    http = TestBed.inject(HttpTestingController);
  });

  it('entra con cookie y guarda el usuario', async () => {
    const r = sesion.entrar('wolve', 'clave');
    const req = http.expectOne('/api/auth/login?useCookies=true');
    expect(req.request.body).toEqual({ email: 'wolve', password: 'clave' });
    req.flush({});
    expect(await r).toBe('ok');
    expect(sesion.usuario()).toBe('wolve');
  });

  it('distingue el segundo factor y el bloqueo', async () => {
    const a = sesion.entrar('yo@casa.es', 'clave');
    http.expectOne('/api/auth/login?useCookies=true').flush({ detail: 'RequiresTwoFactor' }, { status: 401, statusText: 'No' });
    expect(await a).toBe('segundo-factor');

    const b = sesion.entrar('yo@casa.es', 'clave');
    http.expectOne('/api/auth/login?useCookies=true').flush({ detail: 'LockedOut' }, { status: 401, statusText: 'No' });
    expect(await b).toBe('bloqueado');
    expect(sesion.dentro()).toBe(false);
  });

  it('sin cookie válida no hay sesión', async () => {
    const r = sesion.comprobar();
    http.expectOne('/api/sesion').flush(null, { status: 401, statusText: 'No' });
    expect(await r).toBe(false);
  });
});
