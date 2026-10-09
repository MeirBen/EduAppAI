/** Shared task choices; difficulty is relative to the audience. */
export interface TaskSettings {
  topic: string;
  audience: string;
  difficulty: 'easy' | 'medium' | 'hard';
  questionCount: number;
}

/** Server-recorded generation diagnostics; excludes prompts, identity and model reasoning. */
export interface GenerationMetadata {
  provider: string;
  model: string;
  promptVersion: string;
  generatedAtUtc: string;
}

/** Supported application-owned question interactions. */
export type QuestionFormat = 'numeric-input' | 'text-input' | 'single-choice';
/** Generated-body words: an advisory target count or a strict range; supplied sources have none. */
export interface LengthExpectation {
  mode: 'target' | 'range';
  count?: number | null;
  lower?: number | null;
  upper?: number | null;
}
/** A generated material or a source copied verbatim from the parent. */
export interface PlanMaterial {
  id: string;
  label: string;
  source: 'generated' | 'supplied';
  guidance: string;
  text?: string | null;
  length?: LengthExpectation | null;
}
/** Canonical learning requirements. Preserve the schema version supplied by the API. */
export interface LearningPlan {
  schemaVersion: number;
  name: string;
  goal: string;
  guidance: string;
  settings: TaskSettings;
  materials: PlanMaterial[];
  totalLength?: LengthExpectation | null;
  questions: {
    formats: QuestionFormat[];
    choiceCount: number | null;
    guidance: string;
  };
}
/** Unresolved conversation only. Accepted requirements live in the current plan. */
export interface AuthoringTurn {
  role: 'parent' | 'assistant';
  text: string;
}
/** One explicit parent message; correlation fields are echoed, never sent to the provider. */
export interface PlanAuthoringRequest {
  message: string;
  baseDefinition?: LearningPlan;
  context: AuthoringTurn[];
  requestId: string;
  baseRevision: number;
}
/** Server-computed changes, including removed/moved objects. Paths are mapped to Hebrew labels. */
export interface PlanChange {
  kind: 'added' | 'removed' | 'moved' | 'changed';
  path: string;
  id?: string | null;
  previousPath?: string | null;
}
/** A complete proposal or one clarification; neither publishes a template. */
export interface PlanAuthoringReply {
  proposal: LearningPlan | null;
  clarification: string | null;
  assumptions: string[];
  changes: PlanChange[];
  requestId: string;
  baseRevision: number;
  generationMetadata: GenerationMetadata;
}
/** Immutable canonical template version. */
export interface PlanTemplateDetail {
  id: string;
  currentVersion: number;
  versionId: string;
  definition: LearningPlan;
}
/** Parent-editable checkpoint. No revisions, acceptance or generation provenance can be submitted. */
export interface EditableActivity {
  title: string;
  instructions: string | null;
  materials: { id: string; title: string | null; body: string }[];
  questions: EditableQuestion[];
}
export interface EditableQuestion {
  id: string;
  prompt: string;
  interaction: { type: QuestionFormat; options: string[] | null };
  answer: { value: string } | null;
  points: number;
}
/** Saved parent-only content. Metadata is read-only and is excluded at the form boundary. */
export interface ActivityDocument extends EditableActivity {
  materials: (EditableActivity['materials'][number] & {
    revision: number;
    origin: ContentOrigin;
    acceptance: ContentAcceptance | null;
  })[];
  questions: (EditableQuestion & {
    origin: ContentOrigin;
    acceptance: ContentAcceptance | null;
  })[];
}
export interface ContentOrigin {
  kind: string;
  generation?: GenerationMetadata | null;
}
export interface ContentAcceptance {
  inputFingerprint: string;
  sources: { id: string; revision: number }[];
  adoptedAtUtc?: string | null;
}
/** A generated-body length requirement resolved from the activity plan. */
export interface ResolvedLength {
  mode: 'target' | 'range';
  value: number | null;
  lower: number | null;
  upper: number | null;
}
/** Server-owned generated-body measurement; target expectations are advisory (satisfied is null). */
export interface LengthMeasurement {
  scope: string;
  expected: ResolvedLength;
  actual: number;
  satisfied: boolean | null;
}
/** Saved authoritative state; diagnostics are server-derived, not local readiness claims. */
export interface ActivityDetail {
  id: string;
  revision: number;
  plan: LearningPlan;
  document: ActivityDocument;
  diagnostics: Record<string, string[]>;
  measurements: LengthMeasurement[];
  activeOperationId: string | null;
  templateVersionId: string | null;
  releasedSnapshotId: string | null;
  releasedSourceRevision: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  chat: ActivityChatTurn[];
  canUndo: boolean;
}
export interface ActivitySummary {
  id: string;
  name: string;
  revision: number;
  updatedAtUtc: string;
}
export interface RevisionTarget {
  kind: 'material' | 'question';
  id: string;
}
export interface ActivityChatTurn {
  role: 'parent' | 'assistant';
  text: string;
  atUtc: string;
  target: RevisionTarget | null;
  operationId: string | null;
  assumptions: string[] | null;
  outcome: string | null;
}
export type ImportedChatTurn = Omit<ActivityChatTurn, 'operationId' | 'outcome'>;
/** New atomic operations coexist with the old workspace actions until the canvas cutover. */
export type GenerationKind =
  | 'Create'
  | 'Revise'
  | 'GenerateMaterials'
  | 'GenerateQuestions'
  | 'ReplaceMaterial'
  | 'ReplaceQuestion';
