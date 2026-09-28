import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResponse, QueryParams, toHttpParams } from '../../core/api/api.models';
import {
  CatalogueEntry,
  CatalogueEntryDetail,
  Course,
  Enrolment,
  GradeComponent,
  InstructorSummary,
  Learner,
  LearnerUserSummary,
  Programme,
  Role,
  ScheduledSession,
  Section,
  SectionDetail,
  Transcript,
  User,
  UserSession,
} from './admin.models';

/** Thin typed clients over the api/v1 routes. Requests are dedicated contract objects, never entities (API-02). */
@Injectable({ providedIn: 'root' })
export class ProgrammesApi {
  private readonly http = inject(HttpClient);

  list(params: QueryParams): Observable<PagedResponse<Programme>> {
    return this.http.get<PagedResponse<Programme>>('/api/v1/programmes', { params: toHttpParams(params) });
  }

  get(id: string): Observable<Programme> {
    return this.http.get<Programme>(`/api/v1/programmes/${id}`);
  }

  create(body: { code: string; nameEn: string; nameAr: string; durationMonths: number }): Observable<Programme> {
    return this.http.post<Programme>('/api/v1/programmes', body);
  }

  update(id: string, body: { nameEn: string; nameAr: string; durationMonths: number; isActive: boolean }): Observable<Programme> {
    return this.http.put<Programme>(`/api/v1/programmes/${id}`, body);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/programmes/${id}`);
  }
}

@Injectable({ providedIn: 'root' })
export class CoursesApi {
  private readonly http = inject(HttpClient);

  list(params: QueryParams): Observable<PagedResponse<Course>> {
    return this.http.get<PagedResponse<Course>>('/api/v1/courses', { params: toHttpParams(params) });
  }

  create(body: { programmeId: string; code: string; nameEn: string; nameAr: string; descriptionEn: string | null; descriptionAr: string | null; credits: number }): Observable<Course> {
    return this.http.post<Course>('/api/v1/courses', body);
  }

  update(id: string, body: { nameEn: string; nameAr: string; descriptionEn: string | null; descriptionAr: string | null; credits: number }): Observable<Course> {
    return this.http.put<Course>(`/api/v1/courses/${id}`, body);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/courses/${id}`);
  }
}

export interface SectionWrite {
  termName: string;
  startDate: string;
  endDate: string;
  capacity: number;
  instructorUserId: string;
  deliveryMode: string;
}

@Injectable({ providedIn: 'root' })
export class SectionsApi {
  private readonly http = inject(HttpClient);

  list(params: QueryParams): Observable<PagedResponse<Section>> {
    return this.http.get<PagedResponse<Section>>('/api/v1/sections', { params: toHttpParams(params) });
  }

  get(id: string): Observable<SectionDetail> {
    return this.http.get<SectionDetail>(`/api/v1/sections/${id}`);
  }

  create(body: SectionWrite & { courseId: string; code: string }): Observable<SectionDetail> {
    return this.http.post<SectionDetail>('/api/v1/sections', body);
  }

  update(id: string, body: SectionWrite): Observable<SectionDetail> {
    return this.http.put<SectionDetail>(`/api/v1/sections/${id}`, body);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/sections/${id}`);
  }

  transition(id: string, action: 'open' | 'close' | 'cancel'): Observable<SectionDetail> {
    return this.http.post<SectionDetail>(`/api/v1/sections/${id}/${action}`, null);
  }

  instructors(): Observable<InstructorSummary[]> {
    return this.http.get<InstructorSummary[]>('/api/v1/sections/instructors');
  }

  addSession(id: string, body: { scheduledStartUtc: string; scheduledEndUtc: string; location: string | null }): Observable<ScheduledSession> {
    return this.http.post<ScheduledSession>(`/api/v1/sections/${id}/sessions`, body);
  }

  removeSession(id: string, sessionId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/sections/${id}/sessions/${sessionId}`);
  }

  addGradeComponent(id: string, body: { nameEn: string; nameAr: string; weightPercent: number; maxScore: number }): Observable<GradeComponent> {
    return this.http.post<GradeComponent>(`/api/v1/sections/${id}/grade-components`, body);
  }

  removeGradeComponent(id: string, componentId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/sections/${id}/grade-components/${componentId}`);
  }

  enrolments(id: string, page: number, pageSize: number): Observable<PagedResponse<Enrolment>> {
    return this.http.get<PagedResponse<Enrolment>>(`/api/v1/sections/${id}/enrolments`, { params: toHttpParams({ page, pageSize }) });
  }
}

@Injectable({ providedIn: 'root' })
export class LearnersApi {
  private readonly http = inject(HttpClient);

  list(params: QueryParams): Observable<PagedResponse<Learner>> {
    return this.http.get<PagedResponse<Learner>>('/api/v1/learners', { params: toHttpParams(params) });
  }

  get(id: string): Observable<Learner> {
    return this.http.get<Learner>(`/api/v1/learners/${id}`);
  }

  create(body: { userId: string; learnerNumber: string; nationalId: string | null; dateOfBirth: string | null; gender: string; phone: string | null }): Observable<Learner> {
    return this.http.post<Learner>('/api/v1/learners', body);
  }

  update(id: string, body: { nationalId: string | null; dateOfBirth: string | null; gender: string; phone: string | null; status: string }): Observable<Learner> {
    return this.http.put<Learner>(`/api/v1/learners/${id}`, body);
  }

  unlinkedUsers(): Observable<LearnerUserSummary[]> {
    return this.http.get<LearnerUserSummary[]>('/api/v1/learners/unlinked-users');
  }

  enrolments(id: string, page: number, pageSize: number): Observable<PagedResponse<Enrolment>> {
    return this.http.get<PagedResponse<Enrolment>>(`/api/v1/learners/${id}/enrolments`, { params: toHttpParams({ page, pageSize }) });
  }

  transcript(id: string): Observable<Transcript> {
    return this.http.get<Transcript>(`/api/v1/learners/${id}/transcript`);
  }
}

@Injectable({ providedIn: 'root' })
export class EnrolmentsApi {
  private readonly http = inject(HttpClient);

  create(body: { learnerId: string; sectionId: string }): Observable<Enrolment> {
    return this.http.post<Enrolment>('/api/v1/enrolments', body);
  }

  withdraw(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/enrolments/${id}`);
  }
}

