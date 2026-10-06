import { ActivityDocument, SnapshotPreview } from './models';

/** Bounded server page. A following page exists only when hasMore is true. */
export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}
export interface ChildSummary {
  id: string;
  name: string;
  enabled: boolean;
  revision: number;
  createdAtUtc: string;
  grade: string | null;
  age: number | null;
  ageConfirmedAtUtc: string | null;
  updatedAtUtc: string | null;
  hasAssignments: boolean;
}
export interface ChildProfileDetails {
  grade: string | null;
  age: number | null;
}
export interface ChildDevice {
  id: string;
  deviceLabel: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  revokedAtUtc: string | null;
  canRemove: boolean;
}
/** Transient one-time secret. Never put this in storage, a URL or logging. */
export interface ChildActivation {
  code: string;
  expiresAtUtc: string;
}
export type AssignmentStatus = 'assigned' | 'withdrawn' | 'awaiting-review' | 'completed';
export interface AssignmentSummary {
  id: string;
  childId: string;
  childName: string;
  snapshotId: string;
  title: string;
  status: AssignmentStatus;
  revision: number;
  createdAtUtc: string;
  hasStarted: boolean;
}
/** Parent-only frozen content and answer keys; never consume from child routes. */
export interface AssignmentDetail {
  assignment: AssignmentSummary;
  snapshot: SnapshotPreview;
  startedAtUtc: string | null;
  savedAtUtc: string | null;
  submittedAtUtc: string | null;
}
export interface ParentGrade {
  questionId: string;
  points: number;
}
/** Parent-only report. Revision belongs to the session, not the assignment. */
export interface ParentAssignmentResult {
  assignment: AssignmentSummary;
  document: ActivityDocument;
  revision: number;
  answers: { questionId: string; value: string }[];
  evaluation: {
    questions: {
      questionId: string;
      possiblePoints: number;
      awardedPoints: number | null;
      gradingMethod: 'automatic' | 'parent';
    }[];
    automaticSubtotal: number;
    possibleTotal: number;
    pendingCount: number;
    finalTotal: number | null;
  };
  scoringPolicyVersion: number;
  startedAtUtc: string;
  savedAtUtc: string | null;
  submittedAtUtc: string;
  reviewedAtUtc: string | null;
  reviewedByParentId: string | null;
}
