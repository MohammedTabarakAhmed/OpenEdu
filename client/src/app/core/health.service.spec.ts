import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HealthService } from './health.service';

describe('HealthService', () => {
  let service: HealthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HealthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests the health endpoint as text', () => {
    let result = '';
    service.status().subscribe((s) => (result = s));

    const req = http.expectOne('/api/health');
    expect(req.request.method).toBe('GET');
    req.flush('Healthy');

    expect(result).toBe('Healthy');
  });
});
