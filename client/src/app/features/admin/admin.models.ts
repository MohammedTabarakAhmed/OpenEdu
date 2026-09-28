/** Contracts of the SIS and user-administration interfaces (mirror the server's dedicated response types, API-02). */

export type DeliveryMode = 'InPerson' | 'Online' | 'Blended';
export type SectionStatus = 'Draft' | 'Open' | 'Closed' | 'Cancelled';
export type EnrolmentStatus = 'Active' | 'AtRisk' | 'Withdrawn' | 'Completed';
export type LearnerStatus = 'Active' | 'Suspended' | 'Graduated' | 'Withdrawn';
export type Gender = 'Unspecified' | 'Male' | 'Female';
export type RegistrationStatus = 'None' | 'AwaitingVerification' | 'AwaitingApproval' | 'Approved';

export const DELIVERY_MODES: DeliveryMode[] = ['InPerson', 'Online', 'Blended'];
export const SECTION_STATUSES: SectionStatus[] = ['Draft', 'Open', 'Closed', 'Cancelled'];
export const LEARNER_STATUSES: LearnerStatus[] = ['Active', 'Suspended', 'Graduated', 'Withdrawn'];
export const GENDERS: Gender[] = ['Unspecified', 'Male', 'Female'];

export interface Programme {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  durationMonths: number;
  isActive: boolean;
  createdAtUtc: string;
  modifiedAtUtc: string | null;
}

export interface Course {
  id: string;
  programmeId: string;
  programmeCode: string;
  programmeNameEn: string;
  programmeNameAr: string;
  code: string;
  nameEn: string;
  nameAr: string;
  descriptionEn: string | null;
  descriptionAr: string | null;
  credits: number;
  createdAtUtc: string;
  modifiedAtUtc: string | null;
}

export interface InstructorSummary {
  userId: string;
  userName: string;
  fullNameEn: string;
  fullNameAr: string;
  isActive: boolean;
}

export interface Section {
  id: string;
  courseId: string;
  courseCode: string;
  courseNameEn: string;
  courseNameAr: string;
  programmeId: string;
  code: string;
  termName: string;
  startDate: string;
  endDate: string;
  capacity: number;
  activeEnrolmentCount: number;
  instructor: InstructorSummary | null;
  deliveryMode: DeliveryMode;
  status: SectionStatus;
  createdAtUtc: string;
  modifiedAtUtc: string | null;
}

export interface ScheduledSession {
  id: string;
  sectionId: string;
  scheduledStartUtc: string;
  scheduledEndUtc: string;
  location: string | null;
}

export interface GradeComponent {
  id: string;
  sectionId: string;
  nameEn: string;
  nameAr: string;
  weightPercent: number;
  maxScore: number;
}

export interface SectionDetail {
  section: Section;
  sessions: ScheduledSession[];
  gradeComponents: GradeComponent[];
  totalWeightPercent: number;
}

export interface LearnerUserSummary {
  userId: string;
  userName: string;
  email: string;
  fullNameEn: string;
  fullNameAr: string;
  isActive: boolean;
}

export interface Learner {
  id: string;
  user: LearnerUserSummary | null;
  learnerNumber: string;
  nationalId: string | null;
  dateOfBirth: string | null;
  gender: Gender;
  phone: string | null;
  status: LearnerStatus;
  createdAtUtc: string;
  modifiedAtUtc: string | null;
}

export interface Enrolment {
  id: string;
  learner: { learnerId: string; learnerNumber: string; userId: string; userName: string | null; fullNameEn: string | null; fullNameAr: string | null };
  section: {
    sectionId: string;
    sectionCode: string;
    termName: string;
    startDate: string;
    endDate: string;
    courseId: string;
    courseCode: string;
    courseNameEn: string;
    courseNameAr: string;
    credits: number;
    instructorNameEn: string | null;
    instructorNameAr: string | null;
  };
  enrolledAtUtc: string;
  status: EnrolmentStatus;
  finalGrade: number | null;
  completedAtUtc: string | null;
}

export interface Transcript {
  learnerId: string;
  learnerNumber: string;
  fullNameEn: string | null;
  fullNameAr: string | null;
  enrolments: Enrolment[];
  completedCount: number;
  creditsEarned: number;
}

export interface CatalogueEntry {
  sectionId: string;
  sectionCode: string;
  termName: string;
  startDate: string;
  endDate: string;
  deliveryMode: DeliveryMode;
  capacity: number;
  placesRemaining: number;
  courseId: string;
  courseCode: string;
  courseNameEn: string;
  courseNameAr: string;
  descriptionEn: string | null;
  descriptionAr: string | null;
  credits: number;
  programmeId: string;
  programmeNameEn: string;
  programmeNameAr: string;
  instructorNameEn: string | null;
  instructorNameAr: string | null;
  isEnrolled: boolean;
}

export interface CatalogueEntryDetail {
  entry: CatalogueEntry;
  sessions: ScheduledSession[];
}

export interface User {
  id: string;
  userName: string;
  email: string;
  fullNameEn: string;
  fullNameAr: string;
  isActive: boolean;
  mfaEnabled: boolean;
  lockedUntilUtc: string | null;
  roles: string[];
  createdAtUtc: string;
  modifiedAtUtc: string | null;
  registrationStatus: RegistrationStatus;
  requestedRole: string | null;
  emailVerifiedAtUtc: string | null;
}

export interface Role {
  id: string;
  name: string;
  description: string;
  permissions: string[];
}

export interface UserSession {
  id: string;
  familyId: string;
  issuedAtUtc: string;
  expiresAtUtc: string;
  ipAddress: string | null;
  userAgent: string | null;
}
