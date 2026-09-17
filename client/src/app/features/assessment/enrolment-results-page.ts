import { Component, inject, input, signal } from '@angular/core';
import { PresentableError, toPresentableError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { BilingualPipe, LocaleNumberPipe } from '../../shared/ui';
import { Certificate, CertificatesApi } from '../certificates/certificates.api';
import { BlobSaver } from '../content/content.api';
import { AssessmentApi } from './assessment.api';
import { LearnerResultsResponse } from './assessment.models';

/** A learner's own released results for one enrolment (15.3; BR-06): nothing until release, then the weighted final grade. */
@Component({
  imports: [TranslatePipe, BilingualPipe, LocaleNumberPipe],
  template: `
    @if (loading()) {
      <p class="text-secondary" role="status" data-testid="state-loading">{{ 'common.loading' | t }}</p>
    } @else if (loadError()) {
      <div class="alert alert-danger" role="alert" data-testid="state-error">{{ 'common.loadFailed' | t }}</div>
    } @else if (results(); as r) {
      <h2 class="h5 mb-1" data-testid="section-title"><code>{{ r.courseCode }}</code> {{ r | bilingual: 'courseName' }}</h2>
      <p class="small text-secondary mb-3">{{ r.sectionCode }} · {{ 'enrolments.status.' + r.status | t }}</p>

      @if (!r.isReleased) {
        <div class="alert alert-info" role="status" data-testid="not-released">{{ 'assessment.resultsNotReleased' | t }}</div>
      } @else {
        <table class="table table-sm" data-testid="results-table">
          <thead><tr><th scope="col">{{ 'assessment.component' | t }}</th><th scope="col">{{ 'assessment.score' | t }}</th></tr></thead>
          <tbody>
            @for (c of r.components; track c.id) {
              <tr>
                <td>{{ c | bilingual: 'name' }} ({{ c.weightPercent | localeNumber: 'percent' }})</td>
                <td>
                  @let entry = entryFor(r, c.id);
                  {{ (entry?.score | localeNumber) || '—' }} / {{ c.maxScore | localeNumber }}
                </td>
              </tr>
            }
          </tbody>
        </table>
        <p class="fw-semibold" data-testid="final-grade">{{ 'assessment.finalGrade' | t }}: {{ r.finalGrade ?? '—' }}</p>
        @if (certificate(); as c) {
          <button class="btn btn-outline-primary btn-sm" type="button" (click)="download(c)" [disabled]="busy()" data-testid="download-certificate">{{ 'certificates.download' | t }}</button>
        }
      }
    }
  `,
})
export class EnrolmentResultsPage {
  readonly id = input.required<string>();

  private readonly api = inject(AssessmentApi);
  private readonly certificatesApi = inject(CertificatesApi);
  private readonly saver = inject(BlobSaver);
  protected readonly results = signal<LearnerResultsResponse | null>(null);
  protected readonly certificate = signal<Certificate | null>(null);
  protected readonly busy = signal(false);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<PresentableError | null>(null);

  constructor() {
    queueMicrotask(() => {
      this.api.myResults(this.id()).subscribe({
        next: (r) => {
          this.results.set(r);
          this.loading.set(false);
          if (r.status === 'Completed') {
            // Only a completed enrolment can carry a certificate; the listing is the learner's own (SEC-12).
            this.certificatesApi.mine().subscribe({
              next: (items) => this.certificate.set(items.find((c) => c.enrolmentId === r.enrolmentId) ?? null),
              error: () => this.certificate.set(null),
            });
          }
        },
        error: (failure: unknown) => {
          this.loadError.set(toPresentableError(failure));
          this.loading.set(false);
        },
      });
    });
  }

  protected download(certificate: Certificate): void {
    this.busy.set(true);
    this.certificatesApi.download(certificate.id).subscribe({
      next: (blob) => {
        this.saver.save(blob, `certificate-${certificate.verificationCode}.pdf`);
        this.busy.set(false);
      },
      error: () => this.busy.set(false),
    });
  }

  protected entryFor(r: LearnerResultsResponse, componentId: string) {
    return r.entries.find((e) => e.gradeComponentId === componentId) ?? null;
  }
}
