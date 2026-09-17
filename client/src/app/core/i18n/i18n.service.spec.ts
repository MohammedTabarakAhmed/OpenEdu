import { TestBed } from '@angular/core/testing';
import { BOOTSTRAP_LINK_ID, I18nService } from './i18n.service';
import { ar, en, ResourceKey } from './resources';

describe('I18nService (17.4)', () => {
  beforeEach(() => {
    localStorage.clear();
    document.getElementById(BOOTSTRAP_LINK_ID)?.remove();
    TestBed.configureTestingModule({});
  });

  const link = (): HTMLLinkElement | null => document.getElementById(BOOTSTRAP_LINK_ID) as HTMLLinkElement | null;

  // UI-06: every string comes from the resources, and both languages cover the same identifiers.
  it('holds the same set of non-empty identifiers in English and Arabic', () => {
    const enKeys = Object.keys(en).sort();
    const arKeys = Object.keys(ar).sort();

    expect(arKeys).toEqual(enKeys);
    for (const key of enKeys) {
      expect(en[key as ResourceKey].trim(), `en ${key}`).not.toBe('');
      expect(ar[key as ResourceKey].trim(), `ar ${key}`).not.toBe('');
    }
  });

  it('resolves identifiers in the active language and echoes unknown identifiers so tests catch them', () => {
    const i18n = TestBed.inject(I18nService);

    expect(i18n.t('common.save')).toBe('Save');
    i18n.use('ar');
    expect(i18n.t('common.save')).toBe(ar['common.save']);
    expect(i18n.t('no.such.key' as ResourceKey)).toBe('no.such.key');
  });

  // UI-07 and UI-08: language sets the document language, direction and the mirrored Bootstrap build.
  it('sets document language, direction, title and the direction-specific Bootstrap stylesheet', () => {
    const i18n = TestBed.inject(I18nService);
    TestBed.tick();

    expect(document.documentElement.lang).toBe('en');
    expect(document.documentElement.dir).toBe('ltr');
    expect(document.title).toBe('OpenCampus');
    expect(link()?.getAttribute('href')).toBe('bootstrap-ltr.css');

    i18n.use('ar');
    TestBed.tick();

    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.title).toBe(ar['app.title']);
    expect(link()?.getAttribute('href')).toBe('bootstrap-rtl.css');

    i18n.toggle();
    TestBed.tick();

    expect(document.documentElement.dir).toBe('ltr');
    expect(link()?.getAttribute('href')).toBe('bootstrap-ltr.css');
  });

  // UI-10: the selection persists across sessions — a fresh service instance starts from the stored language.
  it('persists the selection and restores it for a new session', () => {
    TestBed.inject(I18nService).use('ar');
    expect(localStorage.getItem('opencampus.language')).toBe('ar');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const restored = TestBed.inject(I18nService);
    TestBed.tick();

    expect(restored.language()).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(link()?.getAttribute('href')).toBe('bootstrap-rtl.css');
  });

  it('falls back to English when the stored value is not a supported language', () => {
    localStorage.setItem('opencampus.language', 'fr');

    expect(TestBed.inject(I18nService).language()).toBe('en');
  });
});
