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

/**
 * Same-origin client for the current parent's templates and drafts.
 * Promises reject with HTTP errors for callers to present through apiError.
 * Authentication and XSRF cookies are managed by HttpClient and the browser.
 */
@Injectable({ providedIn: 'root' })
export class LearningApi {
  private readonly http = inject(HttpClient);

  /** Returns up to 100 of the family's most recently created templates. */
  listTemplates() {
    return firstValueFrom(this.http.get<TemplateSummary[]>('/api/templates'));
  }
  /** Loads the current published definition; missing and foreign IDs both return HTTP 404. */
  getTemplate(id: string) {
    return firstValueFrom(this.http.get<TemplateDetail>(`/api/templates/${id}`));
  }
  /** Creates a stable template and its first immutable version after server validation. */
  createTemplate(definition: TemplateDefinition) {
    return firstValueFrom(this.http.post<TemplateDetail>('/api/templates', definition));
  }
  /** Publishes a new immutable revision; HTTP 409 leaves the caller's stale draft unsaved. */
  publishTemplate(id: string, expectedVersion: number, definition: TemplateDefinition) {
    return firstValueFrom(
      this.http.post<TemplateDetail>(`/api/templates/${id}/versions`, {
        expectedVersion,
        definition,
      }),
    );
  }
  /** Returns up to 100 of the family's most recently created drafts, without question content. */
  listInstances() {
    return firstValueFrom(this.http.get<InstanceSummary[]>('/api/instances'));
  }
  /** Reads frozen content, including parent-only answer keys; it never generates new questions. */
  getInstance(id: string) {
    return firstValueFrom(this.http.get<InstancePreview>(`/api/instances/${id}`));
  }
  /**
   * Creates a new persisted draft from the template's current version at request time.
   * @param templateId - The stable template ID; the server selects its current published revision.
   * @param parameters - Submitted values; an empty object accepts the template's defaults.
   * @returns The saved preview. Each successful call creates another draft; do not retry automatically.
   */
  createInstance(templateId: string, parameters: ParameterValues) {
    return firstValueFrom(
      this.http.post<InstancePreview>(`/api/templates/${templateId}/instances`, { parameters }),
    );
  }
}
