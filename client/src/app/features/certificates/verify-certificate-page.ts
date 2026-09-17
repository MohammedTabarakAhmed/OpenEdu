import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { PresentableError, toPresentableError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { Mark } from '../../shared/brand';
import { BilingualPipe, LocaleDatePipe } from '../../shared/ui';
import { CertificateVerification, CertificatesApi } from './certificates.api';

/**
 * Anonymous certificate verification (15.3, 15.4): anyone holding a printed code confirms what it certifies. Reachable
 * without a session at `/verify` and, from a link, `/verify/:code`. An unknown code is reported as such — the API
 * answers 404 and nothing else — so the page discloses only what the paper already states.
 */
@Component({
  imports: [FormsModule, TranslatePipe, BilingualPipe, LocaleDatePipe, Mark],
  template: `
    <div class="row justify-content-center">
      <div class="col-12 col-md-8 col-lg-6">
        <div class="card">
          <div class="card-body p-4">
            <p class="oc-brand mb-1"><app-mark [size]="28" /> {{ 'app.title' | t }}</p>
            <h1 class="h4 mb-0">{{ 'verify.title' | t }}</h1>
            <span class="oc-horizon" aria-hidden="true"></span>
            <p class="text-secondary small">{{ 'verify.intro' | t }}</p>

            <form (ngSubmit)="submit()" novalidate data-testid="verify-form">
              <div class="mb-3">
                <label class="form-label" for="verify-code">{{ 'certificates.verificationCode' | t }}</label>
                <input class="form-control font-monospace" id="verify-code" name="code" autocomplete="off" spellcheck="false"
                       [placeholder]="'verify.codePlaceholder' | t" maxlength="32" required [(ngModel)]="entered" [disabled]="busy()" data-testid="verify-code" />
              </div>
              <button class="btn btn-primary" type="submit" [disabled]="busy() || !entered.trim()" data-testid="verify-submit">{{ 'verify.submit' | t }}</button>
            </form>

            @if (busy()) {
              <p class="text-secondary mt-3" role="status" data-testid="state-loading">{{ 'common.loading' | t }}</p>
            } @else if (notFound()) {
              <div class="alert alert-warning mt-3" role="alert" data-testid="verify-not-found">{{ 'verify.notFound' | t }}</div>
            } @else if (failure()) {
              <div class="alert alert-danger mt-3" role="alert" data-testid="state-error">{{ 'common.loadFailed' | t }}</div>
            } @else if (result(); as r) {
              <div class="alert alert-success mt-3" role="status" data-testid="verify-valid">{{ 'verify.valid' | t }}</div>
              <dl class="row mb-0" data-testid="verify-result">
                <dt class="col-sm-4">{{ 'verify.learner' | t }}</dt><dd class="col-sm-8" data-testid="verify-learner">{{ r | bilingual: 'learnerFullName' }}</dd>
                <dt class="col-sm-4">{{ 'courses.title' | t }}</dt><dd class="col-sm-8"><code>{{ r.courseCode }}</code> {{ r | bilingual: 'courseName' }}</dd>
                <dt class="col-sm-4">{{ 'programmes.title' | t }}</dt><dd class="col-sm-8">{{ r | bilingual: 'programmeName' }}</dd>
                <dt class="col-sm-4">{{ 'sections.term' | t }}</dt><dd class="col-sm-8">{{ r.term }}</dd>
                <dt class="col-sm-4">{{ 'verify.completedAt' | t }}</dt><dd class="col-sm-8">{{ r.completedAtUtc | localeDate: 'date' }}</dd>
                <dt class="col-sm-4">{{ 'certificates.issuedAt' | t }}</dt><dd class="col-sm-8">{{ r.issuedAtUtc | localeDate: 'date' }}</dd>
                <dt class="col-sm-4">{{ 'certificates.verificationCode' | t }}</dt><dd class="col-sm-8"><code>{{ r.verificationCode }}</code></dd>
              </dl>
            }
          </div>
        </div>
      </div>
    </div>
  `,
})
export class VerifyCertificatePage {
  /** Optional code from the route, so a link on the certificate can open the answer directly. */
  readonly code = input<string | undefined>();

  private readonly api = inject(CertificatesApi);
  private readonly router = inject(Router);

  protected entered = '';
  protected readonly busy = signal(false);
  protected readonly notFound = signal(false);
  protected readonly failure = signal<PresentableError | null>(null);
  protected readonly result = signal<CertificateVerification | null>(null);

  constructor() {
    queueMicrotask(() => {
      const fromRoute = this.code();
      if (fromRoute) {
        this.entered = fromRoute;
        this.lookUp(fromRoute);
      }
    });
  }

  protected submit(): void {
    const code = this.entered.trim();
    if (!code || this.busy()) {
      return;
    }
    // Keep the address shareable: the code becomes part of the URL without reloading the page.
    void this.router.navigate(['/verify', code], { replaceUrl: true });
    this.lookUp(code);
  }

  private lookUp(code: string): void {
    this.busy.set(true);
    this.notFound.set(false);
    this.failure.set(null);
    this.result.set(null);
    this.api.verify(code).subscribe({
      next: (r) => {
        this.result.set(r);
        this.busy.set(false);
      },
      error: (failure: unknown) => {
        const presentable = toPresentableError(failure);
        if (presentable.status === 404) {
          this.notFound.set(true);
        } else {
          this.failure.set(presentable);
        }
        this.busy.set(false);
      },
    });
  }
}
