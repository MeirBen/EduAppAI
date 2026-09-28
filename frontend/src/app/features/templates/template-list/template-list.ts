import { ChangeDetectionStrategy, Component, inject, resource } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';

@Component({
  selector: 'app-template-list',
  imports: [RouterLink, DatePipe],
  templateUrl: './template-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TemplateList {
  private readonly api = inject(LearningApi);
  protected readonly data = resource({
    loader: async () => {
      const [templates, instances] = await Promise.all([
        this.api.listTemplates(),
        this.api.listInstances(),
      ]);
      return { templates, instances };
    },
  });
  protected readonly apiError = apiError;
}
