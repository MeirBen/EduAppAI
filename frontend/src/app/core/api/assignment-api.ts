import { HttpClient, httpResource } from '@angular/common/http';
import { DestroyRef, inject, Injectable } from '@angular/core';
import {
  AssignmentDetail,
  AssignmentStatus,
  AssignmentSummary,
  Page,
  ParentAssignmentResult,
  ParentGrade,
} from './assignment-models';
import { requestResult } from './request-result';

/** Parent-only assignment reads and writes. Cancellation cannot guarantee rollback; never retry implicitly. */
@Injectable({ providedIn: 'root' })
export class AssignmentApi {
  private readonly http = inject(HttpClient);

  list(filter: () => { page: number; childId: string; status: AssignmentStatus | '' }) {
    return httpResource<Page<AssignmentSummary>>(() => {
      const { page, childId, status } = filter();
      return {
        url: '/api/assignments',
        params: { page, ...(childId ? { childId } : {}), ...(status ? { status } : {}) },
      };
    });
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
  withdraw(assignment: AssignmentSummary, lifetime: DestroyRef) {
    return requestResult(
      this.http.post<AssignmentSummary>(`/api/assignments/${assignment.id}/withdraw`, {
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
