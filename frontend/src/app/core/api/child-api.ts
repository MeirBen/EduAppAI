import { HttpClient, httpResource } from '@angular/common/http';
import { DestroyRef, inject, Injectable } from '@angular/core';
import { LearnerAnswer, LearnerAssignment, LearnerInbox, LearnerSession } from './child-models';
import { requestResult } from './request-result';

/** Learner endpoints only. Every write is explicit and lifetime-bound, with no automatic retry. */
@Injectable({ providedIn: 'root' })
export class ChildApi {
  private readonly http = inject(HttpClient);
  inbox(filter: () => { state: 'available' | 'submitted'; page: number }) {
    return httpResource<LearnerInbox>(() => ({ url: '/api/child/assignments', params: filter() }));
  }
  assignment(id: () => string) {
    return httpResource<LearnerAssignment>(() => `/api/child/assignments/${id()}`);
  }
  start(id: string, lifetime: DestroyRef) {
    return requestResult(
      this.http.post<LearnerSession>(`/api/child/assignments/${id}/session`, {}),
      lifetime,
    );
  }
  readSession(id: string, lifetime: DestroyRef) {
    return requestResult(
      this.http.get<LearnerSession>(`/api/child/assignments/${id}/session`),
      lifetime,
    );
  }
  save(id: string, expectedRevision: number, answers: LearnerAnswer[], lifetime: DestroyRef) {
    return requestResult(
      this.http.put<LearnerSession>(`/api/child/assignments/${id}/session`, {
        expectedRevision,
        answers,
      }),
      lifetime,
    );
  }
  submit(id: string, expectedRevision: number, answers: LearnerAnswer[], lifetime: DestroyRef) {
    return requestResult(
      this.http.post<LearnerSession>(`/api/child/assignments/${id}/session/submit`, {
        expectedRevision,
        answers,
      }),
      lifetime,
    );
  }
}
