import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import { ParameterValues } from '../../../core/api/models';
import { ParameterForm } from '../parameter-form/parameter-form';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';

/** Collects choices, requests an AI task and opens its saved preview. */
@Component({
  selector: 'app-create-instance',
  imports: [LoadingIndicator, RouterLink, ParameterForm],
  templateUrl: './create-instance.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateInstance {
  /** Bound from the route by withComponentInputBinding. */
  readonly templateId = input.required<string>();
  private readonly api = inject(LearningApi);
  private readonly router = inject(Router);
  private readonly lifetime = inject(DestroyRef);
  protected readonly template = this.api.template(this.templateId);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly apiError = apiError;

  protected async generate(parameters: ParameterValues) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const instance = await this.api.createInstance(this.templateId(), parameters, this.lifetime);
      if (!this.lifetime.destroyed) await this.router.navigate(['/instances', instance.id]);
    } catch (error) {
      if (!this.lifetime.destroyed) this.error.set(apiError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
