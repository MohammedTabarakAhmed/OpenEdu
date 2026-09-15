import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('shows the API as healthy when the health endpoint responds', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/health').flush('Healthy');
    await fixture.whenStable();

    const status = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="api-status"]');
    expect(status?.textContent).toContain('Healthy');
  });

  it('shows the API as unreachable when the health request fails', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/health').error(new ProgressEvent('error'));
    await fixture.whenStable();

    const status = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="api-status"]');
    expect(status?.textContent).toContain('Unreachable');
  });
});
