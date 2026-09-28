import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  inject,
  Injector,
  input,
  linkedSignal,
  output,
  signal,
} from '@angular/core';
import { disabled, form, FormField, submit, validate } from '@angular/forms/signals';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import { TemplateDefinition, TemplateDetail } from '../../../core/api/models';
import {
  aiTemplateDefinition,
  aiTemplateDraft,
  aiTemplateErrors,
  blankParameter,
} from './ai-template-draft';

/** Reviews AI instructions and dynamic fields before explicit publication; failures retain local edits. */
@Component({
  selector: 'app-ai-template-form',
  imports: [FormField],
  templateUrl: './ai-template-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AiTemplateForm {
  readonly definition = input.required<TemplateDefinition>();
  /** Present only when publishing a new revision; its version guards against concurrent edits. */
  readonly existing = input<TemplateDetail>();
  readonly busy = input(false);
  readonly saved = output<TemplateDetail>();
  readonly savingChanged = output<boolean>();
  readonly reloadRequested = output<void>();
  private readonly api = inject(LearningApi);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  protected readonly model = linkedSignal(() => aiTemplateDraft(this.definition()));
  protected readonly saving = signal(false);
  protected readonly attempted = signal(false);
  protected readonly error = signal('');
  protected readonly conflict = signal(false);
  protected readonly fields = form(this.model, (path) => {
    disabled(path, () => this.saving() || this.busy());
    validate(path, ({ value }) =>
      aiTemplateErrors(value()).map((message, index) => ({ kind: `blueprint-${index}`, message })),
    );
  });

  protected addParameter() {
    if (this.saving() || this.busy() || this.model().parameters.length >= 16) return;
    const field = blankParameter();
    this.model.update((draft) => ({ ...draft, parameters: [...draft.parameters, field] }));
    this.focus(field.id + '-label');
  }

  protected removeParameter(index: number) {
    if (this.saving() || this.busy()) return;
    this.model.update((draft) => ({
      ...draft,
      parameters: draft.parameters.filter((_, position) => position !== index),
    }));
    this.focus('add-parameter');
  }

  protected async save(event: Event) {
    event.preventDefault();
    if (this.saving() || this.busy()) return;
    this.attempted.set(true);
    this.error.set('');
    await submit(this.fields, async () => {
      const definition = aiTemplateDefinition(this.model());
      const previous = this.existing();
      this.saving.set(true);
      this.savingChanged.emit(true);
      try {
        const result = previous
          ? await this.api.publishTemplate(previous.id, previous.currentVersion, definition)
          : await this.api.createTemplate(definition);
        this.saved.emit(result);
      } catch (error) {
        this.error.set(apiError(error));
        this.conflict.set(error instanceof HttpErrorResponse && error.status === 409);
      } finally {
        this.saving.set(false);
        this.savingChanged.emit(false);
      }
    });
    if (this.fields().invalid() || this.error()) this.focus('blueprint-errors');
  }

  private focus(id: string) {
    afterNextRender(() => this.document.getElementById(id)?.focus(), { injector: this.injector });
  }
}
