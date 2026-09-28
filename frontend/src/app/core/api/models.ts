/** JSON scalar accepted by the parameter form; the server validates its type and bounds. */
export type ParameterValue = string | number | boolean;
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
  schemaVersion: 1;
  name: string;
  instanceParameters: ParameterDefinition[];
  generation: {
    mode: 'deterministic';
    generator: 'math-v1';
    fixedSettings: { operation: 'multiplication' };
  };
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
export interface TaskContent {
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

/** Saved-task list projection. Only the draft lifecycle is implemented in this increment. */
export interface InstanceSummary {
  id: string;
  title: string;
  status: 'Draft';
  createdAtUtc: string;
}

/**
 * Parent-only saved snapshot. Reloading it reads stored content without generation.
 * A future child contract must omit answers and enforce assignment access on the server.
 */
export interface InstancePreview extends InstanceSummary {
  templateVersionId: string;
  templateVersion: number;
  parameters: ParameterValues;
  content: TaskContent;
}
