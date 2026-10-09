import { expect, test, Page } from '@playwright/test';
import { numericPlan } from '../../src/app/features/activities/learning-plan.fixture';
import {
  ActivityDetail,
  GenerationOperation,
  SnapshotPreview,
} from '../../src/app/core/api/models';
import limits from '../../src/app/core/api/limits.fixture.json';
import { textSize } from '../text-size';

async function isolate(page: Page, format: 'numeric-input' | 'single-choice' = 'numeric-input') {
  let signedIn = true;
  let draft: ActivityDetail = {
    id: 'draft',
    revision: 1,
    plan: {
      ...numericPlan,
      questions: {
        ...numericPlan.questions,
        formats: [format],
        choiceCount: format === 'single-choice' ? 2 : null,
      },
    },
    chat: [],
    canUndo: false,
    document: { title: 'תרגול', instructions: null, materials: [], questions: [] },
    diagnostics: { questions: ['נדרשות שתי שאלות'] },
    measurements: [],
    activeOperationId: null,
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
      return route.fulfill(
        signedIn ? { json: { email: 'parent@example.test', familyId: 'family' } } : { status: 401 },
      );
    if (path === '/api/auth/csrf') return route.fulfill({ json: { token: 'isolated' } });
    if (path === '/api/auth/logout') {
      signedIn = false;
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/children' && method === 'GET')
      return route.fulfill({ json: { items: [], page: 1, pageSize: 25, hasMore: false } });
    if (path === '/api/limits') return route.fulfill({ json: limits });
    // No other device exists here; 204 closes the change stream without a retry.
    if (path === '/api/library/changes') return route.fulfill({ status: 204 });
    if (path === '/api/ai/status') return route.fulfill({ json: { configured: true } });
    if (path === '/api/activity-drafts' && method === 'POST') {
      const body = request.postDataJSON();
      draft = { ...draft, id: body.snapshotId ? 'copy' : 'draft', releasedSnapshotId: null };
      return route.fulfill({ status: 201, json: draft });
    }
    if (path === '/api/activity-drafts/draft' && method === 'PUT') {
      if (!signedIn) return route.fulfill({ status: 401 });
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
    if (path === '/api/activity-drafts/draft/operations/op/cancel') {
      const unchanged = draft.revision === operation!.expectedRevision;
      draft = { ...draft, activeOperationId: null };
      operation = {
        ...operation!,
        status: 'cancelled',
        expectedRevision: unchanged ? draft.revision : operation!.expectedRevision,
      };
      return route.fulfill({ json: operation });
    }
    if (path === '/api/activity-drafts/draft/release') {
      expect(request.postDataJSON()).toEqual({ expectedRevision: draft.revision });
      snapshot = {
        archivedAtUtc: null,
        id: 'ready',
        sourceDraftId: draft.id,
        sourceDraftRevision: draft.revision,
        plan: draft.plan,
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
    unknown() {
      if (!operation) throw new Error('No operation started');
      operation = { ...operation, status: 'unknown' };
      draft = { ...draft, activeOperationId: null };
    },
    complete() {
      if (!operation) throw new Error('No operation started');
      operation = {
        ...operation,
        status: 'completed',
        expectedRevision: draft.revision + 1,
        steps: [{ stage: 'questions', outcome: 'accepted', usage: null, metadata: null }],
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
            interaction: {
              type: format,
              options: format === 'single-choice' ? ['שלום', 'אחר'] : null,
            },
            answer: { value: format === 'single-choice' ? 'שלום' : String(i + 1) },
            points: 1,
            origin: { kind: 'generated' },
            acceptance: null,
          })),
        },
      };
    },
  };
}

