import { expect, type APIRequestContext } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import type { ActivityDetail } from '../src/app/core/api/models';

/** Completes disposable test content using the suite's isolated provider and real worker. */
export async function generateDraft(
  parent: APIRequestContext,
  headers: Record<string, string>,
  draft: ActivityDetail,
): Promise<ActivityDetail> {
  const path = `/api/activity-drafts/${draft.id}`;
  const started = await parent.post(path + '/operations', {
    headers,
    data: { operationKey: randomUUID(), expectedRevision: draft.revision, kind: 'Create' },
  });
  expect(started.status(), await started.text()).toBe(202);
  const operation = await started.json();
  await expect
    .poll(
      async () => (await (await parent.get(path + '/operations/' + operation.id)).json()).status,
      { timeout: 20_000 },
    )
    .toBe('completed');
  return (await parent.get(path)).json();
}
