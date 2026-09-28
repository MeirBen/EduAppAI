import { ChangeDetectionStrategy, Component, inject, input, resource, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import { ParameterValues } from '../../../core/api/models';
import { ParameterForm } from '../../../dynamic-form/parameter-form/parameter-form';
import { mathRange } from '../../../core/locale/hebrew';

/** Collects parameters for the current template and navigates to the newly saved draft. */
@Component({
  selector: 'app-create-instance',
  imports: [RouterLink, ParameterForm],
  templateUrl: './create-instance.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateInstance {
  /** Bound from the route by withComponentInputBinding. */
  readonly templateId = input.required<string>();
  private readonly api = inject(LearningApi);
  private readonly router = inject(Router);
  protected readonly template = resource({
    params: this.templateId,
    loader: ({ params }) => this.api.getTemplate(params),
  });
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly apiError = apiError;
  protected readonly range = mathRange;

  protected async generate(parameters: ParameterValues) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const instance = await this.api.createInstance(this.templateId(), parameters);
      await this.router.navigate(['/instances', instance.id]);
    } catch (error) {
      this.error.set(apiError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
