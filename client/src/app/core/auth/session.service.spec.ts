import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthenticationResponse } from './auth.models';
import { SessionService } from './session.service';

export function authenticated(token = 'access-1', roles: string[] = ['Learner'], permissions: string[] = []): AuthenticationResponse {
  return {
    accessToken: token,
    accessTokenExpiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString(),
    principal: {
      id: '00000000-0000-0000-0000-000000000001',
      userName: 'ada',
      email: 'ada@example.org',
      fullNameEn: 'Ada',
      fullNameAr: 'آدا',
      mfaEnabled: false,
      roles,
      permissions,
    },
  };
}

describe('SessionService', () => {
  let service: SessionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    service = TestBed.inject(SessionService);
    http = TestBed.inject(HttpTestingController);
    localStorage.clear();
    sessionStorage.clear();
  });

  afterEach(() => http.verify());

  // UI-01: the access token is held in memory only.
  it('keeps the access token in memory and never in browser storage', () => {
    service.login('ada', 'pw').subscribe();
    http.expectOne('/api/v1/auth/login').flush({ mfaRequired: false, challenge: null, authenticated: authenticated('tok') });

    expect(service.accessToken()).toBe('tok');
    expect(service.isAuthenticated()).toBe(true);
    expect(JSON.stringify(localStorage)).not.toContain('tok');
    expect(JSON.stringify(sessionStorage)).not.toContain('tok');
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('does not establish a session when the credential step yields an MFA challenge (SEC-09)', () => {
    let response: { mfaRequired: boolean } | undefined;
    service.login('ada', 'pw').subscribe((r) => (response = r));
    http.expectOne('/api/v1/auth/login').flush({ mfaRequired: true, challenge: 'chal', authenticated: null });

    expect(response?.mfaRequired).toBe(true);
    expect(service.isAuthenticated()).toBe(false);

    service.verifyMfa('chal', '123456').subscribe();
    http.expectOne('/api/v1/auth/mfa/verify').flush(authenticated('after-mfa'));
    expect(service.accessToken()).toBe('after-mfa');
  });

  // UI-04: concurrent refresh requests collapse into one.
  it('shares a single in-flight refresh between concurrent callers', () => {
    const tokens: string[] = [];
    service.refresh().subscribe((t) => tokens.push(t));
    service.refresh().subscribe((t) => tokens.push(t));
    service.refresh().subscribe((t) => tokens.push(t));

    const requests = http.match('/api/v1/auth/refresh');
    expect(requests.length).toBe(1);
    requests[0].flush(authenticated('fresh'));

    expect(tokens).toEqual(['fresh', 'fresh', 'fresh']);
    expect(service.accessToken()).toBe('fresh');
  });

  it('issues a new refresh request once the previous one has completed', () => {
    service.refresh().subscribe();
    http.expectOne('/api/v1/auth/refresh').flush(authenticated('one'));

    service.refresh().subscribe();
    http.expectOne('/api/v1/auth/refresh').flush(authenticated('two'));

    expect(service.accessToken()).toBe('two');
  });

  it('clears the session when refresh fails', () => {
    service.login('ada', 'pw').subscribe();
    http.expectOne('/api/v1/auth/login').flush({ mfaRequired: false, challenge: null, authenticated: authenticated('tok') });

    let failed = false;
    service.refresh().subscribe({ error: () => (failed = true) });
    http.expectOne('/api/v1/auth/refresh').flush({ status: 401 }, { status: 401, statusText: 'Unauthorized' });

    expect(failed).toBe(true);
    expect(service.isAuthenticated()).toBe(false);
    expect(service.principal()).toBeNull();
  });

  // UI-02: start-up exchanges the refresh cookie; failure leaves the client anonymous without error.
  it('initialise resolves true after a successful refresh and false otherwise', () => {
    let outcome: boolean | undefined;
    service.initialise().subscribe((o) => (outcome = o));
    http.expectOne('/api/v1/auth/refresh').flush(authenticated('boot'));
    expect(outcome).toBe(true);
    expect(service.accessToken()).toBe('boot');

    outcome = undefined;
    service.initialise().subscribe((o) => (outcome = o));
    http.expectOne('/api/v1/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(outcome).toBe(false);
    expect(service.isAuthenticated()).toBe(false);
  });

  // UI-05: ending the session returns the user to authentication.
  it('logout posts to the server, clears the session and navigates to login', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    service.login('ada', 'pw').subscribe();
    http.expectOne('/api/v1/auth/login').flush({ mfaRequired: false, challenge: null, authenticated: authenticated('tok') });

    service.logout().subscribe();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });

    expect(service.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/login');
  });

  it('exposes role and permission checks from the principal', () => {
    service.login('ada', 'pw').subscribe();
    http.expectOne('/api/v1/auth/login').flush({
      mfaRequired: false,
      challenge: null,
      authenticated: authenticated('tok', ['Instructor'], ['lms.content.write']),
    });

    expect(service.hasRole('Instructor')).toBe(true);
    expect(service.hasRole('Administrator')).toBe(false);
    expect(service.hasAnyRole(['Administrator', 'Instructor'])).toBe(true);
    expect(service.hasPermission('lms.content.write')).toBe(true);
    expect(service.hasPermission('identity.user.read')).toBe(false);
  });
});
