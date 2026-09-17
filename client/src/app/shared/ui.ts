import { formatDate, formatNumber, formatPercent } from '@angular/common';
import { Component, inject, Injectable, input, output, Pipe, PipeTransform } from '@angular/core';
import { PresentableError } from '../core/api/problem';
import { I18nService, Language, TranslatePipe } from '../core/i18n/i18n.service';
import { ResourceKey } from '../core/i18n/resources';
import { PageState } from './page-state';

/** 17.5: destructive actions require confirmation. A service so that components stay testable. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly i18n = inject(I18nService);

  confirm(key: ResourceKey): boolean {
    return globalThis.confirm(this.i18n.t(key));
  }
}

/** UI-09: dates formatted according to the active locale; re-evaluates when the language changes. */
@Pipe({ name: 'localeDate', pure: false })
export class LocaleDatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: string | Date | null | undefined, format: 'date' | 'datetime' = 'date'): string {
    if (!value) {
      return '';
    }
    const locale = this.i18n.language() === 'ar' ? 'ar' : 'en-GB';
    return formatDate(value, format === 'date' ? 'mediumDate' : 'medium', locale);
  }
}

/**
 * UI-09: numbers formatted according to the active locale (grades, scores, weights, counts, sizes).
 * `percent` takes a 0–100 value and renders it with the locale's percent sign and placement;
 * the second argument caps the fraction digits (integers are never padded).
 */
@Pipe({ name: 'localeNumber', pure: false })
export class LocaleNumberPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number | null | undefined, style: 'number' | 'percent' = 'number', maxFractionDigits = 2): string {
    return formatLocaleNumber(this.i18n.language(), value, style, maxFractionDigits);
  }
}

/** The pipe's formatting as a plain function, for component code that composes a number with a unit. */
export function formatLocaleNumber(
  language: Language,
  value: number | null | undefined,
  style: 'number' | 'percent' = 'number',
  maxFractionDigits = 2,
): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '';
  }
  const locale = language === 'ar' ? 'ar' : 'en-GB';
  const digits = `1.0-${maxFractionDigits}`;
  return style === 'percent' ? formatPercent(value / 100, locale, digits) : formatNumber(value, locale, digits);
}

/** Picks the English or Arabic value of a bilingual pair for the active language. */
@Pipe({ name: 'bilingual', pure: false })
export class BilingualPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(entity: object | null | undefined, field: string): string {
    if (!entity) {
      return '';
    }
    const suffix = this.i18n.language() === 'ar' ? 'Ar' : 'En';
    const record = entity as Record<string, unknown>;
    const value = record[`${field}${suffix}`] ?? record[`${field}En`];
    return typeof value === 'string' ? value : '';
  }
}

/** Loading, empty and error states of a collection view (17.5), plus previous/next paging controls. */
@Component({
  selector: 'app-page-controls',
  imports: [TranslatePipe, LocaleNumberPipe],
  template: `
    @if (state().loading()) {
      <p class="text-secondary" role="status" data-testid="state-loading">{{ 'common.loading' | t }}</p>
    } @else if (state().error(); as error) {
      <div class="alert alert-danger" role="alert" data-testid="state-error">
        {{ 'common.loadFailed' | t }} {{ error.message }}
      </div>
    } @else if (state().isEmpty) {
      <p class="text-secondary" data-testid="state-empty">{{ 'common.empty' | t }}</p>
    }
    @if (state().totalPages() > 1) {
      <nav class="d-flex align-items-center gap-2 mt-2" [attr.aria-label]="'common.pagination' | t">
        <button class="btn btn-outline-secondary btn-sm" type="button" (click)="state().previous()" [disabled]="state().page() <= 1 || state().loading()" data-testid="page-previous">
          {{ 'common.previous' | t }}
        </button>
        <span class="small" data-testid="page-indicator">{{ state().page() | localeNumber }} / {{ state().totalPages() | localeNumber }} ({{ state().totalCount() | localeNumber }})</span>
        <button class="btn btn-outline-secondary btn-sm" type="button" (click)="state().next()" [disabled]="state().page() >= state().totalPages() || state().loading()" data-testid="page-next">
          {{ 'common.next' | t }}
        </button>
      </nav>
    }
  `,
})
export class PageControls {
  readonly state = input.required<PageState<unknown>>();
}

/** Validation messages associated with their field and announced to assistive technology (17.5). */
@Component({
  selector: 'app-field-errors',
  template: `
    @if (messages().length > 0) {
      <div class="invalid-feedback d-block" role="alert" [id]="id()">
        @for (message of messages(); track message) {
          <div>{{ message }}</div>
        }
      </div>
    }
  `,
})
export class FieldErrors {
  readonly messages = input<string[]>([]);
  readonly id = input<string>('');
}

/** The non-field outcome of a submission: a rule violation (422), conflict (409) or other failure. */
@Component({
  selector: 'app-submit-error',
  imports: [TranslatePipe],
  template: `
    @if (error(); as e) {
      @if (e.status === 422 || e.status === 409 || e.status === 404) {
        <div class="alert alert-warning" role="alert" data-testid="submit-error">
          @if (e.code) { <strong>{{ e.code }}</strong> — }
          {{ e.message }}
        </div>
      } @else if (e.status !== 400) {
        <div class="alert alert-danger" role="alert" data-testid="submit-error">{{ 'common.saveFailed' | t }}</div>
      } @else {
        <div class="alert alert-danger" role="alert" data-testid="submit-error">{{ 'common.validationFailed' | t }}</div>
      }
    }
  `,
})
export class SubmitError {
  readonly error = input<PresentableError | null>(null);
}

/** A search box that emits on submit, keeping list pages free of duplicate form plumbing. */
@Component({
  selector: 'app-search-box',
  imports: [TranslatePipe],
  template: `
    <form class="d-flex gap-2" role="search" (submit)="$event.preventDefault(); search.emit(value)">
      <label class="visually-hidden" [for]="id()">{{ 'common.search' | t }}</label>
      <input class="form-control form-control-sm" [id]="id()" type="search" [value]="value" (input)="value = $any($event.target).value" [placeholder]="'common.search' | t" />
      <button class="btn btn-outline-primary btn-sm" type="submit">{{ 'common.search' | t }}</button>
    </form>
  `,
})
export class SearchBox {
  readonly id = input('search');
  readonly search = output<string>();
  protected value = '';
}
