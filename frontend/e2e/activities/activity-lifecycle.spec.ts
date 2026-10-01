import { expect, test, Page } from '@playwright/test';
import { numericPlan } from '../../src/app/features/activities/learning-plan.fixture';
import {
  ActivityDetail,
  GenerationOperation,
  SnapshotPreview,
} from '../../src/app/core/api/models';

async function isolate(page: Page) {
  let draft: ActivityDetail = {
    id: 'draft',
    revision: 1,
    plan: numericPlan,
    input: { settings: numericPlan.defaults },
    document: { title: 'תרגול', instructions: null, materials: [], questions: [] },
    diagnostics: { questions: ['נדרשות שתי שאלות'] },
    measurements: [],
    activeOperationId: null,
    templateVersionId: null,
    releasedSnapshotId: null,
    releasedSourceRevision: null,
    createdAtUtc: '2026-10-01T00:00:00Z',
    updatedAtUtc: '2026-10-01T00:00:00Z',
  };
  let operation: GenerationOperation | undefined, snapshot: SnapshotPreview | undefined;
  const writes: { path: string; body: Record<string, unknown> }[] = [];
  await page.route('**/api/**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname,
      method = request.method();
    if (method === 'POST' || method === 'PUT') writes.push({ path, body: request.postDataJSON() });
    if (path === '/api/auth/me')
      return route.fulfill({ json: { email: 'parent@example.test', familyId: 'family' } });
    if (path === '/api/auth/csrf') return route.fulfill({ json: { token: 'isolated' } });
    if (path === '/api/ai/status')
      return route.fulfill({ json: { configured: true, schemaVersion: 1 } });
    if (path === '/api/templates/example')
      return route.fulfill({
        json: { id: 'example', currentVersion: 3, versionId: 'v3', definition: numericPlan },
      });
    if (path === '/api/activity-drafts' && method === 'POST') {
      const body = request.postDataJSON();
      draft = { ...draft, id: body.snapshotId ? 'copy' : 'draft', releasedSnapshotId: null };
      return route.fulfill({ status: 201, json: draft });
    }
    if (path === '/api/activity-drafts/draft' && method === 'PUT') {
      const body = request.postDataJSON();
      if (body.expectedRevision !== draft.revision)
        return route.fulfill({ status: 409, json: { title: 'התנגשות' } });
      draft = { ...draft, ...body, revision: draft.revision + 1, diagnostics: {} };
      return route.fulfill({ json: draft });
    }
    if (path === '/api/activity-drafts/draft' && method === 'GET')
      return route.fulfill({ json: draft });
    if (path === '/api/activity-drafts/draft/operations' && method === 'POST') {
      const body = request.postDataJSON();
      expect(body.expectedRevision).toBe(draft.revision);
      operation = {
        id: 'op',
        draftId: 'draft',
        kind: body.kind,
        status: 'queued',
        stage: 'questions',
        originalRevision: draft.revision,
        expectedRevision: draft.revision,
        failure: null,
        diagnosticsExpired: false,
        steps: [],
        artifacts: null,
      };
      draft = { ...draft, activeOperationId: 'op' };
      return route.fulfill({ status: 202, json: operation });
    }
    if (path === '/api/activity-drafts/draft/operations/op')
      return route.fulfill({ json: operation });
    if (path === '/api/activity-drafts/draft/release') {
      expect(request.postDataJSON()).toEqual({ expectedRevision: draft.revision });
      snapshot = {
        id: 'ready',
        sourceDraftId: draft.id,
        sourceDraftRevision: draft.revision,
        plan: draft.plan,
        input: draft.input,
        document: draft.document,
        reviewedAtUtc: '2026-10-01T00:00:00Z',
        measurements: [],
      };
      draft = { ...draft, releasedSnapshotId: 'ready' };
      return route.fulfill({ status: 201, json: snapshot });
    }
    if (path === '/api/instances/ready') return route.fulfill({ json: snapshot });
    throw new Error(`Unexpected isolated request ${method} ${path}`);
  });
  return {
    writes,
    complete() {
      if (!operation) throw new Error('No operation started');
      operation = {
        ...operation,
        status: 'completed',
        steps: [{ stage: 'questions', outcome: 'applied', usage: null, metadata: null }],
      };
      draft = {
        ...draft,
        revision: draft.revision + 1,
        activeOperationId: null,
        diagnostics: {},
        document: {
          title: 'תרגול חדש',
          instructions: 'ענו על השאלות — Answer carefully.',
          materials: [],
          questions: [1, 2].map((i) => ({
            id: 'q' + i,
            prompt: `כמה הם ${i}+1?`,
            interaction: { type: 'numeric-input' as const, options: null },
            answer: { value: String(i + 1) },
            points: 1,
            origin: { kind: 'generated' },
            acceptance: null,
          })),
        },
      };
    },
  };
}

