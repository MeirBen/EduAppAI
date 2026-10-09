import { ActivityDocument, LearningPlan } from '../../../core/api/models';
import { DocumentForm, documentForm } from '../activity-document-editor/document-form';
import { PlanForm, planForm } from './plan-form';

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

/** The buffer for a saved draft or an unsaved authoring proposal. */
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
