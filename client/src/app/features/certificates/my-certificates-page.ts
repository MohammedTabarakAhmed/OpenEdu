import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PresentableError, toPresentableError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { BlobSaver } from '../content/content.api';
import { BilingualPipe, LocaleDatePipe, LocaleNumberPipe, SubmitError } from '../../shared/ui';
import { Certificate, CertificatesApi } from './certificates.api';

/** Certificate listing and download for the calling learner (15.3 "certificate listing/download"). */
@Component({
  imports: [RouterLink, TranslatePipe, BilingualPipe, LocaleDatePipe, SubmitError, LocaleNumberPipe],
  template: `
    <h2 class="h5 mb-3">{{ 'certificates.title' | t }}</h2>
    <app-submit-error [error]="error()" />

    @if (loading()) {
      <p class="text-secondary" role="status" data-testid="state-loading">{{ 'common.loading' | t }}</p>
    } @else if (loadError()) {
      <div class="alert alert-danger" role="alert" data-testid="state-error">{{ 'common.loadFailed' | t }}</div>
    } @else if (items().length === 0) {
      <p class="text-secondary" data-testid="state-empty">{{ 'certificates.none' | t }}</p>
    } @else {
      <table class="table table-sm align-middle" data-testid="certificates-table">
        <thead>
          <tr>
            <th scope="col">{{ 'courses.title' | t }}</th>
            <th scope="col">{{ 'sections.term' | t }}</th>
            <th scope="col">{{ 'transcript.finalGrade' | t }}</th>
            <th scope="col">{{ 'certificates.issuedAt' | t }}</th>
            <th scope="col">{{ 'certificates.verificationCode' | t }}</th>
            <th scope="col"></th>
          </tr>
        </thead>
        <tbody>
          @for (c of items(); track c.id) {
            <tr [attr.data-testid]="'certificate-' + c.id">
              <td><code>{{ c.courseCode }}</code> {{ c | bilingual: 'courseName' }}</td>
              <td>{{ c.term }}</td>
              <td>{{ c.finalGrade | localeNumber }}</td>
              <td>{{ c.issuedAtUtc | localeDate: 'date' }}</td>
              <td><code>{{ c.verificationCode }}</code></td>
              <td class="text-end">
                <button class="btn btn-outline-primary btn-sm" type="button" (click)="download(c)" [disabled]="busy()" [attr.data-testid]="'download-' + c.id">
                  {{ 'certificates.download' | t }}
                </button>
              </td>
            </tr>
          }
        </tbody>
      </table>
      <p class="small text-secondary">{{ 'certificates.verifyHint' | t }} <a routerLink="/verify">/verify</a></p>
    }
  `,
})
export class MyCertificatesPage {
  private readonly api = inject(CertificatesApi);
  private readonly saver = inject(BlobSaver);

  protected readonly items = signal<Certificate[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<PresentableError | null>(null);
  protected readonly error = signal<PresentableError | null>(null);
  protected readonly busy = signal(false);

  constructor() {
    this.api.mine().subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loadError.set(toPresentableError(failure));
        this.loading.set(false);
      },
    });
  }

  protected download(certificate: Certificate): void {
    this.busy.set(true);
    this.error.set(null);
    this.api.download(certificate.id).subscribe({
      next: (blob) => {
        this.saver.save(blob, `certificate-${certificate.verificationCode}.pdf`);
        this.busy.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(toPresentableError(failure));
        this.busy.set(false);
      },
    });
  }
}