/** Keep this exact request for explicit same-key recovery after a lost response. */
export interface StartGeneration {
  operationKey: string;
  expectedRevision: number;
  kind: GenerationKind;
  targetId?: string;
  instruction?: string;
  message?: string;
  target?: RevisionTarget;
  sources?: { label: string; text: string }[];
}
/** Polling this parent-only evidence never starts a call. Unknown candidates remain diagnostic text. */
export interface GenerationOperation {
  id: string;
  draftId: string;
  kind: GenerationKind;
  status: 'queued' | 'calling' | 'completed' | 'failed' | 'conflict' | 'cancelled' | 'unknown';
  stage: string;
  originalRevision: number;
  /** Original revision until a successful atomic apply, then its resulting content revision. */
  expectedRevision: number;
  failure: string | null;
  diagnosticsExpired: boolean;
  steps: {
    stage: string;
    outcome: string;
    usage: { costCredits?: number | null } | null;
    metadata: GenerationMetadata | null;
  }[];
  artifacts: {
    targetId: string | null;
    /** Requirements pinned when the operation started; only the parts the parent UI reads. */
    input?: {
      materials: { id: string; label: string; length: ResolvedLength | null }[];
      totalLength: ResolvedLength | null;
    } | null;
    steps: {
      stage: string;
      candidate: unknown;
      diagnostics: Record<string, string[]> | null;
      call?: { output: string | null } | null;
    }[];
  } | null;
}
/** Immutable parent preview, including answer keys. Never reuse as a child contract. */
export interface SnapshotPreview {
  id: string;
  sourceDraftId: string;
  sourceDraftRevision: number;
  plan: LearningPlan;
  document: ActivityDocument;
  reviewedAtUtc: string;
  archivedAtUtc: string | null;
  measurements: LengthMeasurement[];
}
export interface SnapshotSummary {
  id: string;
  title: string;
  status: 'Ready';
  createdAtUtc: string;
  hasAssignments: boolean;
}

/** Template list projection; fetch PlanTemplateDetail when the definition is needed. */
export interface TemplateSummary {
  id: string;
  name: string;
  currentVersion: number;
  createdAtUtc: string;
}

/** Server-enforced limits. The client mirrors them for native validation, caps and copy; the API stays authoritative. */
export interface ContentLimits {
  maxQuestionCount: number;
  minChoiceCount: number;
  maxChoiceCount: number;
  maxMaterials: number;
  maxPoints: number;
  nameLength: number;
  goalLength: number;
  guidanceLength: number;
  scopedGuidanceLength: number;
  settingTextLength: number;
  titleLength: number;
  instructionsLength: number;
  bodyLength: number;
  promptLength: number;
  answerLength: number;
  contentLength: number;
  messageLength: number;
  maxContextTurns: number;
  contextLength: number;
  listLimit: number;
  maxChildAge: number;
  revisionReplyLength: number;
  editInstructionLength: number;
  maxSelectedEdits: number;
  maxAssumptions: number;
  assumptionLength: number;
  maxChatTurns: number;
  authoringReplyLength: number;
}