test('sign-out asks before dropping unsaved work, and saves a waiting edit without asking', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/activities/draft');
  const composer = page.locator('#chat-message');
  await composer.fill('בקשה שלא נשלחה');
  let prompts = 0;
  page.on('dialog', async (dialog) => {
    prompts++;
    await dialog.dismiss();
  });
  const signOut = page.getByRole('button', { name: 'יציאה', exact: true });
  await signOut.click();
  await expect(signOut).toBeEnabled();
  expect(prompts).toBe(1);
  expect(state.writes.filter((write) => write.path === '/api/auth/logout')).toHaveLength(0);
  await expect(page).toHaveURL(/\/activities\/draft$/);
  await expect(composer).toHaveValue('בקשה שלא נשלחה');

  // A valid edit still waiting for its pause saves on the way out instead of asking.
  await composer.fill('');
  await page.locator('#edit-activity').click();
  await page.locator('#document-title').fill('נשמר ביציאה');
  await signOut.click();
  await expect(page).toHaveURL(/\/login$/);
  expect(prompts).toBe(1);
  expect(
    state.writes.find((write) => write.path === '/api/activity-drafts/draft')?.body,
  ).toMatchObject({
    document: { title: 'נשמר ביציאה' },
  });
  expect(state.writes.filter((write) => write.path === '/api/auth/logout')).toHaveLength(1);
});

test('failed sign-out keeps edits and asks again before a later attempt', async ({ page }) => {
  const state = await isolate(page);
  await page.route('**/api/auth/logout', (route) =>
    route.fulfill({ status: 503, json: { title: 'לא ניתן לצאת כעת' } }),
  );
  await page.goto('/activities/draft');
  const composer = page.locator('#chat-message');
  await composer.fill('בקשה שלא נשלחה');
  let prompts = 0;
  page.on('dialog', async (dialog) => {
    prompts++;
    if (prompts === 1) await dialog.accept();
    else await dialog.dismiss();
  });
  const signOut = page.getByRole('button', { name: 'יציאה', exact: true });
  await signOut.click();
  await expect(page.locator('#main-content > [role="alert"]')).toBeVisible();
  expect(prompts).toBe(1);
  await expect(page).toHaveURL(/\/activities\/draft$/);
  await expect(composer).toHaveValue('בקשה שלא נשלחה');
  await page.unroute('**/api/auth/logout');
  await signOut.click();
  await expect(signOut).toBeEnabled();
  expect(prompts).toBe(2);
  expect(state.writes.filter((write) => write.path === '/api/auth/logout')).toHaveLength(0);
});

test('active work locks content while keeping progress and cancellation accessible', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/activities/draft');
  await page.locator('#create-activity').click();
  await expect(page.locator('#chat-cancel')).toBeVisible();
  state.complete();
  await expect(page.getByText('כמה הם 1+1?', { exact: true })).toBeVisible();
  await page.locator('#ask-question-q1').click();
  await page.locator('#chat-send').click();
  await expect(page.locator('#chat-cancel')).toBeVisible();
  await expect(page.locator('#edit-activity')).toHaveAttribute('aria-disabled', 'true');
  await expect(page.locator('#question-0-prompt')).toHaveCount(0);
  await expect(page.locator('app-generation-status')).toHaveCount(1);
  await expect(page.getByText('כמה הם 1+1?', { exact: true })).toBeVisible();
  await page.locator('#chat-cancel').click();
  await expect(page.locator('#chat-cancel')).toHaveCount(0);
  expect(state.writes.filter((write) => write.path.endsWith('/cancel'))).toHaveLength(1);
  await expect(page.getByText('כמה הם 1+1?', { exact: true })).toBeVisible();
});

