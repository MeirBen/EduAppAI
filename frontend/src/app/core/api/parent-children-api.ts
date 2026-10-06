import { HttpClient, httpResource } from '@angular/common/http';
import { DestroyRef, inject, Injectable } from '@angular/core';
import {
  ChildActivation,
  ChildDevice,
  ChildProfileDetails,
  ChildSummary,
  Page,
} from './assignment-models';
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
  create(profile: { name: string; details: ChildProfileDetails }, lifetime: DestroyRef) {
    return requestResult(this.http.post<ChildSummary>('/api/children', profile), lifetime);
  }
  update(
    child: ChildSummary,
    profile: { name: string; enabled: boolean; details: ChildProfileDetails },
    lifetime: DestroyRef,
  ) {
    return requestResult(
      this.http.put<ChildSummary>(`/api/children/${child.id}`, {
        ...profile,
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
  delete(child: ChildSummary, lifetime: DestroyRef) {
    return requestResult(
      this.http.delete<void>(`/api/children/${child.id}`, {
        params: { expectedRevision: child.revision },
      }),
      lifetime,
    );
  }
  revoke(childId: string, grantId: string, lifetime: DestroyRef) {
    return requestResult(
      this.http.delete<void>(`/api/children/${childId}/devices/${grantId}`),
      lifetime,
    );
  }
  removeDevice(childId: string, grantId: string, lifetime: DestroyRef) {
    return requestResult(
      this.http.delete<void>(`/api/children/${childId}/devices/${grantId}/record`),
      lifetime,
    );
  }
}
