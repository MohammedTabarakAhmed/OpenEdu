import { registerLocaleData } from '@angular/common';
import localeAr from '@angular/common/locales/ar';
import { TestBed } from '@angular/core/testing';
import { I18nService } from '../core/i18n/i18n.service';
import { formatLocaleNumber, LocaleNumberPipe } from './ui';

// UI-09: numbers formatted according to the active locale, re-evaluated when the language changes.
describe('LocaleNumberPipe (UI-09)', () => {
  beforeAll(() => registerLocaleData(localeAr));
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  it('formats grouping and fraction digits for English and follows the language switch', () => {
    const pipe = TestBed.runInInjectionContext(() => new LocaleNumberPipe());

    expect(pipe.transform(1234.5)).toBe('1,234.5');
    expect(pipe.transform(88)).toBe('88');
    expect(pipe.transform(2 / 3, 'number', 1)).toBe('0.7');

    TestBed.inject(I18nService).use('ar');
    expect(pipe.transform(1234.5)).toBe(formatLocaleNumber('ar', 1234.5));
    expect(pipe.transform(1234.5)).toContain('1');
  });

  it('renders a 0–100 value as a locale percentage', () => {
    expect(formatLocaleNumber('en', 87.5, 'percent', 1)).toBe('87.5%');
    expect(formatLocaleNumber('en', 100, 'percent')).toBe('100%');
    // Arabic wraps the sign in directional marks so it sits correctly beside the digits.
    expect(formatLocaleNumber('ar', 87.5, 'percent', 1)).toContain('%');
    expect(formatLocaleNumber('ar', 87.5, 'percent', 1)).toContain('87.5');
  });

  it('renders nothing for an absent value so templates can substitute a placeholder', () => {
    expect(formatLocaleNumber('en', null)).toBe('');
    expect(formatLocaleNumber('en', undefined)).toBe('');
    expect(formatLocaleNumber('en', Number.NaN)).toBe('');
  });
});
