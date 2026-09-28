import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';

/** Displays a saved task and parent-only answers without regeneration. */
@Component({
  selector: 'app-instance-preview',
  imports: [RouterLink, DatePipe],
  templateUrl: './instance-preview.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InstancePreviewPage {
  /** Bound from the route; changing it reloads the corresponding saved snapshot. */
  readonly instanceId = input.required<string>();
  private readonly api = inject(LearningApi);
  protected readonly instance = this.api.instance(this.instanceId);
  protected readonly apiError = apiError;
}
