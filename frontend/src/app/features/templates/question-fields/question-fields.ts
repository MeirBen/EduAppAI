import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { choiceOptions, QuestionDraft } from '../template-form/template-draft';

/** Native question controls; the parent Signal Form owns validation and persistence. */
@Component({
  imports: [FormField],
  selector: 'app-question-fields',
  templateUrl: './question-fields.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionFields {
  readonly fields = input.required<FieldTree<QuestionDraft, number>>();
  readonly index = input.required<number>();
  readonly count = input.required<number>();
  readonly removed = output<void>();
  readonly moved = output<number>();
  protected readonly options = computed(() => choiceOptions(this.fields().choices().value()));
}