test('saves before generation, edits manually, reviews the current revision and opens an immutable copy', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/activities/draft');
  await page.locator('#edit-activity').click();
  await page.locator('#document-title').fill('לפני היצירה');
  await page.locator('#create-activity').click();
  await expect(page.locator('#chat-cancel')).toBeVisible();
  expect(state.writes.map((w) => w.path)).toEqual([
    '/api/activity-drafts/draft',
    '/api/activity-drafts/draft/operations',
  ]);
  await expect(page).toHaveURL(/\/activities\/draft\?operation=op$/);
  state.complete();
  await expect(page.getByText('תרגול חדש', { exact: true })).toBeVisible();
  await page.locator('#edit-activity').click();
  await page.locator('#question-0-answer').fill('9');
  await expect(page.getByText('נשמר', { exact: true })).toBeVisible();
  expect(state.writes.filter((w) => w.path.endsWith('/operations'))).toHaveLength(1);
  page.once('dialog', (dialog) => dialog.accept());
  await page.locator('#release-activity').click();
  await page.getByRole('link', { name: 'הקצאה לילדים' }).click();
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

test('supports keyboard content editing with fixed structure at 360px and 200% text', async ({
  page,
}) => {
  const state = await isolate(page, 'single-choice');
  await page.setViewportSize({ width: 360, height: 800 });
  await page.goto('/activities/draft');
  await page.locator('#create-activity').click();
  // On phones the chat is a sheet; its opener shows the running work and leads to Stop.
  await expect(page.locator('#open-chat')).toHaveAttribute('aria-busy', 'true');
  await page.locator('#open-chat').click();
  await expect(page.locator('#chat-cancel')).toBeVisible();
  state.complete();
  await expect(page.locator('#chat-cancel')).toBeHidden();
  await page.keyboard.press('Escape');
  await page.locator('#edit-activity').click();
  await expect(page.locator('#question-0-prompt')).toBeVisible();
  await textSize(page, 32);
  const card = page.locator('li[aria-labelledby="question-0-heading"]');
  await card.getByLabel('נוסח השאלה', { exact: true }).fill('מה פירוש Hello — שלום?');
  await card.getByText('סוג התשובה והניקוד — שאלה 1').focus();
  await page.keyboard.press('Enter');
  await expect(card.locator('#question-0-type')).toHaveText('סוג התשובה: בחירה');
  await expect(card.getByLabel('סוג התשובה', { exact: true })).toHaveCount(0);
  await expect(card.getByRole('button', { name: 'הוספת אפשרות', exact: true })).toHaveCount(0);
  await card.getByLabel('תשובה נכונה').selectOption('שלום');
  await card.getByLabel('אפשרות 1', { exact: true }).fill('שלום רב');
  await expect(card.getByLabel('תשובה נכונה')).toHaveValue('שלום');
  await expect(card.getByLabel('תשובה נכונה')).toHaveAttribute('aria-invalid', 'true');
  await page.locator('#question-0-points').fill('1.5');
  await expect(page.locator('#question-0-points-error')).toBeEmpty();
  await page.locator('#question-0-points').blur();
  await expect(page.locator('#question-0-points-error')).toContainText('מספר שלם');
  const more = page.locator('details:has(#question-0-points) > summary');
  await expect(more.getByText('נדרש תיקון')).toBeHidden();
  await more.click();
  await expect(more.getByText('נדרש תיקון')).toBeVisible();
  await more.click();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await expect(page.locator('#add-question')).toHaveCount(0);
  await page.screenshot({ path: test.info().outputPath('content-mobile.png'), fullPage: true });
});

test('unknown outcomes retain local work and do not automatically start another operation', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/activities/draft');
  await page.locator('#create-activity').click();
  await expect(page.locator('#chat-cancel')).toBeVisible();
  await expect(page.locator('#edit-activity')).toHaveAttribute('aria-disabled', 'true');
  state.unknown();
  await expect(
    page.getByText('לא הפעלנו ניסיון נוסף אוטומטית כדי למנוע חיוב כפול.', { exact: true }),
  ).toBeVisible();
  await expect(page.locator('#check-saved')).toBeVisible();
  await expect(page.locator('#chat-cancel')).toBeHidden();
  await page.locator('#edit-activity').click();
  await expect(page.locator('#document-title')).toBeEnabled();
  await page.locator('#document-title').fill('העבודה שלי נשמרת מקומית');
  await expect(page.locator('#document-title')).toHaveValue('העבודה שלי נשמרת מקומית');
  expect(state.writes.filter((write) => write.path.endsWith('/operations'))).toHaveLength(1);
});
