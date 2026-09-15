import { HttpClient, HttpContext, HttpContextToken } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, finalize, map, Observable, of, shareReplay, tap, throwError } from 'rxjs';
import { AuthenticationResponse, LoginResponse, Principal } from './auth.models';

/** Marks a request as belonging to the authentication flow itself; a 401 there is final and never triggers a refresh. */
export const IS_AUTH_FLOW = new HttpContextToken<boolean>(() => false);

const authFlow = () => ({ context: new HttpContext().set(IS_AUTH_FLOW, true) });

/**
 * Holds the client session (SDD 17.2).
 * UI-01: the access token lives only in this service's memory; nothing is written to browser storage.
 * UI-02: `initialise()` exchanges the refresh cookie on application start.
 * UI-04: `refresh()` is single-flight; concurrent callers share one HTTP request.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly token = signal<string | null>(null);
  private readonly user = signal<Principal | null>(null);
  private refreshInFlight: Observable<string> | null = null;

  readonly accessToken = this.token.asReadonly();
  readonly principal = this.user.asReadonly();
  readonly isAuthenticated = computed(() => this.token() !== null);

  hasRole(role: string): boolean {
    return this.user()?.roles.includes(role) ?? false;
  }

  hasAnyRole(roles: readonly string[]): boolean {
    return roles.some((r) => this.hasRole(r));
  }

  hasPermission(code: string): boolean {
    return this.user()?.permissions.includes(code) ?? false;
  }

  /** Credential step. Resolves to the MFA challenge when one is required (SEC-09), else establishes the session. */
  login(userNameOrEmail: string, password: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>('/api/v1/auth/login', { userNameOrEmail, password }, authFlow())
      .pipe(tap((r) => r.authenticated && this.establish(r.authenticated)));
  }

  verifyMfa(challenge: string, code: string): Observable<AuthenticationResponse> {
    return this.http
      .post<AuthenticationResponse>('/api/v1/auth/mfa/verify', { challenge, code }, authFlow())
      .pipe(tap((r) => this.establish(r)));
  }

  /** UI-02: called once at start-up; a failure simply leaves the client anonymous. */
  initialise(): Observable<boolean> {
    return this.refresh().pipe(
      map(() => true),
      catchError(() => of(false)),
    );
  }

  /** UI-04: exactly one refresh at a time; later callers queue behind the in-flight request. */
  refresh(): Observable<string> {
    this.refreshInFlight ??= this.http
      .post<AuthenticationResponse>('/api/v1/auth/refresh', null, authFlow())
      .pipe(
        tap((r) => this.establish(r)),
        map((r) => r.accessToken),
        catchError((error: unknown) => {
          this.clear();
          return throwError(() => error);
        }),
        finalize(() => (this.refreshInFlight = null)),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    return this.refreshInFlight;
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/v1/auth/logout', null, authFlow()).pipe(
      catchError(() => of(void 0)),
      finalize(() => this.endSession()),
    );
  }

  /** UI-05: terminates the client session and returns the user to authentication. */
  endSession(): void {
    this.clear();
    void this.router.navigateByUrl('/login');
  }

  private establish(response: AuthenticationResponse): void {
    this.token.set(response.accessToken);
    this.user.set(response.principal);
  }

  private clear(): void {
    this.token.set(null);
    this.user.set(null);
  }
}
