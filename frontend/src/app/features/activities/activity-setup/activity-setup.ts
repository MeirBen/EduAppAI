import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { activitySummary, lengthPhrase } from '../activity-presentation';
import { PlanForm } from '../activity-workspace/plan-form';
import { FieldDirection } from '../../../shared/forms/field-direction';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { FieldErrors } from '../../../shared/forms/field-errors';
import { FieldValidity } from '../../../shared/forms/field-validity';

/** Concrete activity settings and confirmed source text in the workspace's own buffer. */
@Component({
  selector: 'app-activity-setup',
  imports: [FieldValidity, FieldErrors, DisabledInteractive, FormField, FieldDirection],
  templateUrl: './activity-setup.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(input)': 'changed($event)', '(change)': 'changed($event)' },
})
export class ActivitySetup {
  readonly plan = input.required<FieldTree<PlanForm>>();
  readonly pendingSources = input<string[]>([]);
  readonly locked = input(false);
  readonly edited = output<{ key: string }>();
  readonly sourceConfirmed = output<string>();
  protected readonly summary = computed(() => activitySummary(this.plan()().value()));
  protected readonly lengthPhrase = lengthPhrase;
  protected changed(event: Event) {
    if (this.locked() || !(event.target instanceof HTMLElement)) return;
    this.edited.emit({ key: event.target.id });
  }
}
