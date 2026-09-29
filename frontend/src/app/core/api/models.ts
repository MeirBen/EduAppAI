/** JSON scalar parameter value; the server validates type and bounds. */
type ParameterValue = string | number | boolean;
/** Values keyed by case-sensitive schema keys. Omitted keys allow server defaults. */
export type ParameterValues = Record<string, ParameterValue>;

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
  schemaVersion: 2;
  name: string;
  instanceParameters: ParameterDefinition[];
  generation: {
    instructions: string;
    questionCountParameter?: string | null;
    /** Optional inclusive word limits across all text blocks; an omitted bound is open. */
    contentWordCount?: { min?: number | null; max?: number | null } | null;
  };
}

/** Server-recorded generation diagnostics; excludes prompts, identity and model reasoning. */
interface GenerationMetadata {
  provider: string;
  model: string;
  promptVersion: string;
  generatedAtUtc: string;
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
  parameters: ParameterValues;
  content: TaskContent;
}
