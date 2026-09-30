import { schema, validate } from '@angular/forms/signals';
import { TaskSettings } from '../../core/api/models';
import { isIntegerInput } from './integer-input';

/** Keep numeric input as text until submission so blank or fractional values stay invalid. */
export type TaskSettingsDraft = Omit<TaskSettings, 'questionCount'> & { questionCount: string };

export const difficultyLabels = { easy: 'קלה', medium: 'בינונית', hard: 'קשה' } satisfies Record<
  TaskSettings['difficulty'],
  string
>;

/** Shared by template defaults and per-task choices; the API validates independently. */
export const taskSettingsSchema = schema<TaskSettingsDraft>((path) => {
  for (const key of ['topic', 'audience'] as const) {
    validate(path[key], ({ value }) =>
      value().trim() && value().length <= 200
        ? undefined
        : { kind: key, message: 'יש להזין טקסט עד 200 תווים.' },
    );
  }
  validate(path.difficulty, ({ value }) =>
    Object.hasOwn(difficultyLabels, value())
      ? undefined
      : { kind: 'difficulty', message: 'יש לבחור רמת קושי.' },
  );
  validate(path.questionCount, ({ value }) =>
    isIntegerInput(value()) && Number(value()) >= 1
      ? undefined
      : { kind: 'questionCount', message: 'יש להזין מספר שלם גדול מאפס.' },
  );
});

export function taskSettingsDraft(settings: TaskSettings): TaskSettingsDraft {
  return { ...settings, questionCount: String(settings.questionCount) };
}

/** Called only after the form passes validation. */
export function taskSettingsValue(draft: TaskSettingsDraft): TaskSettings {
  return { ...draft, questionCount: Number(draft.questionCount) };
}
