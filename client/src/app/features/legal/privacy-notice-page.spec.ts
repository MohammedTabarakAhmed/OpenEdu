import { registerLocaleData } from '@angular/common';
import localeAr from '@angular/common/locales/ar';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { I18nService } from '../../core/i18n/i18n.service';
import { ar, en, ResourceKey } from '../../core/i18n/resources';
import { expectAccessibleControls } from '../../shared/accessibility.spec-support';
import { PrivacyNoticePage } from './privacy-notice-page';

describe('PrivacyNoticePage', () => {
  beforeAll(() => registerLocaleData(localeAr));
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({ imports: [PrivacyNoticePage], providers: [provideRouter([])] }).compileComponents();
  });

  it('renders every section, table and key in the active language and follows a language switch', () => {
    const fixture = TestBed.createComponent(PrivacyNoticePage);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const text = (): string => root.textContent ?? '';

    // Every section declared by the page resolves to a real resource — an unknown key would render as the key itself.
    expect(root.querySelectorAll('section').length).toBe(18);
    expect(text()).not.toMatch(/privacy\.[a-z]+\.(title|p\d|b\d)/);
    for (const section of fixture.componentInstance.sections) {
      expect(text()).toContain(en[`privacy.${section.key}.title` as ResourceKey]);
    }
    expect(root.querySelector('[data-testid="privacy-data-table"] tbody')?.children.length).toBe(7);
    expect(root.querySelector('[data-testid="privacy-purpose-table"] tbody')?.children.length).toBe(7);
    expect(root.querySelector('[data-testid="privacy-retention-table"] tbody')?.children.length).toBe(7);
    expect(text()).toContain(en['privacy.cookies.p1']);
    expect(text()).toContain(en['privacy.rights.b3']);
    expect(text()).toContain(en['legal.operator.placeholder']);
    expectAccessibleControls(root);

    TestBed.inject(I18nService).use('ar');
    fixture.detectChanges();

    expect(text()).toContain(ar['privacy.title']);
    expect(text()).toContain(ar['privacy.retention.p1']);
    expect(text()).toContain(ar['privacy.data.2.category']);
    expect(text()).not.toContain(en['privacy.intro']);
  });
});
