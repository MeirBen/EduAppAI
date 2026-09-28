import { DOCUMENT } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  Injector,
  output,
  resource,
  signal,
} from '@angular/core';
import { disabled, form, FormField, maxLength, submit, validate } from '@angular/forms/signals';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import { AiTemplateDraft, TemplateDetail } from '../../../core/api/models';
import { AiTemplateForm } from '../ai-template-form/ai-template-form';

/** AI proposes a template; the parent reviews and explicitly saves it. */
@Component({
  selector: 'app-ai-template-author',
  imports: [FormField, AiTemplateForm],
  templateUrl: './ai-template-author.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AiTemplateAuthor {
  readonly saved = output<TemplateDetail>();
  private readonly api = inject(LearningApi);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly lifetime = inject(DestroyRef);
  protected readonly status = resource({ loader: () => this.api.aiStatus() });
  protected readonly model = signal({ prompt: '' });
  protected readonly error = signal('');
  protected readonly publishing = signal(false);
  protected readonly draft = signal<AiTemplateDraft | undefined>(undefined);
  protected readonly fields = form(this.model, (path) => {
    disabled(path, ({ state }) => state.submitting() || this.publishing());
    maxLength(path.prompt, 4000, { message: 'אפשר לכתוב עד 4,000 תווים.' });
    validate(path.prompt, ({ value }) =>
      value().trim() ? undefined : { kind: 'required', message: 'כתבו מה תרצו ללמד.' },
    );
  });
  protected readonly busy = this.fields().submitting;

  protected async generate(event: Event) {
    event.preventDefault();
    if (this.busy() || this.publishing() || !this.status.value()?.configured) return;
    this.error.set('');
    await submit(this.fields, async () => {
      try {
        const proposal = await this.api.authorTemplate(this.model().prompt, this.lifetime);
        if (this.lifetime.destroyed) return;
        this.draft.set(proposal);
        afterNextRender(() => this.document.getElementById('blueprint-review')?.focus(), {
          injector: this.injector,
        });
      } catch (error) {
        if (!this.lifetime.destroyed) this.error.set(apiError(error));
      }
    });
  }
}
