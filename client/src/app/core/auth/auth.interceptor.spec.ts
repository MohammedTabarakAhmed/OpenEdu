import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { SessionService } from './session.service';
import { authenticated } from './session.service.spec';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let session: SessionService;
  let router: Router;

  function signIn(token: string): void {
    session.login('ada', 'pw').subscribe();
    backend.expectOne('/api/v1/auth/login').flush({ mfaRequired: false, challenge: null, authenticated: authenticated(token) });
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => backend.verify());

  // UI-03: the access token is attached to outbound requests.
  it('attaches the bearer token to outbound requests when authenticated', () => {
    signIn('tok');

    http.get('/api/v1/users').subscribe();

    const request = backend.expectOne('/api/v1/users');
    expect(request.request.headers.get('Authorization')).toBe('Bearer tok');
    request.flush([]);
  });

  it('sends no Authorization header when anonymous or for the authentication flow', () => {
    http.get('/api/health').subscribe();
    expect(backend.expectOne('/api/health').request.headers.has('Authorization')).toBe(false);

    signIn('tok');
    session.refresh().subscribe();
    expect(backend.expectOne('/api/v1/auth/refresh').request.headers.has('Authorization')).toBe(false);
  });

  // UI-03: on an authentication failure, a single refresh is attempted and the original request replayed.
  it('refreshes once and replays the original request after a 401', () => {
    signIn('stale');
    let body: unknown;
    http.get('/api/v1/auth/me').subscribe((b) => (body = b));

    backend.expectOne('/api/v1/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/auth/refresh').flush(authenticated('fresh'));

    const replay = backend.expectOne('/api/v1/auth/me');
    expect(replay.request.headers.get('Authorization')).toBe('Bearer fresh');
    replay.flush({ ok: true });

    expect(body).toEqual({ ok: true });
    expect(session.accessToken()).toBe('fresh');
  });

  // UI-04: concurrent failures result in exactly one refresh; the rest queue behind it.
  it('performs exactly one refresh for concurrent 401 responses and replays each request', () => {
    signIn('stale');
    const results: string[] = [];
    http.get('/api/v1/users').subscribe(() => results.push('users'));
    http.get('/api/v1/roles').subscribe(() => results.push('roles'));
    http.get('/api/v1/audit').subscribe(() => results.push('audit'));

    backend.expectOne('/api/v1/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/roles').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/audit').flush(null, { status: 401, statusText: 'Unauthorized' });

    const refreshes = backend.match('/api/v1/auth/refresh');
    expect(refreshes.length).toBe(1);
    refreshes[0].flush(authenticated('fresh'));

    for (const url of ['/api/v1/users', '/api/v1/roles', '/api/v1/audit']) {
      const replay = backend.expectOne(url);
      expect(replay.request.headers.get('Authorization')).toBe('Bearer fresh');
      replay.flush({});
    }

    expect(results.sort()).toEqual(['audit', 'roles', 'users']);
    expect(backend.match('/api/v1/auth/refresh').length).toBe(0);
  });

  // UI-05: failure of refresh terminates the client session and returns the user to authentication.
  it('ends the session and navigates to login when the refresh fails', () => {
    signIn('stale');
    let error: unknown;
    http.get('/api/v1/users').subscribe({ error: (e: unknown) => (error = e) });

    backend.expectOne('/api/v1/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect((error as HttpErrorResponse).status).toBe(401);
    expect(session.isAuthenticated()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    backend.expectNone('/api/v1/users');
  });

  it('does not attempt a refresh when the authentication flow itself returns 401', () => {
    let status = 0;
    session.login('ada', 'wrong').subscribe({ error: (e: HttpErrorResponse) => (status = e.status) });

    backend.expectOne('/api/v1/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(status).toBe(401);
    backend.expectNone('/api/v1/auth/refresh');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('passes through non-401 failures without refreshing', () => {
    signIn('tok');
    let status = 0;
    http.get('/api/v1/users').subscribe({ error: (e: HttpErrorResponse) => (status = e.status) });

    backend.expectOne('/api/v1/users').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(status).toBe(403);
    backend.expectNone('/api/v1/auth/refresh');
    expect(session.isAuthenticated()).toBe(true);
  });

  it('propagates a failure of the replayed request without ending the session', () => {
    signIn('stale');
    let status = 0;
    http.get('/api/v1/users').subscribe({ error: (e: HttpErrorResponse) => (status = e.status) });

    backend.expectOne('/api/v1/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/auth/refresh').flush(authenticated('fresh'));
    backend.expectOne('/api/v1/users').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(status).toBe(403);
    expect(session.isAuthenticated()).toBe(true);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
