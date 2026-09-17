import { Component, inject } from '@angular/core';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n.service';

/**
 * Identity of the institution operating this instance — the data controller and the party to the terms of use.
 * Deliberately not invented: every field is `null` until the operator fills it in, and the page then shows a visible
 * "to be completed" marker rather than a plausible-looking fiction. Replace the values here (or read them from
 * configuration) before the system is used with real learners.
 */
export interface OperatorDetails {
  legalNameEn: string | null;
  legalNameAr: string | null;
  licenceNumber: string | null;
  addressEn: string | null;
  addressAr: string | null;
  email: string | null;
  phone: string | null;
  dataProtectionContact: string | null;
  /** Country and emirate / region whose law governs, e.g. "United Arab Emirates, Dubai". */
  jurisdictionEn: string | null;
  jurisdictionAr: string | null;
}

export const OPERATOR: OperatorDetails = {
  legalNameEn: null,
  legalNameAr: null,
  licenceNumber: null,
  addressEn: null,
  addressAr: null,
  email: null,
  phone: null,
  dataProtectionContact: null,
  jurisdictionEn: null,
  jurisdictionAr: null,
};

/** The operator block shared by the privacy notice and the terms of use. */
@Component({
  selector: 'app-operator-details',
  imports: [TranslatePipe],
  template: `
    <h2 class="h6">{{ 'legal.operator.title' | t }}</h2>
    <p>{{ 'legal.operator.intro' | t }}</p>
    <dl class="oc-details" data-testid="operator-details">
      <dt>{{ 'legal.operator.name' | t }}</dt><dd>{{ value(operator.legalNameEn, operator.legalNameAr) }}</dd>
      <dt>{{ 'legal.operator.licence' | t }}</dt><dd>{{ value(operator.licenceNumber) }}</dd>
      <dt>{{ 'legal.operator.address' | t }}</dt><dd>{{ value(operator.addressEn, operator.addressAr) }}</dd>
      <dt>{{ 'legal.operator.email' | t }}</dt><dd dir="ltr">{{ value(operator.email) }}</dd>
      <dt>{{ 'legal.operator.phone' | t }}</dt><dd dir="ltr">{{ value(operator.phone) }}</dd>
      <dt>{{ 'legal.operator.dpo' | t }}</dt><dd>{{ value(operator.dataProtectionContact) }}</dd>
      <dt>{{ 'terms.law.title' | t }}</dt><dd>{{ value(operator.jurisdictionEn, operator.jurisdictionAr) }}</dd>
    </dl>
    <p class="small text-secondary mt-4 mb-0">{{ 'legal.review' | t }}</p>
  `,
})
export class OperatorDetailsBlock {
  protected readonly operator = OPERATOR;
  private readonly i18n = inject(I18nService);

  protected value(en: string | null, ar?: string | null): string {
    const chosen = this.i18n.language() === 'ar' ? (ar ?? en) : en;
    return chosen?.trim() ? chosen : `[${this.i18n.t('legal.operator.placeholder')}]`;
  }
}
