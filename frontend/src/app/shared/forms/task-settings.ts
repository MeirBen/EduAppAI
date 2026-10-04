import { TaskSettings } from '../../core/api/models';

/** Keep numeric input as text until submission so blank or fractional values stay invalid. */
export type TaskSettingsDraft = Omit<TaskSettings, 'questionCount'> & { questionCount: string };

/** Parent wording for each difficulty; the keys are also the only accepted values. */
export const difficultyLabels = { easy: 'קלה', medium: 'בינונית', hard: 'קשה' } satisfies Record<
  TaskSettings['difficulty'],
  string
>;

export function taskSettingsDraft(settings: TaskSettings): TaskSettingsDraft {
  return { ...settings, questionCount: String(settings.questionCount) };
}

/** Called only after the form passes validation. */
export function taskSettingsValue(draft: TaskSettingsDraft): TaskSettings {
  return { ...draft, questionCount: Number(draft.questionCount) };
}
