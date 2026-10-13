import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { Observable, takeUntil } from 'rxjs';
import { DestroyRef, inject, Injectable } from '@angular/core';
import { requestResult } from './request-result';
import {
  LearningPlan,
  PlanAuthoringRequest,
  PlanAuthoringReply,
  ActivityDetail,
  ActivitySummary,
  ImportedChatTurn,
  LibraryCounts,
  EditableActivity,
  StartGeneration,
  GenerationOperation,
  SnapshotPreview,
  SnapshotSummary,
  SnapshotView,
} from './models';
import { Page } from './assignment-models';

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
    return httpResource<{ configured: boolean }>(() => '/api/ai/status');
  }
  /** One cancellable proposal call; cancellation never causes a retry. */
  authorPlan(request: PlanAuthoringRequest, cancelled: Observable<void>, lifetime: DestroyRef) {
    return requestResult(
      this.http
        .post<PlanAuthoringReply>('/api/ai/activity-plans', request)
        .pipe(takeUntil(cancelled)),
      lifetime,
    );
  }
  /** Loads the saved activity checkpoint without starting generation or saving local changes. */
  activity(id: () => string | undefined) {
    return httpResource<ActivityDetail>(() => (id() ? `/api/activity-drafts/${id()}` : undefined));
  }
  /** Reads one checkpoint for explicit refresh or operation reconciliation. */
  readActivity(id: string) {
    return this.http.get<ActivityDetail>(`/api/activity-drafts/${id}`);
  }
  activities(page: () => number) {
    return httpResource<Page<ActivitySummary>>(() => ({
      url: '/api/activity-drafts',
      params: { page: page() },
    }));
  }
  /** `id` names the new draft, so a retry after a lost response replays it instead of adding another. */
  createActivity(id: string, plan: LearningPlan, lifetime: DestroyRef, chat?: ImportedChatTurn[]) {
    return requestResult(
      this.http.post<ActivityDetail>('/api/activity-drafts', {
        id,
        plan,
        ...(chat?.length ? { chat } : {}),
      }),
      lifetime,
    );
  }
  saveActivity(
    id: string,
    expectedRevision: number,
    plan: LearningPlan,
    document: EditableActivity,
    lifetime: DestroyRef,
    sourceReplacements?: { id: string; text: string }[],
  ) {
    return requestResult(
      this.http.put<ActivityDetail>(`/api/activity-drafts/${id}`, {
        expectedRevision,
        plan,
        document,
        ...(sourceReplacements?.length ? { sourceReplacements } : {}),
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
  undoActivity(id: string, expectedRevision: number, lifetime: DestroyRef) {
    return requestResult(
      this.http.post<ActivityDetail>(`/api/activity-drafts/${id}/undo`, { expectedRevision }),
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
  snapshots(filter: () => { page: number; status: SnapshotView }) {
    return httpResource<Page<SnapshotSummary>>(() => ({ url: '/api/instances', params: filter() }));
  }
  libraryCounts() {
    return httpResource<LibraryCounts>(() => '/api/library/counts');
  }
  archivedSnapshots(page: () => number) {
    return httpResource<Page<SnapshotSummary>>(() => ({
      url: '/api/instances/archive',
      params: { page: page() },
    }));
  }
  /** The first messages that began the latest approved activities, read only while `wanted`. */
  firstMessages(wanted: () => boolean) {
    return httpResource<string[]>(() => (wanted() ? '/api/instances/first-messages' : undefined));
  }
  snapshot(id: () => string) {
    return httpResource<SnapshotPreview>(() => `/api/instances/${id()}`);
  }
  /** Copies without changing the snapshot or starting AI; `checkSaved` recovers a lost response even if the source was removed. */
  async copySnapshot(
    snapshotId: string,
    draftId: string,
    lifetime: DestroyRef,
    checkSaved = false,
  ) {
    if (checkSaved) {
      try {
        return await requestResult(this.readActivity(draftId), lifetime);
      } catch (error) {
        if (!(error instanceof HttpErrorResponse && error.status === 404)) throw error;
      }
    }
    return requestResult(
      this.http.post<ActivityDetail>('/api/activity-drafts', { id: draftId, snapshotId }),
      lifetime,
    );
  }
  /**
   * Deletes every activity with the work assigned in it, or all children's data with the archive kept for that work.
   * Parent accounts and AI settings always stay.
   */
  deleteData(kind: 'activities' | 'children', lifetime: DestroyRef) {
    return requestResult(this.http.delete<void>(`/api/learning-data/${kind}`), lifetime);
  }
  /** Removes a snapshot from the library: archives assigned content, deletes unassigned content; other items remain. */
  deleteSnapshot(id: string, lifetime: DestroyRef) {
    return requestResult(this.http.delete<void>(`/api/instances/${id}`), lifetime);
  }
  /** Permanently deletes an archived snapshot with its assignments, answers and grades. */
  deleteArchivedSnapshot(id: string, lifetime: DestroyRef) {
    return requestResult(this.http.delete<void>(`/api/instances/archive/${id}`), lifetime);
  }
}
