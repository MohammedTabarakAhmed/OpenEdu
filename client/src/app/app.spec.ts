import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { SessionService } from './core/auth/session.service';
import { authenticated } from './core/auth/session.service.spec';

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
});
