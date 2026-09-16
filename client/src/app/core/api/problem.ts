import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from './api.models';

/** A failure translated for presentation: field-keyed messages for 400s, a message for everything else (17.5). */
export interface PresentableError {
  status: number;
  /** The rule reference for 422 (e.g. "BR-01") or the error code for other problems. */
  code: string | null;
  message: string | null;
  fieldErrors: Record<string, string[]>;
}

export function toPresentableError(failure: unknown): PresentableError {
  if (!(failure instanceof HttpErrorResponse)) {
    return { status: 0, code: null, message: null, fieldErrors: {} };
  }

  const problem = (typeof failure.error === 'object' && failure.error !== null ? failure.error : {}) as ProblemDetails;
  return {
    status: failure.status,
    code: problem.title ?? null,
    message: problem.detail ?? null,
    fieldErrors: problem.errors ?? {},
  };
}

/** Field keys arrive PascalCase from the server contract; forms use camelCase control names. */
export function errorsFor(error: PresentableError | null, control: string): string[] {
  if (!error) {
    return [];
  }
  const pascal = control.charAt(0).toUpperCase() + control.slice(1);
  return error.fieldErrors[pascal] ?? error.fieldErrors[control] ?? [];
}
