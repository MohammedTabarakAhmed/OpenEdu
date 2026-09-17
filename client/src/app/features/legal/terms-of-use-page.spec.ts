import { registerLocaleData } from '@angular/common';
import localeAr from '@angular/common/locales/ar';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { I18nService } from '../../core/i18n/i18n.service';
import { ar, en } from '../../core/i18n/resources';
import { expectAccessibleControls } from '../../shared/accessibility.spec-support';
import { TermsOfUsePage } from './terms-of-use-page';

describe('TermsOfUsePage', () => {
  beforeAll(() => registerLocaleData(localeAr));
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({ imports: [TermsOfUsePage], providers: [provideRouter([])] }).compileComponents();
  });

  it('renders the ten sections, the operator block with visible placeholders, and switches language', () => {
    const fixture = TestBed.createComponent(TermsOfUsePage);
    fixture.detectChanges();
    const text = (): string => fixture.nativeElement.textContent;

    expect(text()).toContain(en['terms.fees.body']);
    expect(text()).toContain(en['terms.comms.body']);
    expect(text()).toContain(en['terms.claims.body']);
    expect(fixture.nativeElement.querySelectorAll('[data-testid^="terms-"]').length).toBe(11); // title + 10 sections
    // Nothing is invented: unfilled operator fields are shown as an explicit placeholder, never as a plausible value.
    expect(fixture.nativeElement.querySelector('[data-testid="operator-details"]').textContent).toContain(en['legal.operator.placeholder']);
    expect(text()).toContain(en['legal.review']);
    expectAccessibleControls(fixture.nativeElement);

    TestBed.inject(I18nService).use('ar');
    fixture.detectChanges();

    expect(text()).toContain(ar['terms.fees.body']);
    expect(text()).toContain(ar['legal.operator.placeholder']);
  });
});
