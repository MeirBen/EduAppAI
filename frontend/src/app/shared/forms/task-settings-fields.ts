import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { difficultyLabels, TaskSettingsDraft } from './task-settings';

/** The same accessible controls edit template defaults and task choices. The parent owns the form. */
@Component({
  selector: 'app-task-settings-fields',
  imports: [FormField],
  templateUrl: './task-settings-fields.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TaskSettingsFields {
  readonly fields = input.required<FieldTree<TaskSettingsDraft>>();
  readonly prefix = input.required<string>();
  protected readonly difficultyOptions = Object.entries(difficultyLabels);
  protected readonly controls = [
    { key: 'topic', label: 'נושא' },
    { key: 'audience', label: 'למי?', hint: 'למשל כיתה ג׳, או מבוגרים ללא ידע קודם.' },
    { key: 'difficulty', label: 'רמת קושי', hint: 'ביחס לגיל או לכיתה שבחרתם.' },
    { key: 'questionCount', label: 'מספר שאלות' },
  ] as const;
}
