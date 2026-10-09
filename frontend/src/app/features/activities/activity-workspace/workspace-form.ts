import { ActivityDocument, ContentLimits, LearningPlan } from '../../../core/api/models';
import { DocumentForm, documentForm } from '../activity-document-editor/document-form';
import { PlanStructureEdit } from '../plan-editor/plan-editor';
import { materialForm, PlanForm, planForm } from '../plan-editor/plan-form';

/** One editable buffer; canonical plan and document values are derived projections, never a second draft. */
export interface WorkspaceForm {
  plan: PlanForm;
  document: DocumentForm;
}

/** Supplied source text the parent has confirmed, keyed by material ID. */
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
  };
}

/** The buffer for a saved draft, or for a template's plan when there is no draft yet. */
export function workspaceForm(plan: LearningPlan, document?: ActivityDocument): WorkspaceForm {
  return { plan: planForm(plan), document: documentForm(document) };
}

/** Whether the plan holds anything; the server-supplied schema version alone does not make a plan. */
export const hasPlanContent = (plan: PlanForm) =>
  JSON.stringify({ ...plan, schemaVersion: 0 }) !== blankPlan;

export const withSchemaVersion = (raw: WorkspaceForm, schemaVersion: number): WorkspaceForm => ({
  ...raw,
  plan: { ...raw.plan, schemaVersion },
});

/** Supplied source text in a plan; with `confirmed`, only the text the parent already confirmed. */
export function fixedSources(plan: LearningPlan, confirmed?: ConfirmedSources): ConfirmedSources {
  return Object.fromEntries(
    plan.materials
      .filter(
        (material) =>
          material.source === 'supplied' &&
          (!confirmed || confirmed[material.id] === material.text),
      )
      .map((material) => [material.id, material.text!]),
  );
}

/** Keeps only document materials still referenced by an unsaved plan edit. */
export function reconcile(raw: WorkspaceForm): WorkspaceForm {
  return {
    ...raw,
    document: {
      ...raw.document,
      materials: raw.document.materials.filter((m) =>
        raw.plan.materials.some((p) => p.id === m.id),
      ),
    },
  };
}

/** Applies an unsaved plan proposal while retaining content still referenced by the plan. */
export function proposedWorkspace(raw: WorkspaceForm, plan: LearningPlan): WorkspaceForm {
  return {
    ...raw,
    plan: planForm(plan),
    document: {
      ...raw.document,
      materials: raw.document.materials.filter((material) =>
        plan.materials.some((item) => item.id === material.id),
      ),
    },
  };
}

/** Adds or removes a material within the limits. */
export function editPlanStructure(
  raw: WorkspaceForm,
  edit: PlanStructureEdit,
  limits: ContentLimits,
): WorkspaceForm {
  const next = structuredClone(raw),
    plan = next.plan;
  if (edit.kind === 'add-material' && plan.materials.length < limits.maxMaterials)
    plan.materials.push(materialForm());
  if (edit.kind === 'remove-material')
    plan.materials = plan.materials.filter((material) => material.id !== edit.id);
  return next;
}

/** The current text of a supplied source; undefined for generated materials. */
export function sourceText(raw: WorkspaceForm, id: string): string | undefined {
  const material = raw.plan.materials.find((m) => m.id === id);
  if (!material || material.source === 'generated') return undefined;
  return material.text;
}

/** Replaces a source's text in the plan and in the document body. */
export function replaceSourceText(
  raw: WorkspaceForm,
  id: string,
  text: string,
): WorkspaceForm | undefined {
  const next = structuredClone(raw),
    material = next.plan.materials.find((m) => m.id === id);
  if (!material) return undefined;
  if (material.source !== 'supplied') return undefined;
  material.text = text;
  const content = next.document.materials.find((m) => m.id === id);
  if (content) content.body = text;
  return next;
}
