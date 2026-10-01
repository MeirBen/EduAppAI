import { HttpClient, httpResource } from '@angular/common/http';
import { Observable, takeUntil } from 'rxjs';
import { DestroyRef, inject, Injectable } from '@angular/core';
import { requestResult } from './request-result';
import {
  TemplateSummary,
  LearningPlan,
  PlanAuthoringRequest,
  PlanAuthoringReply,
  PlanTemplateDetail,
  ActivityDetail,
  ActivitySummary,
  EditableActivity,
  ActivityInput,
  StartGeneration,
  GenerationOperation,
  SnapshotPreview,
  SnapshotSummary,
} from './models';

/**
 * Parent HTTP contracts. Create resources in the caller's injection context: reads
 * cancel when their URL changes or their owner is destroyed. Writes use the supplied
 * lifetime and reject on failure or cancellation; cancellation cannot guarantee rollback.
 * No automatic retries.
 * HttpClient manages same-origin authentication and XSRF cookies.
 */
@Injectable({ providedIn: 'root' })
export class LearningApi {
  private readonly http = inject(HttpClient);

  /** Reports server configuration without exposing credentials or contacting the provider. */
  aiStatus() {
    return httpResource<{ configured: boolean; schemaVersion: number }>(() => '/api/ai/status');
  }
  /** One cancellable proposal call; cancellation never causes a retry. */
  authorPlan(request: PlanAuthoringRequest, cancelled: Observable<void>, lifetime: DestroyRef) {
    return requestResult(
      this.http
        .post<PlanAuthoringReply>('/api/ai/template-drafts', request)
        .pipe(takeUntil(cancelled)),
      lifetime,
    );
  }
  /** Loads the current canonical template; missing and foreign IDs both return 404. */
  planTemplate(id: () => string | undefined) {
    return httpResource<PlanTemplateDetail>(() => (id() ? `/api/templates/${id()}` : undefined));
  }
  /** Loads the saved activity checkpoint without starting generation or saving local changes. */
  activity(id: () => string | undefined) {
    return httpResource<ActivityDetail>(() => (id() ? `/api/activity-drafts/${id()}` : undefined));
  }
  /** Reads one checkpoint for explicit refresh or operation reconciliation. */
  readActivity(id: string) {
    return this.http.get<ActivityDetail>(`/api/activity-drafts/${id}`);
  }
  activities() {
    return httpResource<ActivitySummary[]>(() => '/api/activity-drafts');
  }
  createActivity(
    plan: LearningPlan,
    input: ActivityInput,
    template: PlanTemplateDetail | undefined,
    lifetime: DestroyRef,
  ) {
    return requestResult(
      this.http.post<ActivityDetail>('/api/activity-drafts', {
        plan,
        input,
        ...(template ? { templateId: template.id, expectedVersion: template.currentVersion } : {}),
      }),
      lifetime,
    );
  }
  saveActivity(
    id: string,
    expectedRevision: number,
    plan: LearningPlan,
    input: ActivityInput,
    document: EditableActivity,
    lifetime: DestroyRef,
  ) {
    return requestResult(
      this.http.put<ActivityDetail>(`/api/activity-drafts/${id}`, {
        expectedRevision,
        plan,
        input,
        document,
      }),
      lifetime,
    );
  }
  adoptActivity(
    id: string,
    expectedRevision: number,
    materialIds: string[],
    questionIds: string[],
    lifetime: DestroyRef,
  ) {
    return requestResult(
      this.http.post<ActivityDetail>(`/api/activity-drafts/${id}/adopt-content`, {
        expectedRevision,
        materialIds,
        questionIds,
      }),
      lifetime,
    );
  }
  releaseActivity(id: string, expectedRevision: number, lifetime: DestroyRef) {
    return requestResult(
      this.http.post<SnapshotPreview>(`/api/activity-drafts/${id}/release`, { expectedRevision }),
      lifetime,
    );
  }
  deleteActivity(id: string, lifetime: DestroyRef) {
    return requestResult(this.http.delete<void>(`/api/activity-drafts/${id}`), lifetime);
  }
  startGeneration(id: string, request: StartGeneration, lifetime: DestroyRef) {
    return requestResult(
      this.http.post<GenerationOperation>(`/api/activity-drafts/${id}/operations`, request),
      lifetime,
    );
  }
  operation(id: string, operationId: string) {
    return this.http.get<GenerationOperation>(
      `/api/activity-drafts/${id}/operations/${operationId}`,
    );
  }
  cancelGeneration(id: string, operationId: string, lifetime: DestroyRef) {
    return requestResult(
      this.http.post<GenerationOperation>(
        `/api/activity-drafts/${id}/operations/${operationId}/cancel`,
        {},
      ),
      lifetime,
    );
  }
  snapshots() {
    return httpResource<SnapshotSummary[]>(() => '/api/instances');
  }
  snapshot(id: () => string) {
    return httpResource<SnapshotPreview>(() => `/api/instances/${id()}`);
  }
  /** Explicit copy only; never changes the immutable snapshot or starts AI. */
  copySnapshot(id: string, lifetime: DestroyRef) {
    return requestResult(
      this.http.post<ActivityDetail>('/api/activity-drafts', { snapshotId: id }),
      lifetime,
    );
  }
  /** Independent template publication; never writes an activity or starts generation. */
  savePlanTemplate(
    plan: LearningPlan,
    previous: PlanTemplateDetail | undefined,
    lifetime: DestroyRef,
  ) {
    return requestResult(
      previous
        ? this.http.post<PlanTemplateDetail>(`/api/templates/${previous.id}/versions`, {
            expectedVersion: previous.currentVersion,
            definition: plan,
          })
        : this.http.post<PlanTemplateDetail>('/api/templates', plan),
      lifetime,
    );
  }
  /** Returns up to 100 of the family's most recently created templates. */
  templates() {
    return httpResource<TemplateSummary[]>(() => '/api/templates');
  }
  /** Permanently deletes the family's template and its revisions. */
  deleteTemplate(id: string, lifetime: DestroyRef) {
    return requestResult(this.http.delete<void>(`/api/templates/${id}`), lifetime);
  }
  /** Clears all family learning content, including items beyond list limits; keeps accounts and AI settings. */
  resetLibrary(lifetime: DestroyRef) {
    return requestResult(this.http.delete<void>('/api/templates'), lifetime);
  }
  /** Permanently deletes one owned snapshot; independent drafts and templates remain. */
  deleteSnapshot(id: string, lifetime: DestroyRef) {
    return requestResult(this.http.delete<void>(`/api/instances/${id}`), lifetime);
  }
}
