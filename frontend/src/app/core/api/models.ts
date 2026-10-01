/** JSON scalar parameter value; the server validates type and bounds. */
type ParameterValue = string | number | boolean;
/** Values keyed by case-sensitive schema keys. Omitted keys allow server defaults. */
export type ParameterValues = Record<string, ParameterValue>;

/** Shared task choices; difficulty is relative to the audience. */
export interface TaskSettings {
  topic: string;
  audience: string;
  difficulty: 'easy' | 'medium' | 'hard';
  questionCount: number;
}

/** Task choices; the server resolves omitted parameter defaults before generation and storage. */
export interface TaskInput {
  settings: TaskSettings;
  parameters: ParameterValues;
}

/** Field metadata shared with the backend's ParameterDefinition contract. */
export interface ParameterDefinition {
  key: string;
  label: string;
  type: 'text' | 'integer' | 'select' | 'boolean';
  required?: boolean;
  /** Used when a submitted key is omitted; a missing or null default means no default. */
  default?: ParameterValue | null;
  min?: number | null;
  max?: number | null;
  maxLength?: number | null;
  options?: string[] | null;
}

/** Published blueprint; schemaVersion describes the JSON format, not the template revision. */
export interface TemplateDefinition {
  schemaVersion: 4;
  name: string;
  instanceParameters: ParameterDefinition[];
  generation: {
    instructions: string;
    /** Reviewed starting values, adjustable for each task. */
    defaults: TaskSettings;
  };
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
/** A fixed value or a parent-adjustable default with explicitly requested bounds. */
export interface IntegerChoice {
  value: number;
  adjustable: boolean;
  min?: number | null;
  max?: number | null;
}
/** Generated-body word expectation; supplied sources have no length requirement. */
export interface LengthExpectation {
  mode: 'target' | 'exact' | 'range';
  count?: IntegerChoice | null;
  lower?: number | null;
  upper?: number | null;
}
/** A requested choice at its containing scope. IDs are app-owned, never editable keys. */
export interface PlanControl {
  id: string;
  label: string;
  type: 'text' | 'integer' | 'select' | 'boolean';
  meaning: string;
  required?: boolean;
  default?: ParameterValue | null;
  unit?: string | null;
  min?: number | null;
  max?: number | null;
  maxLength?: number | null;
  options?: { value: string; meaning?: string | null }[] | null;
}
/** A generated material or a source copied verbatim from the parent. */
export interface PlanMaterial {
  id: string;
  label: string;
  source: 'generated' | 'fixed' | 'per-task';
  guidance: string;
  text?: string | null;
  length?: LengthExpectation | null;
  controls: PlanControl[];
}
/** Canonical learning requirements. Preserve the schema version supplied by the API. */
export interface LearningPlan {
  schemaVersion: number;
  name: string;
  goal: string;
  guidance: string;
  defaults: TaskSettings;
  materials: PlanMaterial[];
  controls: PlanControl[];
  totalLength?: LengthExpectation | null;
  questions: {
    formats: QuestionFormat[];
    selectableFormat: boolean;
    defaultFormat?: QuestionFormat | null;
    choiceCount?: IntegerChoice | null;
    countBounds?: { min?: number | null; max?: number | null } | null;
    guidance: string;
    controls: PlanControl[];
  };
}
/** Omitted overrides use defaults. Null and numeric strings never cross this boundary. */
export interface ActivityInput {
  settings: TaskSettings;
  questionFormat?: QuestionFormat;
  choiceCount?: number;
  totalWordCount?: number;
  materialInputs?: Record<string, { wordCount?: number; sourceText?: string }>;
  controlValues?: ParameterValues;
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
/** Immutable template version in the staged content-first API. */
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
  id: string | null;
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
    id: string;
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
/** Server-owned generated-body measurement; target expectations are advisory (satisfied is null). */
export interface LengthMeasurement {
  scope: string;
  expected: {
    mode: 'target' | 'exact' | 'range';
    value: number | null;
    lower: number | null;
    upper: number | null;
  };
  actual: number;
  satisfied: boolean | null;
}
/** Saved authoritative state; diagnostics are server-derived, not local readiness claims. */
export interface ActivityDetail {
  id: string;
  revision: number;
  plan: LearningPlan;
  input: ActivityInput;
  document: ActivityDocument;
  diagnostics: Record<string, string[]>;
  measurements: LengthMeasurement[];
  activeOperationId: string | null;
  templateVersionId: string | null;
  releasedSnapshotId: string | null;
  releasedSourceRevision: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}
export interface ActivitySummary {
  id: string;
  name: string;
  revision: number;
  updatedAtUtc: string;
}
export type GenerationKind =
  'GenerateActivity' | 'GenerateQuestions' | 'ReplaceMaterial' | 'ReplaceQuestion';
/** Keep this exact request for explicit same-key recovery after a lost response. */
export interface StartGeneration {
  operationKey: string;
  expectedRevision: number;
  kind: GenerationKind;
  targetId?: string;
  instruction?: string;
}
/** Polling this parent-only evidence never starts a call. Unknown candidates remain diagnostic text. */
export interface GenerationOperation {
  id: string;
  draftId: string;
  kind: GenerationKind;
  status: 'queued' | 'calling' | 'completed' | 'failed' | 'conflict' | 'cancelled' | 'unknown';
  stage: string;
  originalRevision: number;
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
  input: ActivityInput;
  document: ActivityDocument;
  reviewedAtUtc: string;
  measurements: LengthMeasurement[];
}
export interface SnapshotSummary {
  id: string;
  title: string;
  status: 'Ready';
  createdAtUtc: string;
}

/** Transient AI proposal. The parent must review it and explicitly publish a template. */
export interface AiTemplateDraft {
  definition: TemplateDefinition;
  generationMetadata: GenerationMetadata;
}

/** Template list projection; fetch TemplateDetail when the definition is needed. */
export interface TemplateSummary {
  id: string;
  name: string;
  currentVersion: number;
  createdAtUtc: string;
}

/** Stable template ID paired with the selected published revision and its definition. */
export interface TemplateDetail {
  id: string;
  currentVersion: number;
  versionId: string;
  definition: TemplateDefinition;
}

/** Frozen parent-preview content, including answer keys. Never reuse for a child response. */
interface TaskContent {
  title: string;
  instructions: string | null;
  contentBlocks: { type: 'text'; text: string }[];
  questions: {
    id: string;
    prompt: string;
    interaction: {
      type: 'numeric-input' | 'text-input' | 'single-choice';
      options: string[] | null;
    };
    answer: { value: string };
    points: number;
  }[];
}

/** Saved-task list entry without question content. */
export interface InstanceSummary {
  id: string;
  title: string;
  status: 'Draft';
  createdAtUtc: string;
}

/** Saved parent preview; never regenerate on read or expose its answers to a child. */
export interface InstancePreview {
  id: string;
  status: 'Draft';
  createdAtUtc: string;
  generationMetadata: GenerationMetadata | null;
  templateVersionId: string;
  templateVersion: number;
  input: TaskInput;
  content: TaskContent;
}
