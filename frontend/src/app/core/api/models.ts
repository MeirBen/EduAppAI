export type ParameterValue = string | number | boolean;
export type ParameterValues = Record<string, ParameterValue>;

export interface ParameterDefinition {
  key: string;
  label: string;
  type: 'text' | 'integer' | 'select' | 'boolean';
  required?: boolean;
  default?: ParameterValue | null;
  min?: number | null;
  max?: number | null;
  maxLength?: number | null;
  options?: string[] | null;
}

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

export interface TemplateSummary {
  id: string;
  name: string;
  currentVersion: number;
  createdAtUtc: string;
}

export interface TemplateDetail {
  id: string;
  currentVersion: number;
  versionId: string;
  definition: TemplateDefinition;
}

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

export interface InstanceSummary {
  id: string;
  title: string;
  status: 'Draft';
  createdAtUtc: string;
}

// Parent-only contract. A future child contract must omit answers.
export interface InstancePreview extends InstanceSummary {
  templateVersionId: string;
  templateVersion: number;
  parameters: ParameterValues;
  content: TaskContent;
}