/** Learner self-service (15.3), scoped server-side to the caller's learner record. */
@Injectable({ providedIn: 'root' })
export class CatalogueApi {
  private readonly http = inject(HttpClient);

  browse(params: QueryParams): Observable<PagedResponse<CatalogueEntry>> {
    return this.http.get<PagedResponse<CatalogueEntry>>('/api/v1/catalogue', { params: toHttpParams(params) });
  }

  get(sectionId: string): Observable<CatalogueEntryDetail> {
    return this.http.get<CatalogueEntryDetail>(`/api/v1/catalogue/${sectionId}`);
  }

  myEnrolments(page: number, pageSize: number, activeOnly: boolean): Observable<PagedResponse<Enrolment>> {
    return this.http.get<PagedResponse<Enrolment>>('/api/v1/me/enrolments', { params: toHttpParams({ page, pageSize, activeOnly }) });
  }

  enrol(sectionId: string): Observable<Enrolment> {
    return this.http.post<Enrolment>('/api/v1/me/enrolments', { sectionId });
  }

  withdraw(enrolmentId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/me/enrolments/${enrolmentId}`);
  }

  myTranscript(): Observable<Transcript> {
    return this.http.get<Transcript>('/api/v1/me/enrolments/transcript');
  }
}

/** User administration (Increment 2 interface; screens delivered with the first administrative shell). */
@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  list(params: QueryParams): Observable<PagedResponse<User>> {
    return this.http.get<PagedResponse<User>>('/api/v1/users', { params: toHttpParams(params) });
  }

  get(id: string): Observable<User> {
    return this.http.get<User>(`/api/v1/users/${id}`);
  }

  create(body: { userName: string; email: string; password: string; fullNameEn: string; fullNameAr: string; roles: string[] }): Observable<User> {
    return this.http.post<User>('/api/v1/users', body);
  }

  update(id: string, body: { email: string; fullNameEn: string; fullNameAr: string }): Observable<User> {
    return this.http.put<User>(`/api/v1/users/${id}`, body);
  }

  assignRoles(id: string, roles: string[]): Observable<User> {
    return this.http.put<User>(`/api/v1/users/${id}/roles`, { roles });
  }

  deactivate(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/users/${id}`);
  }

  activate(id: string): Observable<void> {
    return this.http.post<void>(`/api/v1/users/${id}/activate`, null);
  }

  // Self-registration (Increment 7): approval grants the requested role; rejection removes the pending row.
  approveRegistration(id: string): Observable<User> {
    return this.http.post<User>(`/api/v1/users/${id}/registration/approve`, null);
  }

  rejectRegistration(id: string): Observable<void> {
    return this.http.post<void>(`/api/v1/users/${id}/registration/reject`, null);
  }

  roles(): Observable<Role[]> {
    return this.http.get<Role[]>('/api/v1/roles');
  }

  sessions(id: string): Observable<UserSession[]> {
    return this.http.get<UserSession[]>(`/api/v1/users/${id}/sessions`);
  }

  revokeSession(id: string, sessionId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/users/${id}/sessions/${sessionId}`);
  }

  revokeAllSessions(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/users/${id}/sessions`);
  }
}
