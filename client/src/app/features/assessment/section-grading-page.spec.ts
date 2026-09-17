import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ConfirmService } from '../../shared/ui';
import { GradebookResponse } from './assessment.models';
import { SectionGradingPage } from './section-grading-page';
import { expectAccessibleControls } from '../../shared/accessibility.spec-support';

function gradebook(overrides: Partial<GradebookResponse> = {}): GradebookResponse {
  return {
    sectionId: 's1', sectionCode: 'CS101-A', courseCode: 'CS101', courseNameEn: 'Programming', courseNameAr: 'برمجة',
    components: [{ id: 'c1', nameEn: 'Midterm', nameAr: 'نصفي', weightPercent: 50, maxScore: 100 }],
    rows: [{
      enrolmentId: 'e1', learnerId: 'l1', learnerNumber: 'L001', userId: 'learner01', fullNameEn: 'Learner One', fullNameAr: 'متعلم واحد',
      status: 'Active', finalGrade: null, entries: [],
    }],
    ungradedPairCount: 1, isReleased: false,
    ...overrides,
  };
}

describe('SectionGradingPage (15.3 grading, BR-05, BR-07, BR-06)', () => {
  let fixture: ComponentFixture<SectionGradingPage>;
  let http: HttpTestingController;

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [SectionGradingPage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: ConfirmService, useValue: { confirm: () => true } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SectionGradingPage);
    fixture.componentRef.setInput('id', 's1');
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  async function flushInitial(body: GradebookResponse): Promise<void> {
    await fixture.whenStable();
    http.expectOne('/api/v1/sections/s1/grades').flush(body);
    fixture.detectChanges();
  }

  it('shows the gradebook table and blocks release while pairs remain ungraded', async () => {
    await flushInitial(gradebook());

    expect(element('gradebook-table')).not.toBeNull();
    expect(element('ungraded-notice')?.textContent).toContain('1');
    expect((element('release') as HTMLButtonElement).disabled).toBe(true);
    expectAccessibleControls(fixture.nativeElement); // NFR-09
  });

  it('records a score through the inline editor (BR-05)', async () => {
    await flushInitial(gradebook());

    (element('edit-e1-c1') as HTMLButtonElement).click();
    fixture.detectChanges();
    (element('score-submit') as HTMLButtonElement).click();

    const request = http.expectOne('/api/v1/sections/s1/grades');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ enrolmentId: 'e1', gradeComponentId: 'c1', score: 0 });
    request.flush({});
    http.expectOne('/api/v1/sections/s1/grades').flush(gradebook({ ungradedPairCount: 0 }));
    fixture.detectChanges();

    expect(element('notice')).not.toBeNull();
  });

  it('releases the section once every pair is graded (BR-07), unlocking learner visibility', async () => {
    await flushInitial(gradebook({ ungradedPairCount: 0 }));

    expect((element('release') as HTMLButtonElement).disabled).toBe(false);
    (element('release') as HTMLButtonElement).click();

    const request = http.expectOne('/api/v1/sections/s1/grades/release');
    expect(request.request.method).toBe('POST');
    request.flush(gradebook({ ungradedPairCount: 0, isReleased: true }));
    http.expectOne('/api/v1/sections/s1/grades').flush(gradebook({ ungradedPairCount: 0, isReleased: true }));
    fixture.detectChanges();

    expect(element('release-status')?.textContent).toContain('Released');
  });

  it('tells the caller when the section is not available to them (404, API-06)', async () => {
    await fixture.whenStable();
    http.expectOne('/api/v1/sections/s1/grades').flush({ status: 404, title: 'sections.not_found' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(element('state-not-found')).not.toBeNull();
  });
});
