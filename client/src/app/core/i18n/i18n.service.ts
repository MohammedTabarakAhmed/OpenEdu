import { DOCUMENT } from '@angular/common';
import { effect, inject, Injectable, Pipe, PipeTransform, signal } from '@angular/core';
import { ar, en, ResourceKey } from './resources';

export type Language = 'en' | 'ar';

const STORAGE_KEY = 'opencampus.language';

/** The stylesheet link in index.html that carries the direction-specific Bootstrap build (UI-08). */
export const BOOTSTRAP_LINK_ID = 'bootstrap-direction';

/**
 * SDD 17.4. UI-06: strings by identifier; UI-07: language sets document language and direction (and
 * swaps Bootstrap's LTR/RTL build, since the framework's own internals are physical); UI-10: the selection
 * persists across sessions. The document title follows the language too, so no user-facing string is fixed.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly document = inject(DOCUMENT);
  private readonly current = signal<Language>(this.restore());

  readonly language = this.current.asReadonly();

  constructor() {
    effect(() => {
      const language = this.current();
      const direction = language === 'ar' ? 'rtl' : 'ltr';
      this.document.documentElement.lang = language;
      this.document.documentElement.dir = direction;
      this.document.title = this.t('app.title');
      this.document.querySelector('meta[name="description"]')?.setAttribute('content', this.t('app.description'));
      // Only when the build actually changes: re-setting an identical href makes the browser re-fetch and re-apply
      // the stylesheet, which on a cold load shows as a layout shift.
      const link = this.bootstrapLink();
      const href = `bootstrap-${direction}.css`;
      if (link.getAttribute('href') !== href) {
        link.setAttribute('href', href);
      }
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

  /** Finds the direction-specific Bootstrap link, creating it when the host page has none (tests). */
  private bootstrapLink(): HTMLLinkElement {
    const existing = this.document.getElementById(BOOTSTRAP_LINK_ID);
    if (existing instanceof HTMLLinkElement) {
      return existing;
    }
    const link = this.document.createElement('link');
    link.id = BOOTSTRAP_LINK_ID;
    link.rel = 'stylesheet';
    this.document.head.prepend(link);
    return link;
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
