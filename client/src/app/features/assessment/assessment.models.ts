import { SectionSummary } from '../content/content.models';

/** TypeScript mirrors of the Increment 5 assessment contracts (API-02). */
export type AttendanceStatus = 'Present' | 'Absent' | 'Late' | 'Excused';

export const ATTENDANCE_STATUSES: readonly AttendanceStatus[] = ['Present', 'Absent', 'Late', 'Excused'];

export interface EnrolledLearner {
  userId: string;
  learnerNumber: string;
  fullNameEn: string;
  fullNameAr: string;
}

export interface SessionSummary {
  id: string;
  sectionId: string;
  scheduledStartUtc: string;
  scheduledEndUtc: string;
  location: string | null;
}

export interface SubmissionItem {
  id: string;
  assignmentId: string;
  learnerUserId: string;
  learnerNumber: string | null;
  learnerNameEn: string | null;
  learnerNameAr: string | null;
  submittedAtUtc: string;
  isLate: boolean;
  textBody: string | null;
  hasFile: boolean;
  score: number | null;
  feedback: string | null;
  gradedAtUtc: string | null;
  originalityScore: number | null;
}

export interface AssignmentItem {
  id: string;
  sectionId: string;
  titleEn: string;
  titleAr: string;
  instructions: string | null;
  maxScore: number;
  dueAtUtc: string;
  allowLate: boolean;
  latePenaltyPercent: number;
  isPublished: boolean;
  submissionCount: number;
  markedCount: number;
  mySubmission: SubmissionItem | null;
}

export interface SectionAssignmentsResponse {
  section: SectionSummary;
  canManage: boolean;
  assignments: AssignmentItem[];
}

export interface AssignmentSubmissionsResponse {
  assignment: AssignmentItem;
  enrolledLearners: EnrolledLearner[];
  submissions: SubmissionItem[];
}

export interface AttendanceRecordItem {
  sessionId: string;
  learnerUserId: string;
  status: AttendanceStatus;
  recordedByUserId: string;
  recordedAtUtc: string;
}

export interface SessionRegisterResponse {
  session: SessionSummary;
  learners: EnrolledLearner[];
  records: AttendanceRecordItem[];
}

export interface LearnerAttendanceSummary {
  learnerUserId: string;
  sessionsHeld: number;
  sessionsAttended: number;
  attendancePercent: number;
}

export interface SectionAttendanceResponse {
  section: SectionSummary;
  canManage: boolean;
  sessions: SessionSummary[];
  learners: LearnerAttendanceSummary[];
  records: AttendanceRecordItem[];
}

export interface GradebookComponent {
  id: string;
  nameEn: string;
  nameAr: string;
  weightPercent: number;
  maxScore: number;
}

export interface GradeEntry {
  id: string;
  enrolmentId: string;
  gradeComponentId: string;
  score: number;
  isReleased: boolean;
  gradedByUserId: string;
  gradedAtUtc: string;
}

export interface GradebookRow {
  enrolmentId: string;
  learnerId: string;
  learnerNumber: string;
  userId: string;
  fullNameEn: string;
  fullNameAr: string;
  status: string;
  finalGrade: number | null;
  entries: GradeEntry[];
}

export interface GradebookResponse {
  sectionId: string;
  sectionCode: string;
  courseCode: string;
  courseNameEn: string;
  courseNameAr: string;
  components: GradebookComponent[];
  rows: GradebookRow[];
  ungradedPairCount: number;
  isReleased: boolean;
}

export interface LearnerResultsResponse {
  enrolmentId: string;
  sectionId: string;
  sectionCode: string;
  courseCode: string;
  courseNameEn: string;
  courseNameAr: string;
  status: string;
  finalGrade: number | null;
  isReleased: boolean;
  components: GradebookComponent[];
  entries: GradeEntry[];
}