test('saves before generation, edits manually, reviews the current revision and opens an immutable copy', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/templates/example/create');
  await page.locator('#generate-activity').click();
  await expect(page.getByText('ממתינה לביצוע', { exact: false })).toBeVisible();
  expect(state.writes.map((w) => w.path)).toEqual([
    '/api/activity-drafts',
    '/api/activity-drafts/draft/operations',
  ]);
  await expect(page).toHaveURL(/\/activities\/draft\?operation=op$/);
  state.complete();
  await expect(page.locator('#document-title')).toHaveValue('תרגול חדש');
  await page.locator('#question-0-answer').fill('9');
  await page.locator('#save-activity').click();
  await expect(page.getByText('הטיוטה שמורה', { exact: true })).toBeVisible();
  expect(state.writes.filter((w) => w.path.endsWith('/operations'))).toHaveLength(1);
  await page.locator('#release-activity').click();
  await page.getByRole('link', { name: 'לתצוגה המוכנה' }).click();
  await expect(page.getByRole('heading', { name: 'פעילות מוכנה — תצוגה להורים' })).toBeVisible();
  await expect(page.locator('textarea')).toHaveCount(0);
  await page.locator('summary').first().click();
  await expect(page.getByText('9', { exact: true })).toBeVisible();
  await page.locator('#copy-snapshot').click();
  await expect(page.getByRole('link', { name: 'פתיחת הטיוטה החדשה' })).toHaveAttribute(
    'href',
    '/activities/copy',
  );
  expect(state.writes.at(-1)?.body).toEqual({ snapshotId: 'ready' });
});

test('keeps local typing on operation completion, exposes server content and preserves it on stale save', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/templates/example/create');
  await page.locator('#generate-activity').click();
  await expect(page.locator('#cancel-generation')).toBeVisible();
  await page.locator('#document-title').fill('עריכה מקומית — Local');
  state.complete();
  await expect(page.getByText('יש תוצאה חדשה בשרת.', { exact: false })).toBeVisible();
  await expect(page.locator('#document-title')).toHaveValue('עריכה מקומית — Local');
  await page.locator('#save-activity').click();
  await expect(page.getByRole('alert')).toContainText('הטיוטה השתנתה בשרת');
  await expect(page.locator('#document-title')).toHaveValue('עריכה מקומית — Local');
  page.once('dialog', (dialog) => dialog.accept());
  await page.locator('#reload-activity').click();
  await expect(page.locator('#document-title')).toHaveValue('תרגול חדש');
});

test('supports keyboard content editing with native labels at 360px and 200% text', async ({
  page,
}) => {
  await isolate(page);
  await page.setViewportSize({ width: 360, height: 800 });
  await page.goto('/activities/draft');
  await page.addStyleTag({ content: 'html { font-size:200%; }' });
  await page.locator('#add-question').focus();
  await page.keyboard.press('Enter');
  await page.getByLabel('נוסח השאלה', { exact: true }).fill('מה פירוש Hello — שלום?');
  await page.getByLabel('סוג התשובה', { exact: true }).selectOption('single-choice');
  await page.getByRole('button', { name: 'הוספת אפשרות', exact: true }).click();
  await page.getByLabel('אפשרות 1', { exact: true }).fill('שלום');
  await page.getByLabel('תשובה להורים', { exact: true }).fill('שלום');
  await page.locator('#question-0-points').fill('1.5');
  await expect(page.locator('#question-0-points-error')).toContainText('מספר שלם');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
  await page.screenshot({
    path: '../.superpowers/sdd/2026-09-30-structured-templates/task6-mobile.png',
    fullPage: true,
  });
});
