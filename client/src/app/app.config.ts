import { registerLocaleData } from '@angular/common';
import localeAr from '@angular/common/locales/ar';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { SessionService } from './core/auth/session.service';

// UI-09: Arabic locale data for date and number formatting in the active language.
registerLocaleData(localeAr);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    // Route parameters bind to component inputs (e.g. the section identifier of a detail page).
    provideRouter(routes, withComponentInputBinding()),
    // UI-02: session continuity across reload by exchanging the refresh cookie at start-up.
    provideAppInitializer(() => firstValueFrom(inject(SessionService).initialise())),
  ],
};
