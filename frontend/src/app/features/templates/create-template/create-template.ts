import { ChangeDetectionStrategy, Component, inject, input, resource } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import { TemplateDetail } from '../../../core/api/models';
import { AiTemplateForm } from '../ai-template-form/ai-template-form';
import { AiTemplateAuthor } from '../ai-template-author/ai-template-author';

/** Route container for new templates and explicit publication of an existing template revision. */
@Component({
  selector: 'app-create-template',
  imports: [AiTemplateForm, AiTemplateAuthor, RouterLink],
  templateUrl: './create-template.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateTemplate {
  private readonly api = inject(LearningApi);
  private readonly router = inject(Router);
  /** Absent on the create route; editing loads the current immutable definition. */
  readonly templateId = input<string>();
  protected readonly template = resource({
    params: this.templateId,
    loader: ({ params }) => this.api.getTemplate(params),
  });
  protected readonly apiError = apiError;

  protected async saved(template: TemplateDetail) {
    await this.router.navigate(['/templates', template.id, 'create']);
  }
}
