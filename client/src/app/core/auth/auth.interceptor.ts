import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { IS_AUTH_FLOW, SessionService } from './session.service';

function withBearer<T>(request: HttpRequest<T>, token: string): HttpRequest<T> {
  return request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

/**
 * UI-03: attaches the access token to outbound requests and, on an authentication failure,
 * attempts a single refresh and replays the original request.
 * UI-04: concurrent failures share the one in-flight refresh held by SessionService.
 * UI-05: when the refresh fails the session is ended and the user returned to authentication.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionService);
  const isAuthFlow = request.context.get(IS_AUTH_FLOW);
  const token = session.accessToken();

  const outbound = token && !isAuthFlow ? withBearer(request, token) : request;

  return next(outbound).pipe(
    catchError((error: unknown) => {
      if (isAuthFlow || !(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return session.refresh().pipe(
        catchError(() => {
          session.endSession();
          return throwError(() => error);
        }),
        switchMap((fresh) => next(withBearer(request, fresh))),
      );
    }),
  );
};
