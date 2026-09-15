import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { anonymousOnlyGuard, authGuard, landingRedirectGuard, permissionGuard, roleGuard } from './auth.guards';
import { SessionService } from './session.service';
import { authenticated } from './session.service.spec';

describe('route guards (SDD 17.3)', () => {
  let session: SessionService;
  let backend: HttpTestingController;
  let router: Router;

  const route = {} as ActivatedRouteSnapshot;
  const state = (url: string) => ({ url }) as RouterStateSnapshot;

  function run(guard: ReturnType<typeof roleGuard>, url = '/target'): boolean | UrlTree {
    return TestBed.runInInjectionContext(() => guard(route, state(url))) as boolean | UrlTree;
  }

  function signIn(roles: string[], permissions: string[] = []): void {
    session.login('ada', 'pw').subscribe();
    backend.expectOne('/api/v1/auth/login').flush({ mfaRequired: false, challenge: null, authenticated: authenticated('tok', roles, permissions) });
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    session = TestBed.inject(SessionService);
    backend = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  it('authGuard redirects an anonymous user to login with the return url', () => {
    const result = run(authGuard, '/admin/users');

    expect(result).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Fadmin%2Fusers');
  });

  it('authGuard allows an authenticated user', () => {
    signIn(['Learner']);

    expect(run(authGuard)).toBe(true);
  });

  it('roleGuard allows a matching role and redirects a non-matching one to its own shell', () => {
    signIn(['Instructor']);

    expect(run(roleGuard('Instructor'))).toBe(true);
    expect(run(roleGuard('Administrator', 'Registrar'))).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(run(roleGuard('Administrator')) as UrlTree)).toBe('/instructor');
  });

  it('roleGuard sends an anonymous user to login', () => {
    expect(router.serializeUrl(run(roleGuard('Learner'), '/learner') as UrlTree)).toBe('/login?returnUrl=%2Flearner');
  });

  it('registrar shares the administration shell', () => {
    signIn(['Registrar']);

    expect(run(roleGuard('Administrator', 'Registrar'))).toBe(true);
    expect(router.serializeUrl(run(landingRedirectGuard) as UrlTree)).toBe('/admin');
  });

  it('permissionGuard reflects the permission set', () => {
    signIn(['Learner'], ['lms.content.read']);

    expect(run(permissionGuard('lms.content.read'))).toBe(true);
    expect(router.serializeUrl(run(permissionGuard('identity.user.read')) as UrlTree)).toBe('/learner');
  });

  it('landingRedirectGuard sends users to their role shell or to login', () => {
    expect(router.serializeUrl(run(landingRedirectGuard) as UrlTree)).toBe('/login');

    signIn(['Administrator']);
    expect(router.serializeUrl(run(landingRedirectGuard) as UrlTree)).toBe('/admin');
  });

  it('anonymousOnlyGuard keeps an authenticated user away from the login page', () => {
    expect(run(anonymousOnlyGuard)).toBe(true);

    signIn(['Learner']);
    expect(router.serializeUrl(run(anonymousOnlyGuard) as UrlTree)).toBe('/learner');
  });
});
