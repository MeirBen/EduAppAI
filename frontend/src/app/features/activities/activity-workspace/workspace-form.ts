import {
  ActivityDetail,
  ActivityDocument,
  ActivityInput,
  ContentLimits,
  LearningPlan,
  PlanMaterial,
} from '../../../core/api/models';
import { TaskSettingsDraft, taskSettingsDraft } from '../../../shared/forms/task-settings';
import { DocumentEdit } from '../activity-document-editor/activity-document-editor';
import { DocumentForm, documentForm } from '../activity-document-editor/document-form';
import { PlanStructureEdit } from '../plan-editor/plan-editor';
import {
  controlForm,
  formControls,
  InputForm,
  inputForm,
  materialForm,
  newPlanId,
  PlanForm,
  planForm,
} from '../plan-editor/plan-form';

/** One editable buffer; canonical plan, input and document values are derived projections, never a second draft. */
export interface WorkspaceForm {
  plan: PlanForm;
  input: InputForm;
  document: DocumentForm;
}

/** Fixed source text the parent has confirmed, keyed by material ID. */
export type ConfirmedSources = Record<string, string>;

/** A restorable local state: the buffer and the sources confirmed with it. */
export interface WorkspaceSnapshot {
  raw: WorkspaceForm;
  confirmed: ConfirmedSources;
}

const blankPlan = JSON.stringify(planForm());

/** The buffer for a new activity; the server's schema version arrives separately. */
export function emptyWorkspace(): WorkspaceForm {
  return {
    plan: planForm(),
    document: documentForm(),
    input: {
      settings: planForm().settings,
      questionFormat: '',
      choiceCount: '',
      totalWordCount: '',
      materials: [],
      controls: [],
    },
  };
}

/** The buffer for a saved draft, or for a template's plan when there is no draft yet. */
export function workspaceForm(
  plan: LearningPlan,
  input?: ActivityInput,
  document?: ActivityDocument,
): WorkspaceForm {
  return { plan: planForm(plan), input: inputForm(plan, input), document: documentForm(document) };
}

/** Whether the plan holds anything; the server-supplied schema version alone does not make a plan. */
export const hasPlanContent = (plan: PlanForm) =>
  JSON.stringify({ ...plan, schemaVersion: 0 }) !== blankPlan;

export const withSchemaVersion = (raw: WorkspaceForm, schemaVersion: number): WorkspaceForm => ({
  ...raw,
  plan: { ...raw.plan, schemaVersion },
});

/** Fixed source text in a plan; with `confirmed`, only the text the parent already confirmed. */
export function fixedSources(plan: LearningPlan, confirmed?: ConfirmedSources): ConfirmedSources {
  return Object.fromEntries(
    plan.materials
      .filter(
        (material) =>
          material.source === 'fixed' && (!confirmed || confirmed[material.id] === material.text),
      )
      .map((material) => [material.id, material.text!]),
  );
}

/** A source-kind change is a new material, not a rewrite of the saved material's identity. */
export function materialIdentity(
  saved: LearningPlan | undefined,
  id: string,
  source: PlanMaterial['source'],
): string {
  const existing = saved?.materials.find((material) => material.id === id);
  return existing && existing.source !== source ? newPlanId() : id;
}

/** Per-activity settings still equal to the old plan default follow the new one; overrides stay. */
function followDefaults(
  before: TaskSettingsDraft,
  after: TaskSettingsDraft,
  input: TaskSettingsDraft,
): TaskSettingsDraft {
  return {
    topic: input.topic === before.topic ? after.topic : input.topic,
    audience: input.audience === before.audience ? after.audience : input.audience,
    difficulty: input.difficulty === before.difficulty ? after.difficulty : input.difficulty,
    questionCount:
      input.questionCount === before.questionCount ? after.questionCount : input.questionCount,
  };
}

/**
 * Re-aligns the buffer after a plan edit: material identities, per-activity inputs and document
 * materials follow the plan, and untouched settings follow changed defaults.
 */
export function reconcile(
  raw: WorkspaceForm,
  previousDefaults: TaskSettingsDraft,
  saved: LearningPlan | undefined,
): WorkspaceForm {
  const plan = {
    ...raw.plan,
    materials: raw.plan.materials.map((material) => ({
      ...material,
      id: materialIdentity(saved, material.id, material.source),
    })),
  };
  return {
    ...raw,
    plan,
    document: {
      ...raw.document,
      materials: raw.document.materials.filter((m) => plan.materials.some((p) => p.id === m.id)),
    },
    input: {
      ...raw.input,
      settings: followDefaults(previousDefaults, plan.settings, raw.input.settings),
      controls: formControls(plan).map(
        (control) =>
          raw.input.controls.find((value) => value.id === control.id) ?? {
            id: control.id,
            provided: false,
            value: '',
          },
      ),
      materials: plan.materials.map(
        (material) =>
          raw.input.materials.find((value) => value.id === material.id) ?? {
            id: material.id,
            wordCount: '',
            sourceText: '',
          },
      ),
    },
  };
}

/**
 * The buffer after an AI proposal. When refining, per-activity values, including invalid typing,
 * survive by identity; the HTTP projection omits overrides the new plan no longer accepts. When an
 * activity's settings are its plan defaults, the proposed defaults replace them.
 */
