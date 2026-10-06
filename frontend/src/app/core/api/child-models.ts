/** Child-only wire contracts. Never reuse parent documents, previews or grading reports here. */
export interface ChildSessionIdentity {
  childId: string;
  name: string;
  expiresAtUtc: string;
  answerLength: number;
}
export type LearnerStatus = 'assigned' | 'awaiting-review' | 'completed';
export interface LearnerAssignmentSummary {
  id: string;
  title: string;
  status: LearnerStatus;
  revision: number;
  createdAtUtc: string;
  hasStarted: boolean;
}
export interface LearnerInbox {
  items: LearnerAssignmentSummary[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}
export interface LearnerQuestion {
  id: string;
  prompt: string;
  interaction: { type: 'numeric-input' | 'text-input' | 'single-choice'; options: string[] | null };
  points: number;
}
export interface LearnerAssignment {
  id: string;
  status: LearnerStatus;
  revision: number;
  createdAtUtc: string;
  document: {
    title: string;
    instructions: string | null;
    materials: { id: string; title: string | null; body: string }[];
    questions: LearnerQuestion[];
  };
}
/** Raw text is never trimmed or parsed into numbers; omitted answers are represented by empty strings in the form. */
export interface LearnerAnswer {
  questionId: string;
  value: string;
}
export interface LearnerSession {
  assignmentId: string;
  revision: number;
  status: LearnerStatus;
  answers: LearnerAnswer[];
  startedAtUtc: string;
  savedAtUtc: string | null;
  submittedAtUtc: string | null;
  reviewedAtUtc: string | null;
  finalTotal: number | null;
  possibleTotal: number | null;
}
