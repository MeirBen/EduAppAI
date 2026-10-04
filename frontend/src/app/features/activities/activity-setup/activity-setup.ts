import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { TaskSettingsFields } from '../../../shared/forms/task-settings-fields';
import { lengthText } from '../activity-document-view/measurements';
import { activityFormats, formatNames } from '../activity-presentation';
import {
  ControlForm,
  formControls,
  formFormats,
  InputForm,
  LengthForm,
  PlanForm,
} from '../plan-editor/plan-form';
import { FieldDirection } from '../../../shared/forms/field-direction';
import { DisabledInteractive } from '../../../shared/disabled-interactive';

/** One generated-length row: an adjustable word count, or a fixed requirement read aloud. */
interface LengthChoice {
  id: string;
  label: string;
  length: LengthForm;
  requirement: string;
  adjustable: boolean;
  /** Index of the material's input, or null for the combined total. */
  inputIndex: number | null;
}

/**
 * The plan defaults and source text in parent language, and for an activity its per-activity
 * choices. Edits the owner's field trees and emits events; it holds no draft copy, requests or
 * confirmation state.
 */
@Component({
  selector: 'app-activity-setup',
  imports: [DisabledInteractive, FormField, FieldDirection, TaskSettingsFields],
  templateUrl: './activity-setup.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(input)': 'changed($event)', '(change)': 'changed($event)' },
})
export class ActivitySetup {
  readonly plan = input.required<FieldTree<PlanForm>>();
  readonly inputs = input.required<FieldTree<InputForm>>();
  readonly pendingSources = input<string[]>([]);
  readonly locked = input(false);
  /** Template context: there is no activity yet, so only defaults and sources show, with every source mode. */
  readonly reusable = input(false);
  readonly edited = output<{ key: string; sourceId?: string }>();
  readonly sourceConfirmed = output<string>();
  protected readonly formatNames = formatNames;
  protected readonly controls = computed(() => formControls(this.plan()().value()));
  protected readonly formats = computed(() => formFormats(this.plan()().value().questions));
  /** With several texts, every field that belongs to one text names it. */
  protected readonly severalTexts = computed(() => this.plan()().value().materials.length > 1);
  /** The text that owns each text-scoped choice, when texts are named. */
  protected readonly choiceTexts = computed(() => {
    if (!this.severalTexts()) return new Map<string, string>();
    const { materials } = this.plan()().value();
    return new Map(materials.flatMap((m) => m.controls.map((c) => [c.id, m.label] as const)));
  });
  /**
   * The empty option stands for the plan default, so the default is not listed twice unless an
   * explicit choice of it was saved earlier.
   */
  protected readonly formatOptions = computed(() => {
    const { defaultFormat } = this.plan()().value().questions,
      chosen = this.inputs()().value().questionFormat;
    return [
      { value: '', label: defaultFormat ? formatNames[defaultFormat] : 'בחרו סוג' },
      ...this.formats()
        .filter((format) => format !== defaultFormat || format === chosen)
        .map((format) => ({ value: format, label: formatNames[format] })),
    ];
  });
  /** Choice count applies only where single-choice questions will actually be generated. */
  protected readonly choiceCountApplies = computed(() => {
    const plan = this.plan()().value();
    return (
      plan.questions.choiceCount.adjustable &&
      activityFormats(plan, this.inputs()().value()).includes('single-choice')
    );
  });
  protected readonly lengthChoices = computed((): LengthChoice[] => {
    const plan = this.plan()().value(),
      inputs = this.inputs()().value();
    const generated = plan.materials
      .map((material, index) => ({ material, index }))
      .filter(({ material }) => material.source === 'generated');
    const choices = generated.map(({ material, index }) =>
      this.lengthChoice(
        material.id + '-input',
        this.severalTexts() ? material.label : '',
        material.length,
        inputs.materials[index] ? index : undefined,
      ),
    );
    if (generated.length)
      choices.push(this.lengthChoice('input-total', 'כל הטקסטים יחד', plan.totalLength, null));
    return choices.filter((choice): choice is LengthChoice => !!choice);
  });
  /** Format, option-count and length rows; their group renders only when the plan offers one. */
  protected readonly hasPlanChoices = computed(
    () =>
      this.plan()().value().questions.selectableFormat ||
      this.formats().length > 1 ||
      this.choiceCountApplies() ||
      !!this.lengthChoices().length,
  );
  /** Reads an integer choice's allowed range as parents say it. */
  protected bounds(control: Pick<ControlForm, 'min' | 'max'>) {
    return control.min && control.max
      ? ` · אפשר לבחור ${control.min}–${control.max}`
      : control.min
        ? ` · לפחות ${control.min}`
        : control.max
          ? ` · עד ${control.max}`
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
  /** A range is never adjustable per activity; its endpoints are the output requirement itself. */
  private lengthChoice(
    id: string,
    label: string,
    length: LengthForm,
    inputIndex: number | null | undefined,
  ): LengthChoice | undefined {
    const { mode, value, lower, upper } = length;
    if (!mode) return undefined;
    return {
      id,
      label,
      length,
      requirement: lengthText({ mode, value, lower, upper }),
      adjustable: length.adjustable && mode !== 'range' && inputIndex !== undefined,
      inputIndex: inputIndex ?? null,
    };
  }
}
