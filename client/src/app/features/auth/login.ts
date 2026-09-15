import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { landingPathFor } from '../../core/auth/auth.models';
import { SessionService } from '../../core/auth/session.service';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { ResourceKey } from '../../core/i18n/resources';

/** Credential step followed, where required, by the multi-factor step (SEC-09). */
@Component({
  imports: [FormsModule, TranslatePipe],
  templateUrl: './login.html',
})
export class Login {
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected userNameOrEmail = '';
  protected password = '';
  protected code = '';

  protected readonly challenge = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<ResourceKey | null>(null);
  protected readonly notice = signal<ResourceKey | null>(
    this.route.snapshot.queryParamMap.get('reason') === 'session-ended' ? 'login.sessionEnded' : null,
  );

  protected submitCredentials(): void {
    if (this.busy()) {
      return;
    }

    if (!this.userNameOrEmail.trim() || !this.password) {
      this.error.set('login.error.validation');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.session.login(this.userNameOrEmail.trim(), this.password).subscribe({
      next: (response) => {
        this.busy.set(false);
        if (response.mfaRequired) {
          this.challenge.set(response.challenge);
          this.password = '';
        } else {
          this.enter(response.authenticated!.principal.roles);
        }
      },
      error: (failure: unknown) => this.fail(failure),
    });
  }

  protected submitCode(): void {
    const challenge = this.challenge();
    if (this.busy() || !challenge) {
      return;
    }

    if (!/^[0-9]{6}$/.test(this.code)) {
      this.error.set('login.error.validation');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.session.verifyMfa(challenge, this.code).subscribe({
      next: (response) => {
        this.busy.set(false);
        this.enter(response.principal.roles);
      },
      error: (failure: unknown) => this.fail(failure),
    });
  }

  protected back(): void {
    this.challenge.set(null);
    this.code = '';
    this.error.set(null);
  }

  private enter(roles: string[]): void {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    void this.router.navigateByUrl(returnUrl && returnUrl.startsWith('/') ? returnUrl : landingPathFor(roles));
  }

  private fail(failure: unknown): void {
    this.busy.set(false);
    const status = failure instanceof HttpErrorResponse ? failure.status : 0;
    this.error.set(
      status === 401 ? 'login.error.invalidCredentials'
        : status === 429 ? 'login.error.rateLimited'
        : status === 400 ? 'login.error.validation'
        : 'login.error.unexpected',
    );
  }
}
