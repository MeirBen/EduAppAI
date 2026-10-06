import { HttpClient, httpResource } from '@angular/common/http';
import { DestroyRef, inject, Injectable } from '@angular/core';
import { ChildActivation, ChildDevice, ChildSummary, Page } from './assignment-models';
import { requestResult } from './request-result';

/** Parent-owned profile/device requests; resources and writes cancel with their caller, without retry. */
@Injectable({ providedIn: 'root' })
export class ParentChildrenApi {
  private readonly http = inject(HttpClient);

  children(page: () => number) {
    return httpResource<Page<ChildSummary>>(() => ({
      url: '/api/children',
      params: { page: page() },
    }));
  }
  devices(childId: () => string | undefined, page: () => number) {
    return httpResource<Page<ChildDevice>>(() =>
      childId()
        ? {
            url: `/api/children/${childId()}/devices`,
            params: { page: page() },
          }
        : undefined,
    );
  }
  create(name: string, lifetime: DestroyRef) {
    return requestResult(this.http.post<ChildSummary>('/api/children', { name }), lifetime);
  }
  update(child: ChildSummary, name: string, enabled: boolean, lifetime: DestroyRef) {
    return requestResult(
      this.http.put<ChildSummary>(`/api/children/${child.id}`, {
        name,
        enabled,
        expectedRevision: child.revision,
      }),
      lifetime,
    );
  }
  issue(childId: string, deviceLabel: string, lifetime: DestroyRef) {
    return requestResult(
      this.http.post<ChildActivation>(`/api/children/${childId}/activation`, { deviceLabel }),
      lifetime,
    );
  }
  revoke(childId: string, grantId: string, lifetime: DestroyRef) {
    return requestResult(
      this.http.delete<void>(`/api/children/${childId}/devices/${grantId}`),
      lifetime,
    );
  }
}
