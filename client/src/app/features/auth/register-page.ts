import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PresentableError, errorsFor, toPresentableError } from '../../core/api/problem';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n.service';
import { ResourceKey } from '../../core/i18n/resources';
import { Mark } from '../../shared/brand';
import { FieldErrors, SubmitError } from '../../shared/ui';
import { ACCOUNT_TYPES, AccountType, RegistrationApi } from './registration.api';

/** The server's minimum (PasswordRules.MinimumLength); the strength hint is advisory, the server's policy is the rule. */
export const PASSWORD_MIN_LENGTH = 12;

/** Cross-field check: both password boxes must agree before the form can be submitted. */
export function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const password = group.get('password')?.value;
  const confirm = group.get('confirmPassword')?.value;
  return password && confirm && password !== confirm ? { passwordMismatch: true } : null;
}

/** Length plus character variety, no dependency: weak below the minimum, strong from 16 characters with three classes. */
export function passwordStrength(password: string): 'weak' | 'fair' | 'strong' {
  if (password.length < PASSWORD_MIN_LENGTH) {
    return 'weak';
  }
  const classes = [/[a-z]/, /[A-Z]/, /[0-9]/, /[^A-Za-z0-9]/].filter((c) => c.test(password)).length;
  return password.length >= 16 && classes >= 3 ? 'strong' : 'fair';
}

/**
 * Create an account (Increment 7). Step one asks what the account is for — the four roles of 2.3 — step two takes
 * the details, step three says "check your inbox". A learner activates itself by verifying the address; the three
 * staff roles are told an administrator must approve. The answer to a submitted form is the same whether or not the
 * address was new, so this page can never reveal who has an account.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, Mark, FieldErrors, SubmitError],
  templateUrl: './register-page.html',
})
export class RegisterPage {
  private readonly api = inject(RegistrationApi);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);

  protected readonly accountTypes = ACCOUNT_TYPES;
  protected readonly step = signal<'role' | 'form' | 'sent'>('role');
  protected readonly accountType = signal<AccountType | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);
  protected readonly errorKey = signal<ResourceKey | null>(null);
  protected readonly sentTo = signal('');
  protected readonly resendIn = signal(0);
  protected readonly resent = signal(false);
  protected readonly password = signal('');
  protected readonly strength = computed(() => passwordStrength(this.password()));

  protected readonly form = this.fb.nonNullable.group(
    {
      userName: ['', [Validators.required, Validators.maxLength(64), Validators.pattern(/^[A-Za-z0-9._-]+$/)]],
      email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
      password: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(128)]],
      confirmPassword: ['', [Validators.required]],
      fullNameEn: ['', [Validators.required, Validators.maxLength(200)]],
      fullNameAr: ['', [Validators.required, Validators.maxLength(200)]],
    },
    { validators: [passwordsMatch] },
  );

  private resendTimer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    this.form.controls.password.valueChanges.subscribe((value) => this.password.set(value));
    this.destroyRef.onDestroy(() => this.stopCountdown());
  }

  protected needsApproval(type: AccountType): boolean {
    return type !== 'Learner';
  }

  protected choose(type: AccountType): void {
    this.accountType.set(type);
    this.error.set(null);
    this.errorKey.set(null);
    this.step.set('form');
  }

  protected back(): void {
    this.step.set('role');
    this.error.set(null);
    this.errorKey.set(null);
  }

  protected errors(control: string): string[] {
    return errorsFor(this.error(), control);
  }

  protected invalid(control: 'userName' | 'email' | 'password' | 'confirmPassword' | 'fullNameEn' | 'fullNameAr'): boolean {
    const c = this.form.controls[control];
    return this.errors(control).length > 0 || (c.touched && c.invalid) || (control === 'confirmPassword' && c.touched && this.form.hasError('passwordMismatch'));
  }

  protected submit(): void {
    const type = this.accountType();
    if (this.busy() || !type) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.errorKey.set(null);
    this.api
      .register({ accountType: type, userName: v.userName.trim(), email: v.email.trim(), password: v.password, fullNameEn: v.fullNameEn.trim(), fullNameAr: v.fullNameAr.trim() })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.sentTo.set(v.email.trim());
          this.step.set('sent');
          this.startCountdown();
        },
        error: (failure: unknown) => this.fail(failure),
      });
  }

  protected resend(): void {
    if (this.resendIn() > 0 || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.api.resend(this.sentTo()).subscribe({
      next: () => {
        this.busy.set(false);
        this.resent.set(true);
        this.startCountdown();
      },
      error: (failure: unknown) => this.fail(failure),
    });
  }

  private fail(failure: unknown): void {
    this.busy.set(false);
    const status = failure instanceof HttpErrorResponse ? failure.status : 0;
    const presentable = toPresentableError(failure);
    if (status === 404 && presentable.code === 'registration.disabled') {
      this.errorKey.set('register.error.disabled');
    } else if (status === 429) {
      this.errorKey.set('login.error.rateLimited');
    } else if (status === 409) {
      // A taken user name is the one clash the server reports; show it under the field, in the user's language.
      this.error.set({ status: 400, code: null, message: null, fieldErrors: { userName: [this.i18n.t('register.error.userNameTaken')] } });
    } else {
      this.error.set(presentable);
    }
  }

  private startCountdown(): void {
    this.stopCountdown();
    this.resendIn.set(60);
    this.resendTimer = setInterval(() => {
      const left = this.resendIn() - 1;
      this.resendIn.set(left);
      if (left <= 0) {
        this.stopCountdown();
      }
    }, 1000);
  }

  private stopCountdown(): void {
    if (this.resendTimer !== null) {
      clearInterval(this.resendTimer);
      this.resendTimer = null;
    }
  }
}
