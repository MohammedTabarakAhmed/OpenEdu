import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { IS_AUTH_FLOW } from '../../core/auth/session.service';

/** Contracts of the self-registration interface (Increment 7); mirrors the server's dedicated request/response types (API-02). */
export type AccountType = 'Learner' | 'Instructor' | 'Registrar' | 'Administrator';

export const ACCOUNT_TYPES: AccountType[] = ['Learner', 'Instructor', 'Registrar', 'Administrator'];

export interface RegisterRequest {
  accountType: AccountType;
  userName: string;
  email: string;
  password: string;
  fullNameEn: string;
  fullNameAr: string;
}

export interface RegistrationAccepted {
  message: string;
}

export interface VerifyEmailResponse {
  outcome: 'Activated' | 'AwaitingApproval';
  requestedRole: string;
}

/** Every call is anonymous: marked as part of the auth flow so a 401 never triggers a session refresh or a redirect. */
@Injectable({ providedIn: 'root' })
export class RegistrationApi {
  private readonly http = inject(HttpClient);

  private readonly options = () => ({ context: new HttpContext().set(IS_AUTH_FLOW, true) });

  register(body: RegisterRequest): Observable<RegistrationAccepted> {
    return this.http.post<RegistrationAccepted>('/api/v1/registration', body, this.options());
  }

  verifyEmail(token: string): Observable<VerifyEmailResponse> {
    return this.http.post<VerifyEmailResponse>('/api/v1/registration/verify-email', { token }, this.options());
  }

  resend(email: string): Observable<RegistrationAccepted> {
    return this.http.post<RegistrationAccepted>('/api/v1/registration/resend-verification', { email }, this.options());
  }
}
