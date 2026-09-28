import { HttpClient } from '@angular/common/http';
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
 * Parent API for AI proposals, templates and saved tasks. HTTP failures reject.
 * HttpClient and the browser manage same-origin authentication and XSRF cookies.
 */
@Injectable({ providedIn: 'root' })
export class LearningApi {
  private readonly http = inject(HttpClient);

  /** Reports server configuration without exposing credentials or contacting the provider. */
  aiStatus() {
    return firstValueFrom(this.http.get<{ configured: boolean }>('/api/ai/status'));
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
  listTemplates() {
    return firstValueFrom(this.http.get<TemplateSummary[]>('/api/templates'));
  }
  /** Loads the current published definition; missing and foreign IDs both return HTTP 404. */
  getTemplate(id: string) {
    return firstValueFrom(this.http.get<TemplateDetail>(`/api/templates/${id}`));
  }
  /** Permanently deletes the family's template, all revisions and their saved tasks. */
  deleteTemplate(id: string) {
    return firstValueFrom(this.http.delete<void>(`/api/templates/${id}`));
  }
  /** Clears all family learning content, including items beyond list limits; keeps accounts and AI settings. */
  resetLibrary() {
    return firstValueFrom(this.http.delete<void>('/api/templates'));
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
  listInstances() {
    return firstValueFrom(this.http.get<InstanceSummary[]>('/api/instances'));
  }
  /** Reads frozen content, including parent-only answer keys; it never generates new questions. */
  getInstance(id: string) {
    return firstValueFrom(this.http.get<InstancePreview>(`/api/instances/${id}`));
  }
  /** Permanently deletes one family-owned task; its template and sibling tasks remain. */
  deleteInstance(id: string) {
    return firstValueFrom(this.http.delete<void>(`/api/instances/${id}`));
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
