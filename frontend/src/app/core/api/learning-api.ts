import { HttpClient, httpResource } from '@angular/common/http';
import { DestroyRef, inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import {
  AiTemplateDraft,
  InstancePreview,
  InstanceSummary,
  ParameterValues,
  TemplateDefinition,
  TemplateDetail,
  TemplateSummary,
} from './models';

/**
 * Parent HTTP contracts. Create read resources in the caller's injection context so
 * route changes and destruction cancel reads. Commands reject on HTTP failures;
 * cancelling a write does not guarantee server rollback. No automatic retries.
 * HttpClient manages same-origin authentication and XSRF cookies.
 */
@Injectable({ providedIn: 'root' })
export class LearningApi {
  private readonly http = inject(HttpClient);

  /** Reports server configuration without exposing credentials or contacting the provider. */
  aiStatus() {
    return httpResource<{ configured: boolean }>(() => '/api/ai/status');
  }
  /** Produces an unsaved proposal; leaving the caller cancels HTTP. Never retry automatically. */
  authorTemplate(prompt: string, lifetime: DestroyRef) {
    return firstValueFrom(
      this.http
        .post<AiTemplateDraft>('/api/ai/template-drafts', { prompt })
        .pipe(takeUntilDestroyed(lifetime)),
    );
  }

  /** Returns up to 100 of the family's most recently created templates. */
  templates() {
    return httpResource<TemplateSummary[]>(() => '/api/templates');
  }
  /** Loads the current published definition; missing and foreign IDs both return HTTP 404. */
  template(id: () => string | undefined) {
    return httpResource<TemplateDetail>(() => {
      const value = id();
      return value ? `/api/templates/${value}` : undefined;
    });
  }
  /** Permanently deletes the family's template, all revisions and their saved tasks. */
  deleteTemplate(id: string, lifetime: DestroyRef) {
    return firstValueFrom(
      this.http.delete<void>(`/api/templates/${id}`).pipe(takeUntilDestroyed(lifetime)),
    );
  }
  /** Clears all family learning content, including items beyond list limits; keeps accounts and AI settings. */
  resetLibrary(lifetime: DestroyRef) {
    return firstValueFrom(
      this.http.delete<void>('/api/templates').pipe(takeUntilDestroyed(lifetime)),
    );
  }
  /** Creates the first immutable version; leaving the editor cancels the pending request. */
  createTemplate(definition: TemplateDefinition, lifetime: DestroyRef) {
    return firstValueFrom(
      this.http
        .post<TemplateDetail>('/api/templates', definition)
        .pipe(takeUntilDestroyed(lifetime)),
    );
  }
  /** Publishes a new immutable revision; HTTP 409 leaves the caller's stale draft unsaved. */
  publishTemplate(
    id: string,
    expectedVersion: number,
    definition: TemplateDefinition,
    lifetime: DestroyRef,
  ) {
    return firstValueFrom(
      this.http
        .post<TemplateDetail>(`/api/templates/${id}/versions`, { expectedVersion, definition })
        .pipe(takeUntilDestroyed(lifetime)),
    );
  }
  /** Returns up to 100 of the family's most recently created drafts, without question content. */
  instances() {
    return httpResource<InstanceSummary[]>(() => '/api/instances');
  }
  /** Reads frozen content, including parent-only answer keys; it never generates new questions. */
  instance(id: () => string) {
    return httpResource<InstancePreview>(() => `/api/instances/${id()}`);
  }
  /** Permanently deletes one family-owned task; its template and sibling tasks remain. */
  deleteInstance(id: string, lifetime: DestroyRef) {
    return firstValueFrom(
      this.http.delete<void>(`/api/instances/${id}`).pipe(takeUntilDestroyed(lifetime)),
    );
  }
  /**
   * Generates and saves an AI task from the current template revision.
   * @param templateId - The server selects and pins this template's current revision.
   * @param parameters - Submitted values; an empty object accepts the template's defaults.
   * @param lifetime - Cancels the pending request when the owning page is destroyed.
   * @returns The saved preview. Each success creates a task; never retry automatically.
   */
  createInstance(templateId: string, parameters: ParameterValues, lifetime: DestroyRef) {
    return firstValueFrom(
      this.http
        .post<InstancePreview>(`/api/templates/${templateId}/instances`, { parameters })
        .pipe(takeUntilDestroyed(lifetime)),
    );
  }
}