export function proposedWorkspace(
  raw: WorkspaceForm,
  plan: LearningPlan,
  refining: boolean,
  settingsAreDefaults: boolean,
): WorkspaceForm {
  const input = inputForm(plan),
    prior = raw.input;
  if (refining)
    Object.assign(input, {
      ...prior,
      settings: settingsAreDefaults
        ? taskSettingsDraft(plan.defaults)
        : followDefaults(raw.plan.settings, taskSettingsDraft(plan.defaults), prior.settings),
      materials: input.materials.map(
        (item) => prior.materials.find((old) => old.id === item.id) ?? item,
      ),
      controls: input.controls.map(
        (item) => prior.controls.find((old) => old.id === item.id) ?? item,
      ),
    });
  return {
    ...raw,
    plan: planForm(plan),
    input,
    document: {
      ...raw.document,
      materials: raw.document.materials.filter((material) =>
        plan.materials.some((item) => item.id === material.id),
      ),
    },
  };
}

/** Adds or removes a material or choice within the limits; undefined when the scope is gone. */
export function editPlanStructure(
  raw: WorkspaceForm,
  edit: PlanStructureEdit,
  limits: ContentLimits,
): WorkspaceForm | undefined {
  const next = structuredClone(raw),
    plan = next.plan;
  if (edit.kind === 'add-material' && plan.materials.length < limits.maxMaterials)
    plan.materials.push(materialForm());
  if (edit.kind === 'remove-material')
    plan.materials = plan.materials.filter((material) => material.id !== edit.id);
  if (edit.kind === 'add-control' || edit.kind === 'remove-control') {
    const controls =
      edit.scope === 'plan'
        ? plan.controls
        : edit.scope === 'questions'
          ? plan.questions.controls
          : plan.materials.find((material) => material.id === edit.scope)?.controls;
    if (!controls) return undefined;
    if (edit.kind === 'add-control' && formControls(plan).length < limits.maxControls)
      controls.push(controlForm());
    if (edit.kind === 'remove-control') {
      const index = controls.findIndex((control) => control.id === edit.id);
      if (index >= 0) controls.splice(index, 1);
    }
  }
  return next;
}

/** Adds, removes or moves questions and options, or adds a material body; undefined for a stale index. */
export function editDocumentStructure(
  raw: WorkspaceForm,
  edit: DocumentEdit,
  limits: ContentLimits,
): WorkspaceForm | undefined {
  const next = structuredClone(raw),
    doc = next.document;
  if (edit.kind === 'add-question' && doc.questions.length < limits.maxQuestionCount)
    doc.questions.push({
      id: '',
      key: crypto.randomUUID(),
      prompt: '',
      type: 'numeric-input',
      options: [],
      answer: '',
      points: '1',
    });
  else if (edit.kind === 'add-material' && !doc.materials.some((m) => m.id === edit.id))
    doc.materials.push({ id: edit.id, title: '', body: '' });
  else if ('index' in edit) {
    const question = doc.questions[edit.index];
    if (!question) return undefined;
    if (edit.kind === 'remove-question') doc.questions.splice(edit.index, 1);
    if (edit.kind === 'add-option' && question.options.length < limits.maxChoiceCount)
      question.options.push({ value: '' });
    if (edit.kind === 'remove-option') question.options.splice(edit.option, 1);
    const target =
      edit.kind === 'move-up' ? edit.index - 1 : edit.kind === 'move-down' ? edit.index + 1 : -1;
    if (target >= 0 && target < doc.questions.length)
      [doc.questions[edit.index], doc.questions[target]] = [doc.questions[target], question];
  }
  return next;
}

/** The current text of a fixed or per-activity source; undefined for generated materials. */
export function sourceText(raw: WorkspaceForm, id: string): string | undefined {
  const material = raw.plan.materials.find((m) => m.id === id);
  if (!material || material.source === 'generated') return undefined;
  return material.source === 'fixed'
    ? material.text
    : (raw.input.materials.find((m) => m.id === id)?.sourceText ?? '');
}

/** Replaces a source's text in the plan or per-activity input and in the document body. */
export function replaceSourceText(
  raw: WorkspaceForm,
  id: string,
  text: string,
): WorkspaceForm | undefined {
  const next = structuredClone(raw),
    material = next.plan.materials.find((m) => m.id === id);
  if (!material) return undefined;
  if (material.source === 'fixed') material.text = text;
  else {
    const input = next.input.materials.find((m) => m.id === id);
    if (input) input.sourceText = text;
  }
  const content = next.document.materials.find((m) => m.id === id);
  if (content) content.body = text;
  return next;
}

/** A save may have removed restored questions; IDs the draft no longer holds become new questions. */
export function withSavedQuestionIds(raw: WorkspaceForm, saved: ActivityDetail): WorkspaceForm {
  const ids = new Set(saved.document.questions.map((question) => question.id));
  return {
    ...raw,
    document: {
      ...raw.document,
      questions: raw.document.questions.map((q) => ({ ...q, id: ids.has(q.id) ? q.id : '' })),
    },
  };
}
