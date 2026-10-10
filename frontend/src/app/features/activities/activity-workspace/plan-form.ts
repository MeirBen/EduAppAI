import { applyEach, maxLength, schema } from '@angular/forms/signals';
import { ContentLimits, LearningPlan, PlanMaterial } from '../../../core/api/models';

/** Server-validated settings stay canonical; only supplied source text is editable before save. */
export type PlanForm = Omit<LearningPlan, 'materials'> & {
  materials: (Omit<PlanMaterial, 'text'> & { text: string })[];
};

export const planFormSchema = (limits: ContentLimits) =>
  schema<PlanForm>((path) => {
    applyEach(path.materials, (material) => maxLength(material.text, limits.bodyLength));
  });

/** Empty setup is not a plan; authoring or loading supplies all concrete requirements. */
export function planForm(plan?: LearningPlan): PlanForm {
  if (plan) {
    const copy = structuredClone(plan);
    return {
      ...copy,
      materials: copy.materials.map((material) => ({ ...material, text: material.text ?? '' })),
    };
  }
  return {
    schemaVersion: 0,
    name: '',
    goal: '',
    guidance: '',
    documentGuidance: '',
    settings: { topic: '', audience: '', difficulty: 'medium', questionCount: 1 },
    materials: [],
    totalLength: null,
    questions: { formats: [], choiceCount: null, guidance: '' },
  };
}
