/** Contracts of api/v1/auth (mirrors the server's dedicated response types, API-02). */

export interface Principal {
  id: string;
  userName: string;
  email: string;
  fullNameEn: string;
  fullNameAr: string;
  mfaEnabled: boolean;
  roles: string[];
  permissions: string[];
}

export interface AuthenticationResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  principal: Principal;
}

export interface LoginResponse {
  mfaRequired: boolean;
  challenge: string | null;
  authenticated: AuthenticationResponse | null;
}

export const Roles = {
  Administrator: 'Administrator',
  Registrar: 'Registrar',
  Instructor: 'Instructor',
  Learner: 'Learner',
} as const;

export type RoleName = (typeof Roles)[keyof typeof Roles];

/** The three role shells of SDD 17.1; Registrar is a restricted administrator and shares the admin shell. */
export function landingPathFor(roles: readonly string[]): string {
  if (roles.includes(Roles.Administrator) || roles.includes(Roles.Registrar)) {
    return '/admin';
  }
  if (roles.includes(Roles.Instructor)) {
    return '/instructor';
  }
  if (roles.includes(Roles.Learner)) {
    return '/learner';
  }
  return '/login';
}
