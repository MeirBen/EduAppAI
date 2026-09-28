import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  input,
  linkedSignal,
  output,
  signal,
} from '@angular/core';
import { apply, disabled, form, FormField, hidden, max, min, submit } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import { TemplateDetail } from '../../../core/api/models';
import { mathOperations, mathRange } from '../../../core/locale/hebrew';
import { QuestionFields } from '../question-fields/question-fields';
import {
  authoredTextLength,
  blankQuestion,
  templateDefinition,
  templateDraft,
} from './template-draft';
import { templateSchema } from './template-schema';

/** Authors a new definition or publishes a revision; conflicts preserve the current local form. */
@Component({
  imports: [FormField, RouterLink, QuestionFields],
  selector: 'app-template-form',
  templateUrl: './template-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TemplateForm {
  /** Replacing the loaded revision intentionally resets the editor; ordinary errors do not. */
  readonly template = input<TemplateDetail>();
  readonly saved = output<TemplateDetail>();
  readonly reloadRequested = output<void>();
  private readonly api = inject(LearningApi);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  protected readonly model = linkedSignal(() => templateDraft(this.template()?.definition));
  protected readonly saving = signal(false);
  protected readonly attempted = signal(false);
  protected readonly error = signal('');
  protected readonly conflict = signal(false);
  protected readonly operations = mathOperations;
  protected readonly range = mathRange;
  protected readonly textLength = computed(() => authoredTextLength(this.model()));
  protected readonly countBounds = computed(() => {
    const parameter = this.template()?.definition.instanceParameters.find(
      (field) => field.key === 'questionCount',
    );
    return { min: parameter?.min ?? 1, max: parameter?.max ?? 20 };
  });
  protected readonly fields = form(this.model, (path) => {
    apply(path, templateSchema);
    disabled(path, () => this.saving());
    disabled(path.mode, () => !!this.template());
    hidden(path.questionCount, () => this.model().mode === 'static');
    min(path.questionCount, () => this.countBounds().min, {
      message: 'מספר השאלות קטן מהטווח המותר בתבנית.',
    });
    max(path.questionCount, () => this.countBounds().max, {
      message: 'מספר השאלות גדול מהטווח המותר בתבנית.',
    });
  });

  protected addPassage() {
    if (this.saving() || this.model().passages.length >= 4) return;
    const passage = { id: crypto.randomUUID(), text: '' };
    this.model.update((draft) => ({ ...draft, passages: [...draft.passages, passage] }));
    this.focusAfterRender(passage.id);
  }

  protected removePassage(index: number) {
    if (this.saving()) return;
    this.model.update((draft) => ({
      ...draft,
      passages: draft.passages.filter((_, position) => position !== index),
    }));
    this.focusAfterRender(this.model().passages[index]?.id ?? 'add-passage');
  }

  protected addQuestion() {
    if (this.saving() || this.model().questions.length >= 20) return;
    const question = blankQuestion();
    this.model.update((draft) => ({ ...draft, questions: [...draft.questions, question] }));
    this.focusAfterRender(question.id + '-prompt');
  }

  protected removeQuestion(index: number) {
    if (this.saving() || this.model().questions.length <= 1) return;
    this.model.update((draft) => ({
      ...draft,
      questions: draft.questions.filter((_, position) => position !== index),
    }));
    const next = this.model().questions[Math.min(index, this.model().questions.length - 1)];
    this.focusAfterRender(next.id + '-prompt');
  }

  protected moveQuestion(index: number, direction: number) {
    const destination = index + direction;
    if (this.saving() || destination < 0 || destination >= this.model().questions.length) return;
    this.model.update((draft) => {
      const questions = [...draft.questions];
      [questions[index], questions[destination]] = [questions[destination], questions[index]];
      return { ...draft, questions };
    });
    this.focusAfterRender(this.model().questions[destination].id + '-prompt');
  }

  protected async save(event: Event) {
    event.preventDefault();
    if (this.saving()) return;
    this.attempted.set(true);
    this.error.set('');
    await submit(this.fields, async () => {
      const previous = this.template();
      const definition = templateDefinition(this.model(), previous?.definition);
      this.saving.set(true);
      try {
        const saved = previous
          ? await this.api.publishTemplate(previous.id, previous.currentVersion, definition)
          : await this.api.createTemplate(definition);
        this.saved.emit(saved);
      } catch (error) {
        this.conflict.set(error instanceof HttpErrorResponse && error.status === 409);
        this.error.set(apiError(error));
      } finally {
        this.saving.set(false);
      }
    });
    if (this.fields().invalid()) this.focusAfterRender('template-errors');
  }

  private focusAfterRender(id: string) {
    afterNextRender(() => this.document.getElementById(id)?.focus(), { injector: this.injector });
  }
}
