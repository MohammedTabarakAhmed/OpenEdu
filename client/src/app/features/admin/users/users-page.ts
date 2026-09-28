import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { PresentableError, errorsFor, toPresentableError } from '../../../core/api/problem';
import { SessionService } from '../../../core/auth/session.service';
import { TranslatePipe } from '../../../core/i18n/i18n.service';
import { PageState } from '../../../shared/page-state';
import { BilingualPipe, ConfirmService, FieldErrors, LocaleDatePipe, PageControls, SearchBox, SubmitError } from '../../../shared/ui';
import { UsersApi } from '../admin.api';
import { RegistrationStatus, Role, User, UserSession } from '../admin.models';

/**
 * User administration screens over the Increment 2 interface (15.3): listing, creation, amendment,
 * role assignment, logical deactivation and session revocation. Each control is shown only with its permission (17.3).
 */
@Component({
  imports: [ReactiveFormsModule, TranslatePipe, BilingualPipe, LocaleDatePipe, PageControls, FieldErrors, SubmitError, SearchBox],
  templateUrl: './users-page.html',
})
export class UsersPage {
  private readonly api = inject(UsersApi);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);
  protected readonly session = inject(SessionService);

  protected search = '';
  protected registrationFilter: RegistrationStatus | '' = '';
  protected readonly registrationFilters: RegistrationStatus[] = ['AwaitingVerification', 'AwaitingApproval'];
  protected readonly state = new PageState<User>((page, pageSize) =>
    this.api.list({ page, pageSize, search: this.search, sort: 'userName', registrationStatus: this.registrationFilter || undefined }));
  protected readonly roles = signal<Role[]>([]);

  protected readonly editing = signal<User | null>(null);
  protected readonly showForm = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);

  protected readonly sessionsOf = signal<User | null>(null);
  protected readonly sessions = signal<UserSession[]>([]);

  protected readonly form = this.fb.nonNullable.group({
    userName: ['', [Validators.required, Validators.maxLength(64), Validators.pattern(/^[A-Za-z0-9._-]+$/)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.minLength(12)]],
    fullNameEn: ['', [Validators.required, Validators.maxLength(200)]],
    fullNameAr: ['', [Validators.required, Validators.maxLength(200)]],
    roles: this.fb.nonNullable.control<string[]>([]),
  });

  constructor() {
    this.state.load();
    this.api.roles().subscribe((roles) => this.roles.set(roles));
  }

  protected errors(control: string): string[] {
    return errorsFor(this.error(), control);
  }

  protected onSearch(term: string): void {
    this.search = term;
    this.state.load(1);
  }

  protected onRegistrationFilter(value: string): void {
    this.registrationFilter = value as RegistrationStatus | '';
    this.state.load(1);
  }

  protected isPending(user: User): boolean {
    return user.registrationStatus === 'AwaitingVerification' || user.registrationStatus === 'AwaitingApproval';
  }

  /** Approval grants the requested role and activates the account (identity.role.assign). */
  protected approve(user: User): void {
    if (!this.confirm.confirm('users.confirmApprove')) {
      return;
    }
    this.api.approveRegistration(user.id).subscribe({
      next: () => this.state.reload(),
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }

  protected reject(user: User): void {
    if (!this.confirm.confirm('users.confirmReject')) {
      return;
    }
    this.api.rejectRegistration(user.id).subscribe({
      next: () => this.state.reload(),
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.error.set(null);
    this.form.reset({ userName: '', email: '', password: '', fullNameEn: '', fullNameAr: '', roles: [] });
    this.form.controls.userName.enable();
    this.form.controls.password.enable();
    this.showForm.set(true);
  }

  protected startEdit(user: User): void {
    this.editing.set(user);
    this.error.set(null);
    this.form.reset({ userName: user.userName, email: user.email, password: '', fullNameEn: user.fullNameEn, fullNameAr: user.fullNameAr, roles: [...user.roles] });
    this.form.controls.userName.disable();
    this.form.controls.password.disable();
    this.showForm.set(true);
  }

  protected cancel(): void {
    this.showForm.set(false);
    this.editing.set(null);
  }

  protected hasRole(name: string): boolean {
    return this.form.controls.roles.value.includes(name);
  }

  protected toggleRole(name: string, checked: boolean): void {
    const current = this.form.controls.roles.value.filter((r) => r !== name);
    this.form.controls.roles.setValue(checked ? [...current, name] : current);
  }

  protected submit(): void {
    if (this.busy() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    const editing = this.editing();
    this.busy.set(true);
    this.error.set(null);

    const finish = () => {
      this.busy.set(false);
      this.showForm.set(false);
      this.state.reload();
    };
    const fail = (failure: unknown) => {
      this.busy.set(false);
      this.error.set(toPresentableError(failure));
    };

    if (!editing) {
      this.api.create({ userName: v.userName, email: v.email, password: v.password, fullNameEn: v.fullNameEn, fullNameAr: v.fullNameAr, roles: v.roles }).subscribe({ next: finish, error: fail });
      return;
    }

    // Amendment and role replacement are two capabilities with two permissions; apply what the user may.
    this.api.update(editing.id, { email: v.email, fullNameEn: v.fullNameEn, fullNameAr: v.fullNameAr }).subscribe({
      next: () => {
        if (this.session.hasPermission('identity.role.assign')) {
          this.api.assignRoles(editing.id, v.roles).subscribe({ next: finish, error: fail });
        } else {
          finish();
        }
      },
      error: fail,
    });
  }

  protected toggleActive(user: User): void {
    if (!this.confirm.confirm(user.isActive ? 'users.confirmDeactivate' : 'users.confirmActivate')) {
      return;
    }
    (user.isActive ? this.api.deactivate(user.id) : this.api.activate(user.id)).subscribe({
      next: () => this.state.reload(),
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }

  protected showSessions(user: User): void {
    this.sessionsOf.set(user);
    this.api.sessions(user.id).subscribe({
      next: (sessions) => this.sessions.set(sessions),
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }

  protected revokeSession(sessionId: string): void {
    const user = this.sessionsOf();
    if (!user || !this.confirm.confirm('users.confirmRevoke')) {
      return;
    }
    this.api.revokeSession(user.id, sessionId).subscribe({ next: () => this.showSessions(user), error: (failure: unknown) => this.error.set(toPresentableError(failure)) });
  }

  protected revokeAll(): void {
    const user = this.sessionsOf();
    if (!user || !this.confirm.confirm('users.confirmRevoke')) {
      return;
    }
    this.api.revokeAllSessions(user.id).subscribe({ next: () => this.showSessions(user), error: (failure: unknown) => this.error.set(toPresentableError(failure)) });
  }
}
