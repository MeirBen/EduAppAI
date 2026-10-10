import { expect, test } from '@playwright/test';
import { numericPlan } from '../src/app/features/activities/learning-plan.fixture';

test('Kestrel accepts bounded Hebrew activity chat and rejects oversized bodies', async ({
  request,
}) => {
  let csrf = await (await request.get('/api/auth/csrf')).json();
  const login = await request.post('/api/auth/login', {
    headers: { 'X-XSRF-TOKEN': csrf.token },
    data: { email: 'cleanup@example.test', password: 'TestOnly!Parent12345' },
  });
  expect(login.status()).toBe(204);
  csrf = await (await request.get('/api/auth/csrf')).json();
  const headers = { 'X-XSRF-TOKEN': csrf.token, 'Content-Type': 'application/json' };
  const plan = numericPlan;
  const body = {
    plan,
    chat: Array.from({ length: 100 }, (_, index) => ({
      role: index % 2 ? 'assistant' : 'parent',
      text: 'א'.repeat(index % 2 ? 1000 : 4000),
      atUtc: '2026-10-01T00:00:00Z',
      target: null,
      assumptions: null,
    })),
  };
  expect(Buffer.byteLength(JSON.stringify(body), 'utf8')).toBeGreaterThan(256 * 1024);
  for (const escaped of [false, true]) {
    const json = JSON.stringify({ id: crypto.randomUUID(), ...body });
    const data = escaped
      ? json.replace(
          /[^\x00-\x7f]/g,
          (character) => `\\u${character.charCodeAt(0).toString(16).padStart(4, '0')}`,
        )
      : json;
    const created = await request.post('/api/activity-drafts', { headers, data });
    expect(created.status(), await created.text()).toBe(201);
    const draft = await created.json();
    try {
      expect(draft.chat).toHaveLength(100);
      expect(draft.plan).toEqual(plan);
    } finally {
      expect((await request.delete(`/api/activity-drafts/${draft.id}`, { headers })).status()).toBe(
        204,
      );
    }
  }
  const oversized = await request.post('/api/activity-drafts', {
    headers,
    data: JSON.stringify({ id: crypto.randomUUID(), plan }) + ' '.repeat(3 * 1024 * 1024),
  });
  expect(oversized.status()).toBe(413);
});
