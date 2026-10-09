import { ContentLimits, LearningPlan } from '../../../core/api/models';
import { count } from '../../../core/api/limits';
import { FieldIssue, Projection } from '../../../shared/forms/projection';
import { PlanForm } from './plan-form';

/** Settings come from validated server replies; only the parent's source input needs local checks. */
export function planValue(form: PlanForm, limits: ContentLimits): Projection<LearningPlan> {
  const errors: FieldIssue[] = [];
  if (!form.schemaVersion || !form.name || !form.goal)
    errors.push({ path: [], message: 'תארו את הפעילות כדי להכין את ההגדרות.' });
  for (const [index, material] of form.materials.entries()) {
    if (material.source !== 'supplied') continue;
    const message = !material.text.trim()
      ? 'זהו שדה חובה.'
      : material.text.length > limits.bodyLength
        ? `אפשר להזין עד ${count(limits.bodyLength)} תווים.`
        : '';
    if (message) errors.push({ path: ['plan', 'materials', index, 'text'], message });
  }
  return {
    errors,
    value: errors.length
      ? undefined
      : {
          ...form,
          materials: form.materials.map((material) => ({
            ...material,
            text: material.source === 'supplied' ? material.text : null,
          })),
        },
  };
}
