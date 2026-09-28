import { ChangeDetectionStrategy, Component, inject, input, resource } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import { taskInstructions } from '../../../core/locale/hebrew';

/** Read-only parent preview of saved content, including answers; this is not the child player. */
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
  protected readonly instance = resource({
    params: this.instanceId,
    loader: ({ params }) => this.api.getInstance(params),
  });
  protected readonly apiError = apiError;
  protected readonly instructions = taskInstructions;
}
