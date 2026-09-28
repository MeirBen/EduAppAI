import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  InstancePreview,
  InstanceSummary,
  ParameterValues,
  TemplateDefinition,
  TemplateDetail,
  TemplateSummary,
} from './models';

@Injectable({ providedIn: 'root' })
export class LearningApi {
  private readonly http = inject(HttpClient);

  listTemplates() {
    return firstValueFrom(this.http.get<TemplateSummary[]>('/api/templates'));
  }
  getTemplate(id: string) {
    return firstValueFrom(this.http.get<TemplateDetail>(`/api/templates/${id}`));
  }
  createTemplate(definition: TemplateDefinition) {
    return firstValueFrom(this.http.post<TemplateDetail>('/api/templates', definition));
  }
  listInstances() {
    return firstValueFrom(this.http.get<InstanceSummary[]>('/api/instances'));
  }
  getInstance(id: string) {
    return firstValueFrom(this.http.get<InstancePreview>(`/api/instances/${id}`));
  }
  createInstance(templateId: string, parameters: ParameterValues) {
    return firstValueFrom(
      this.http.post<InstancePreview>(`/api/templates/${templateId}/instances`, { parameters }),
    );
  }
}
