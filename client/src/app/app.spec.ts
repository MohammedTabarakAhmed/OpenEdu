import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { SessionService } from './core/auth/session.service';
import { BOOTSTRAP_LINK_ID } from './core/i18n/i18n.service';
import { ar } from './core/i18n/resources';
import { authenticated } from './core/auth/session.service.spec';
import { expectAccessibleControls } from './shared/accessibility.spec-support';

describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  function element(fixture: { nativeElement: HTMLElement }, testId: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  it('shows the API as healthy when the health endpoint responds', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/health').flush('Healthy');
    await fixture.whenStable();

    expect(element(fixture, 'api-status')?.textContent).toContain('Healthy');
    expectAccessibleControls(fixture.nativeElement); // NFR-09
  });

  it('shows the API as unreachable when the health request fails', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/health').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(element(fixture, 'api-status')?.textContent).toContain('Unreachable');
  });

  // 17.3: navigation only presents functions available to the user.
  it('hides role navigation while anonymous and shows only the user role shells once signed in', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush('Healthy');
    await fixture.whenStable();

    expect(element(fixture, 'nav-admin')).toBeNull();
    expect(element(fixture, 'nav-learner')).toBeNull();
    expect(element(fixture, 'logout')).toBeNull();

    const session = TestBed.inject(SessionService);
    session.login('ada', 'pw').subscribe();
    http.expectOne('/api/v1/auth/login').flush({ mfaRequired: false, challenge: null, authenticated: authenticated('tok', ['Learner']) });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(element(fixture, 'nav-learner')).not.toBeNull();
    expect(element(fixture, 'nav-admin')).toBeNull();
    expect(element(fixture, 'nav-instructor')).toBeNull();
    expect(element(fixture, 'logout')).not.toBeNull();
    expect(element(fixture, 'current-user')?.textContent).toContain('Ada');
  });

  // UI-07: language selection sets document language and direction.
  it('switches document language and direction when the language is toggled', async () => {
    const fixture = TestBed.createComponent(App);
    TestBed.inject(HttpTestingController).expectOne('/api/health').flush('Healthy');
    await fixture.whenStable();

    expect(document.documentElement.dir).toBe('ltr');
    (element(fixture, 'language-toggle') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(localStorage.getItem('opencampus.language')).toBe('ar');
  });

  // Increment 6 exit criterion: the journey is available in Arabic — shell strings, the user's Arabic name,
  // the mirrored stylesheet and the document title all follow the selection, and it survives a new session.
  it('presents the signed-in shell in Arabic with the mirrored stylesheet and keeps the choice for the next session', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush('Healthy');
    await fixture.whenStable();
    TestBed.inject(SessionService).login('ada', 'pw').subscribe();
    http.expectOne('/api/v1/auth/login').flush({ mfaRequired: false, challenge: null, authenticated: authenticated('tok', ['Learner']) });
    fixture.detectChanges();
    await fixture.whenStable();

    (element(fixture, 'language-toggle') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(element(fixture, 'nav-learner')?.textContent?.trim()).toBe(ar['nav.learner']);
    expect(element(fixture, 'logout')?.textContent?.trim()).toBe(ar['nav.logout']);
    expect(element(fixture, 'current-user')?.textContent).toContain('آدا');
    expect(element(fixture, 'language-toggle')?.textContent?.trim()).toBe('English');
    expect(document.title).toBe(ar['app.title']);
    expect(document.getElementById(BOOTSTRAP_LINK_ID)?.getAttribute('href')).toBe('bootstrap-rtl.css');

    // A new session (fresh injector, same browser storage) starts in Arabic without any interaction.
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const next = TestBed.createComponent(App);
    TestBed.inject(HttpTestingController).expectOne('/api/health').flush('Healthy');
    await next.whenStable();

    expect(document.documentElement.dir).toBe('rtl');
    expect(element(next, 'nav-admin')).toBeNull();
    expect(element(next, 'language-toggle')?.textContent?.trim()).toBe('English');
  });
});
