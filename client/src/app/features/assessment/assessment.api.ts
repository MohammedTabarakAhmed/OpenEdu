import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AssignmentItem,
  AssignmentSubmissionsResponse,
  AttendanceStatus,
  GradebookResponse,
  LearnerResultsResponse,
  SectionAssignmentsResponse,
  SectionAttendanceResponse,
  SessionRegisterResponse,
  SubmissionItem,
} from './assessment.models';

/** Typed client over the Increment 5 assessment and grading routes (15.3). */
@Injectable({ providedIn: 'root' })
export class AssessmentApi {
  private readonly http = inject(HttpClient);

  // ----- Assignments -----

  listAssignments(sectionId: string): Observable<SectionAssignmentsResponse> {
    return this.http.get<SectionAssignmentsResponse>(`/api/v1/sections/${sectionId}/assignments`);
  }

  createAssignment(
    sectionId: string,
    body: { titleEn: string; titleAr: string; instructions: string | null; maxScore: number; dueAtUtc: string; allowLate: boolean; latePenaltyPercent: number },
  ): Observable<AssignmentItem> {
    return this.http.post<AssignmentItem>(`/api/v1/sections/${sectionId}/assignments`, body);
  }

  publishAssignment(id: string): Observable<AssignmentItem> {
    return this.http.post<AssignmentItem>(`/api/v1/assignments/${id}/publish`, null);
  }

  unpublishAssignment(id: string): Observable<AssignmentItem> {
    return this.http.post<AssignmentItem>(`/api/v1/assignments/${id}/unpublish`, null);
  }

  deleteAssignment(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/assignments/${id}`);
  }

  listSubmissions(assignmentId: string): Observable<AssignmentSubmissionsResponse> {
    return this.http.get<AssignmentSubmissionsResponse>(`/api/v1/assignments/${assignmentId}/submissions`);
  }

  /** A learner's own work: text, an optional file, or both. */
  submit(assignmentId: string, textBody: string | null, file: File | null): Observable<SubmissionItem> {
    const form = new FormData();
    if (textBody) {
      form.append('textBody', textBody);
    }
    if (file) {
      form.append('file', file, file.name);
    }
    return this.http.post<SubmissionItem>(`/api/v1/assignments/${assignmentId}/submit`, form);
  }

  mark(submissionId: string, score: number, feedback: string | null): Observable<SubmissionItem> {
    return this.http.post<SubmissionItem>(`/api/v1/submissions/${submissionId}/mark`, { score, feedback });
  }

  downloadSubmissionFile(submissionId: string): Observable<Blob> {
    return this.http.get(`/api/v1/submissions/${submissionId}/file`, { responseType: 'blob' });
  }

  // ----- Attendance -----

  sectionAttendance(sectionId: string): Observable<SectionAttendanceResponse> {
    return this.http.get<SectionAttendanceResponse>(`/api/v1/sections/${sectionId}/attendance`);
  }

  sessionRegister(sectionId: string, sessionId: string): Observable<SessionRegisterResponse> {
    return this.http.get<SessionRegisterResponse>(`/api/v1/sections/${sectionId}/sessions/${sessionId}/attendance`);
  }

  recordAttendance(sectionId: string, sessionId: string, entries: { learnerUserId: string; status: AttendanceStatus }[]): Observable<SessionRegisterResponse> {
    return this.http.post<SessionRegisterResponse>(`/api/v1/sections/${sectionId}/sessions/${sessionId}/attendance`, { entries });
  }

  // ----- Grading -----

  gradebook(sectionId: string): Observable<GradebookResponse> {
    return this.http.get<GradebookResponse>(`/api/v1/sections/${sectionId}/grades`);
  }

  recordGrade(sectionId: string, enrolmentId: string, gradeComponentId: string, score: number): Observable<unknown> {
    return this.http.post(`/api/v1/sections/${sectionId}/grades`, { enrolmentId, gradeComponentId, score });
  }

  release(sectionId: string): Observable<GradebookResponse> {
    return this.http.post<GradebookResponse>(`/api/v1/sections/${sectionId}/grades/release`, null);
  }

  myResults(enrolmentId: string): Observable<LearnerResultsResponse> {
    return this.http.get<LearnerResultsResponse>(`/api/v1/me/enrolments/${enrolmentId}/results`);
  }
}
