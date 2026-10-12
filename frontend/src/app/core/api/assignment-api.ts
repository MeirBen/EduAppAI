import { HttpClient, httpResource } from '@angular/common/http';
import { DestroyRef, inject, Injectable } from '@angular/core';
import {
  AssignmentCounts,
  AssignmentDetail,
  AssignmentSummary,
  AssignmentView,
  Page,
  ParentAssignmentResult,
  ParentGrade,
} from './assignment-models';
import { requestResult } from './request-result';

/** Parent-only assignment reads and writes. Cancellation cannot guarantee rollback; never retry implicitly. */
@Injectable({ providedIn: 'root' })
export class AssignmentApi {
  private readonly http = inject(HttpClient);

  list(filter: () => { page: number; childId: string; status: AssignmentView }) {
    return httpResource<Page<AssignmentSummary>>(() => {
      const { page, childId, status } = filter();
      return {
        url: '/api/assignments',
        params: { page, status, ...(childId ? { childId } : {}) },
      };
    });
  }
  /** Exact sizes of the list's views, for one child or the whole family. */
  counts(childId: () => string) {
    return httpResource<AssignmentCounts>(() => {
      const id = childId();
      return { url: '/api/assignments/counts', params: { ...(id ? { childId: id } : {}) } };
    });
  }
  /** Every child's current work on one activity. */
  forSnapshot(snapshotId: () => string) {
    return httpResource<Page<AssignmentSummary>>(() => ({
      url: '/api/assignments',
      params: { snapshotId: snapshotId(), pageSize: 100 },
    }));
  }
  detail(id: () => string) {
    return httpResource<AssignmentDetail>(() => `/api/assignments/${id()}`);
  }
  result(id: () => string | undefined) {
    return httpResource<ParentAssignmentResult>(() =>
      id() ? `/api/assignments/${id()}/result` : undefined,
    );
  }
  readResult(id: string, lifetime: DestroyRef) {
    return requestResult(
      this.http.get<ParentAssignmentResult>(`/api/assignments/${id}/result`),
      lifetime,
    );
  }
  create(childId: string, snapshotId: string, lifetime: DestroyRef) {
    return requestResult(
      this.http.post<AssignmentSummary>(
        '/api/assignments',
        { childId, snapshotId },
        { observe: 'response' },
      ),
      lifetime,
    );
  }
  /** Withdraws available work or restores withdrawn work, at the revision the parent saw. */
  change(assignment: AssignmentSummary, action: 'withdraw' | 'restore', lifetime: DestroyRef) {
    return requestResult(
      this.http.post<AssignmentSummary>(`/api/assignments/${assignment.id}/${action}`, {
        expectedRevision: assignment.revision,
      }),
      lifetime,
    );
  }
  review(id: string, expectedRevision: number, grades: ParentGrade[], lifetime: DestroyRef) {
    return requestResult(
      this.http.post<ParentAssignmentResult>(`/api/assignments/${id}/review`, {
        expectedRevision,
        grades,
      }),
      lifetime,
    );
  }
}
