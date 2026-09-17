import { registerLocaleData } from '@angular/common';
import localeAr from '@angular/common/locales/ar';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { I18nService } from '../../core/i18n/i18n.service';
import { ar, en } from '../../core/i18n/resources';
import { PrivacyNoticePage } from './privacy-notice-page';

describe('PrivacyNoticePage', () => {
  beforeAll(() => registerLocaleData(localeAr));
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({ imports: [PrivacyNoticePage], providers: [provideRouter([])] }).compileComponents();
  });

  it('renders every section from the resources in the active language and follows a language switch', () => {
    const fixture = TestBed.createComponent(PrivacyNoticePage);
    fixture.detectChanges();
    const text = (): string => fixture.nativeElement.textContent;

    expect(text()).toContain(en['privacy.title']);
    expect(text()).toContain(en['privacy.retention.body']);
    expect(text()).toContain(en['privacy.notices.body']);
    expect(fixture.nativeElement.querySelectorAll('h2').length).toBe(7);

    TestBed.inject(I18nService).use('ar');
    fixture.detectChanges();

    expect(text()).toContain(ar['privacy.title']);
    expect(text()).toContain(ar['privacy.retention.body']);
    expect(text()).not.toContain(en['privacy.title']);
  });
});
