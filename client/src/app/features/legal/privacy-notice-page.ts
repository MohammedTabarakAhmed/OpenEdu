import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { Mark } from '../../shared/brand';
import { LocaleDatePipe } from '../../shared/ui';
import { OperatorDetailsBlock } from './operator';

/** A section of the notice: its resource-key stem, how many paragraphs and bullet points it has, and any table it carries. */
export interface PrivacySection {
  key: string;
  paragraphs: number;
  bullets: number;
  table?: 'data' | 'purpose' | 'retention';
}

/**
 * Privacy notice — optional scope added after the mandatory increments. A full policy in the shape a data-protection
 * review expects (scope, definitions, data inventory with sources, purposes with legal bases, automated decisions,
 * access, sharing and processors, cookies, security, retention schedule, rights, minors, location, logging,
 * incidents, changes, contact), every statement describing the system as built. Bilingual from the resources; the
 * operator's identity comes from `OPERATOR` and is shown as a visible placeholder until completed. Anonymous like
 * `/verify` so it can be read before signing in. The date is the notice's own revision date.
 */
@Component({
  imports: [RouterLink, TranslatePipe, LocaleDatePipe, Mark, OperatorDetailsBlock],
  template: `
    <div class="row justify-content-center">
      <div class="col-12 col-lg-9">
        <article class="card">
          <div class="card-body p-4">
            <p class="oc-brand mb-1"><app-mark [size]="28" /> {{ 'app.title' | t }}</p>
            <h1 class="h4 mb-0" data-testid="privacy-title">{{ 'privacy.title' | t }}</h1>
            <span class="oc-horizon" aria-hidden="true"></span>
            <p class="text-secondary small mb-1">{{ 'privacy.updated' | t }}: {{ revised | localeDate: 'date' }}</p>
            <p>{{ 'privacy.intro' | t }}</p>

            <div class="alert alert-secondary" role="note" data-testid="privacy-summary">
              <strong>{{ 'privacy.summary.title' | t }}.</strong> {{ 'privacy.summary.body' | t }}
            </div>

            <nav [attr.aria-label]="'privacy.title' | t" class="small mb-4">
              <ol class="mb-0">
                @for (s of sections; track s.key) {
                  <li><a [href]="'#privacy-' + s.key">{{ 'privacy.' + s.key + '.title' | t }}</a></li>
                }
              </ol>
            </nav>

            @for (s of sections; track s.key; let index = $index) {
              <section [id]="'privacy-' + s.key" [attr.data-testid]="'privacy-' + s.key">
                <h2 class="h6 mt-4">{{ index + 1 }}. {{ 'privacy.' + s.key + '.title' | t }}</h2>
                @for (p of range(s.paragraphs); track p) {
                  <p>{{ 'privacy.' + s.key + '.p' + p | t }}</p>
                }
                @if (s.bullets > 0) {
                  <ul>
                    @for (b of range(s.bullets); track b) {
                      <li>{{ 'privacy.' + s.key + '.b' + b | t }}</li>
                    }
                  </ul>
                }
                @switch (s.table) {
                  @case ('data') {
                    <div class="table-responsive">
                      <table class="table table-sm align-top" data-testid="privacy-data-table">
                        <thead><tr><th scope="col">{{ 'privacy.table.category' | t }}</th><th scope="col">{{ 'privacy.table.source' | t }}</th><th scope="col">{{ 'privacy.table.why' | t }}</th></tr></thead>
                        <tbody>
                          @for (i of range(dataRows); track i) {
                            <tr><td>{{ 'privacy.data.' + i + '.category' | t }}</td><td>{{ 'privacy.data.' + i + '.source' | t }}</td><td>{{ 'privacy.data.' + i + '.why' | t }}</td></tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  }
                  @case ('purpose') {
                    <div class="table-responsive">
                      <table class="table table-sm align-top" data-testid="privacy-purpose-table">
                        <thead><tr><th scope="col">{{ 'privacy.table.purpose' | t }}</th><th scope="col">{{ 'privacy.table.basis' | t }}</th></tr></thead>
                        <tbody>
                          @for (i of range(purposeRows); track i) {
                            <tr><td>{{ 'privacy.purpose.' + i + '.purpose' | t }}</td><td>{{ 'privacy.purpose.' + i + '.basis' | t }}</td></tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  }
                  @case ('retention') {
                    <div class="table-responsive">
                      <table class="table table-sm align-top" data-testid="privacy-retention-table">
                        <thead><tr><th scope="col">{{ 'privacy.table.category' | t }}</th><th scope="col">{{ 'privacy.table.period' | t }}</th></tr></thead>
                        <tbody>
                          @for (i of range(retentionRows); track i) {
                            <tr><td>{{ 'privacy.retention.' + i + '.category' | t }}</td><td>{{ 'privacy.retention.' + i + '.period' | t }}</td></tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  }
                }
              </section>
            }

            <app-operator-details />

            <p class="mt-4 mb-0"><a routerLink="/terms">{{ 'terms.title' | t }}</a> &middot; <a routerLink="/verify">{{ 'verify.title' | t }}</a></p>
          </div>
        </article>
      </div>
    </div>
  `,
})
export class PrivacyNoticePage {
  /** Revision date of the notice text (not the build date), so readers can tell whether they have seen this version. */
  protected readonly revised = '2026-09-17';

  protected readonly dataRows = 7;
  protected readonly purposeRows = 7;
  protected readonly retentionRows = 7;

  /** Order and shape of the notice; counts must match the resource keys (the spec verifies every key resolves). */
  readonly sections: PrivacySection[] = [
    { key: 'scope', paragraphs: 2, bullets: 0 },
    { key: 'definitions', paragraphs: 0, bullets: 5 },
    { key: 'collect', paragraphs: 1, bullets: 0, table: 'data' },
    { key: 'provide', paragraphs: 0, bullets: 3 },
    { key: 'purpose', paragraphs: 1, bullets: 0, table: 'purpose' },
    { key: 'automated', paragraphs: 2, bullets: 0 },
    { key: 'access', paragraphs: 1, bullets: 5 },
    { key: 'sharing', paragraphs: 3, bullets: 0 },
    { key: 'cookies', paragraphs: 3, bullets: 0 },
    { key: 'security', paragraphs: 1, bullets: 9 },
    { key: 'retention', paragraphs: 2, bullets: 0, table: 'retention' },
    { key: 'rights', paragraphs: 1, bullets: 8 },
    { key: 'minors', paragraphs: 1, bullets: 0 },
    { key: 'location', paragraphs: 1, bullets: 0 },
    { key: 'logging', paragraphs: 1, bullets: 0 },
    { key: 'breach', paragraphs: 1, bullets: 0 },
    { key: 'changes', paragraphs: 1, bullets: 0 },
    { key: 'contact', paragraphs: 1, bullets: 0 },
  ];

  protected range(count: number): number[] {
    return Array.from({ length: count }, (_, i) => i + 1);
  }
}
