import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { QuestionFormat } from '../../../core/api/models';
import { TaskSettingsFields } from '../../../shared/forms/task-settings-fields';
import { activityFormats, formatNames } from '../activity-presentation';
import {
  ChoiceForm,
  ControlForm,
  InputForm,
  MaterialForm,
  PlanForm,
} from '../plan-editor/plan-form';

/**
 * Ordinary per-activity choices and source text in parent language. Edits the owner's field trees
 * and emits events; it holds no draft copy, requests or confirmation state.
 */
@Component({
  selector: 'app-activity-setup',
  imports: [FormField, TaskSettingsFields, NgTemplateOutlet],
  templateUrl: './activity-setup.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(input)': 'changed($event)', '(change)': 'changed($event)' },
})
export class ActivitySetup {
  readonly plan = input.required<FieldTree<PlanForm>>();
  readonly inputs = input.required<FieldTree<InputForm>>();
  readonly pendingSources = input<string[]>([]);
  readonly locked = input(false);
  /** Template context: per-activity choices become secondary and every source mode is offered. */
  readonly reusable = input(false);
  readonly edited = output<{ key: string; sourceId?: string }>();
  readonly sourceConfirmed = output<string>();
  protected readonly formatNames = formatNames;
  protected readonly isGenerated = (material: MaterialForm) => material.source === 'generated';
  protected readonly controls = computed(() => {
    const plan = this.plan()().value();
    return [
      ...plan.controls,
      ...plan.materials.flatMap((material) => material.controls),
      ...plan.questions.controls,
    ];
  });
  protected readonly allowedFormats = computed(() => {
    const questions = this.plan()().value().questions;
    return (['numeric-input', 'text-input', 'single-choice'] as const).filter(
      (format) =>
        ({
          'numeric-input': questions.numeric,
          'text-input': questions.text,
          'single-choice': questions.choice,
        })[format],
    );
  });
  /** Choice count applies only where single-choice questions will actually be generated. */
  protected readonly choiceCountApplies = computed(() => {
    const plan = this.plan()().value();
    return (
      plan.questions.choiceCount.adjustable &&
      activityFormats(plan, this.inputs()().value()).includes('single-choice')
    );
  });
  protected formatName(format: '' | QuestionFormat) {
    return format ? formatNames[format] : 'בחרו סוג';
  }
  protected bounds(choice: Pick<ChoiceForm, 'min' | 'max'>) {
    return choice.min && choice.max
      ? ` · אפשר לבחור ${choice.min}–${choice.max}`
      : choice.min
        ? ` · לפחות ${choice.min}`
        : choice.max
          ? ` · עד ${choice.max}`
          : '';
  }
  protected defaultLabel(control: ControlForm) {
    if (!control.hasDefault) return control.required ? 'בחרו' : 'ללא בחירה';
    const value =
      control.type === 'boolean'
        ? control.defaultValue === 'true'
          ? 'כן'
          : 'לא'
        : control.defaultValue;
    return `ברירת המחדל (${value})`;
  }
  protected changed(event: Event) {
    if (this.locked() || !(event.target instanceof HTMLElement)) return;
    this.edited.emit({ key: event.target.id, sourceId: event.target.dataset['source'] });
  }
}
