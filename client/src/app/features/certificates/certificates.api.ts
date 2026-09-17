import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

/** Mirror of `CertificateResponse` (Increment 6). */
export interface Certificate {
  id: string;
  enrolmentId: string;
  learnerId: string;
  learnerNumber: string;
  learnerFullNameEn: string;
  learnerFullNameAr: string;
  sectionId: string;
  sectionCode: string;
  term: string;
  courseCode: string;
  courseNameEn: string;
  courseNameAr: string;
  programmeCode: string;
  programmeNameEn: string;
  programmeNameAr: string;
  finalGrade: number;
  completedAtUtc: string;
  verificationCode: string;
  issuedAtUtc: string;
}

/** Mirror of `CertificateVerificationResponse`: what an anonymous verifier is told, and nothing internal. */
export interface CertificateVerification {
  verificationCode: string;
  issuedAtUtc: string;
  learnerFullNameEn: string;
  learnerFullNameAr: string;
  courseCode: string;
  courseNameEn: string;
  courseNameAr: string;
  programmeNameEn: string;
  programmeNameAr: string;
  term: string;
  completedAtUtc: string;
}

/** Typed client over the Increment 6 certificate routes (15.3 "Certification"; 15.4 anonymous verification). */
@Injectable({ providedIn: 'root' })
export class CertificatesApi {
  private readonly http = inject(HttpClient);

  /** The calling learner's certificates. */
  mine(): Observable<Certificate[]> {
    return this.http.get<Certificate[]>('/api/v1/me/certificates');
  }

  /** Administrative listing for one learner (sis.learner.read). */
  forLearner(learnerId: string): Observable<Certificate[]> {
    return this.http.get<Certificate[]>(`/api/v1/learners/${learnerId}/certificates`);
  }

  /** Issuance (sis.certificate.issue); BR-10/BR-11 arrive as 422 with the rule code. */
  issue(enrolmentId: string): Observable<Certificate> {
    return this.http.post<Certificate>(`/api/v1/enrolments/${enrolmentId}/certificate`, null);
  }

  /** The PDF, fetched with the bearer token (SEC-25); the browser never sees a file path (SEC-24). */
  download(certificateId: string): Observable<Blob> {
    return this.http.get(`/api/v1/certificates/${certificateId}/file`, { responseType: 'blob' });
  }

  /** Anonymous verification by code; 404 for an unknown code. */
  verify(code: string): Observable<CertificateVerification> {
    return this.http.get<CertificateVerification>(`/api/v1/certificates/verify/${encodeURIComponent(code.trim())}`);
  }
}
