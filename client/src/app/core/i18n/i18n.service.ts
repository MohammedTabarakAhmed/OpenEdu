import { DOCUMENT } from '@angular/common';
import { effect, inject, Injectable, Pipe, PipeTransform, signal } from '@angular/core';
import { ar, en, ResourceKey } from './resources';

export type Language = 'en' | 'ar';

const STORAGE_KEY = 'opencampus.language';

/**
 * SDD 17.4 groundwork. UI-06: strings by identifier; UI-07: language sets document language
 * and direction; UI-10: the selection persists across sessions. Full coverage is Increment 6.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly document = inject(DOCUMENT);
  private readonly current = signal<Language>(this.restore());

  readonly language = this.current.asReadonly();

  constructor() {
    effect(() => {
      const language = this.current();
      this.document.documentElement.lang = language;
      this.document.documentElement.dir = language === 'ar' ? 'rtl' : 'ltr';
    });
  }

  t(key: ResourceKey): string {
    return (this.current() === 'ar' ? ar : en)[key] ?? key;
  }

  use(language: Language): void {
    this.current.set(language);
    try {
      this.document.defaultView?.localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Storage may be unavailable (private mode); the selection still applies for this session.
    }
  }

  toggle(): void {
    this.use(this.current() === 'en' ? 'ar' : 'en');
  }

  private restore(): Language {
    try {
      const stored = this.document.defaultView?.localStorage.getItem(STORAGE_KEY);
      return stored === 'ar' ? 'ar' : 'en';
    } catch {
      return 'en';
    }
  }
}

@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  /** Accepts a composed key (e.g. a status prefix plus a server value); unknown keys render as the key itself, which tests catch. */
  transform(key: ResourceKey | string): string {
    return this.i18n.t(key as ResourceKey);
  }
}
