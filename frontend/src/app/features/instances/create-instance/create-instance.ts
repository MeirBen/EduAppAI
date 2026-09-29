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
import { CreateInstanceRequest } from '../../../core/api/models';
import { InstanceForm } from '../instance-form/instance-form';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';

/** Collects choices, requests an AI task and opens its saved preview. */
@Component({
  selector: 'app-create-instance',
  imports: [LoadingIndicator, RouterLink, InstanceForm],
  templateUrl: './create-instance.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateInstance {
  readonly templateId = input.required<string>();
  private readonly api = inject(LearningApi);
  private readonly router = inject(Router);
  private readonly lifetime = inject(DestroyRef);
  protected readonly template = this.api.template(this.templateId);
  protected readonly savedInstanceId = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly apiError = apiError;

  protected async generate(request: CreateInstanceRequest) {
    if (this.busy() || this.savedInstanceId()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const instance = await this.api.createInstance(this.templateId(), request, this.lifetime);
      if (this.lifetime.destroyed) return;
      // A failed preview navigation must not turn a confirmed save into another AI request.
      this.savedInstanceId.set(instance.id);
      await this.router.navigate(['/instances', instance.id]).catch(() => false);
    } catch (error) {
      if (!this.lifetime.destroyed) this.error.set(apiError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
