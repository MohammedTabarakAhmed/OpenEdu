import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { ResourceKey } from '../../core/i18n/resources';
import { Mark } from '../../shared/brand';
import { RegistrationApi, VerifyEmailResponse } from './registration.api';

/**
 * Landing page of the verification link (Increment 7). The token arrives as a query parameter and is only sent when
 * the person presses the button — never on load — because mail scanners pre-fetch links and would otherwise burn the
 * single-use token. An unusable link (unknown, used, expired) offers a resend form; the resend answer is always the
 * same, so the page discloses nothing about the address.
 */
@Component({
  imports: [FormsModule, RouterLink, TranslatePipe, Mark],
  template: `
    <div class="row justify-content-center">
      <div class="col-12 col-md-8 col-lg-6">
        <div class="card">
          <div class="card-body p-4">
            <p class="oc-brand mb-1"><app-mark [size]="28" /> {{ 'app.title' | t }}</p>
            <h1 class="h4 mb-0">{{ 'verifyEmail.title' | t }}</h1>
            <span class="oc-horizon" aria-hidden="true"></span>

            @if (!token()) {
              <div class="alert alert-warning" role="alert" data-testid="verify-email-missing">{{ 'verifyEmail.missing' | t }}</div>
            } @else if (result(); as r) {
              @if (r.outcome === 'Activated') {
                <div class="alert alert-success" role="status" data-testid="verify-email-activated">{{ 'verifyEmail.activated' | t }}</div>
                <a routerLink="/login" class="btn btn-primary" data-testid="verify-email-login">{{ 'login.title' | t }}</a>
              } @else {
                <div class="alert alert-info" role="status" data-testid="verify-email-awaiting">
                  {{ 'verifyEmail.awaitingApproval' | t }} <strong>{{ 'register.role.' + r.requestedRole + '.title' | t }}</strong>. {{ 'verifyEmail.awaitingApprovalNext' | t }}
                </div>
                <a routerLink="/login" class="btn btn-link btn-sm" data-testid="verify-email-login">{{ 'register.sent.toLogin' | t }}</a>
              }
            } @else if (invalid()) {
              <div class="alert alert-warning" role="alert" data-testid="verify-email-invalid">{{ 'verifyEmail.invalid' | t }}</div>
              @if (resent()) {
                <p class="small text-secondary" role="status" data-testid="verify-email-resent">{{ 'verifyEmail.resent' | t }}</p>
              } @else {
                <form (ngSubmit)="resend()" novalidate data-testid="verify-email-resend-form">
                  <label class="form-label" for="verify-email-address">{{ 'verifyEmail.resendLabel' | t }}</label>
                  <div class="d-flex gap-2">
                    <input class="form-control" id="verify-email-address" name="email" type="email" autocomplete="email" dir="ltr" required [(ngModel)]="email" [disabled]="busy()" />
                    <button class="btn btn-outline-primary text-nowrap" type="submit" [disabled]="busy() || !email.trim()" data-testid="verify-email-resend">{{ 'register.sent.resend' | t }}</button>
                  </div>
                </form>
              }
            } @else {
              @if (errorKey(); as key) {
                <div class="alert alert-danger" role="alert" data-testid="verify-email-error">{{ key | t }}</div>
              }
              <p class="text-secondary">{{ 'verifyEmail.intro' | t }}</p>
              <button class="btn btn-primary" type="button" (click)="verify()" [disabled]="busy()" data-testid="verify-email-submit">{{ 'verifyEmail.submit' | t }}</button>
            }
          </div>
        </div>
      </div>
    </div>
  `,
})
export class VerifyEmailPage {
  private readonly api = inject(RegistrationApi);

  /** Bound from `?token=` by withComponentInputBinding. */
  readonly token = input<string | undefined>();

  protected email = '';
  protected readonly busy = signal(false);
  protected readonly result = signal<VerifyEmailResponse | null>(null);
  protected readonly invalid = signal(false);
  protected readonly resent = signal(false);
  protected readonly errorKey = signal<ResourceKey | null>(null);

  protected verify(): void {
    const token = this.token();
    if (this.busy() || !token) {
      return;
    }
    this.busy.set(true);
    this.errorKey.set(null);
    this.api.verifyEmail(token).subscribe({
      next: (response) => {
        this.busy.set(false);
        this.result.set(response);
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        const status = failure instanceof HttpErrorResponse ? failure.status : 0;
        if (status === 404 || status === 400) {
          this.invalid.set(true);
        } else {
          this.errorKey.set(status === 429 ? 'login.error.rateLimited' : 'common.loadFailed');
        }
      },
    });
  }

  protected resend(): void {
    const email = this.email.trim();
    if (this.busy() || !email) {
      return;
    }
    this.busy.set(true);
    this.api.resend(email).subscribe({
      next: () => {
        this.busy.set(false);
        this.resent.set(true);
      },
      error: () => {
        // The answer is uniform by design; a failure here is reported the same way so nothing is disclosed.
        this.busy.set(false);
        this.resent.set(true);
      },
    });
  }
}
