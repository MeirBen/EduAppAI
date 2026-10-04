import { expect, test, Page } from '@playwright/test';
import { numericPlan } from '../../src/app/features/activities/learning-plan.fixture';
import {
  ActivityDetail,
  GenerationOperation,
  SnapshotPreview,
} from '../../src/app/core/api/models';
import limits from '../../src/app/core/api/limits.fixture.json';

async function isolate(page: Page) {
  let signedIn = true;
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
      return route.fulfill(
        signedIn ? { json: { email: 'parent@example.test', familyId: 'family' } } : { status: 401 },
      );
    if (path === '/api/auth/csrf') return route.fulfill({ json: { token: 'isolated' } });
    if (path === '/api/auth/login') {
      signedIn = true;
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/auth/logout') {
      signedIn = false;
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/limits') return route.fulfill({ json: limits });
    // No other device exists here; 204 closes the change stream without a retry.
    if (path === '/api/library/changes') return route.fulfill({ status: 204 });
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
        artifacts: body.targetId ? { targetId: body.targetId, steps: [] } : null,
      };
      draft = { ...draft, activeOperationId: 'op' };
      return route.fulfill({ status: 202, json: operation });
    }
    if (path === '/api/activity-drafts/draft/operations/op')
      return route.fulfill({ json: operation });
    if (path === '/api/activity-drafts/draft/operations/op/cancel') {
      operation = { ...operation!, status: 'cancelled' };
      draft = { ...draft, activeOperationId: null };
      return route.fulfill({ json: operation });
    }
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

test('sign-out respects a cancelled unsaved-work warning and asks only once when accepted', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/activities/draft');
  await page.locator('#document-title').fill('שינוי שלא נשמר');
  let accept = false;
  let prompts = 0;
  page.on('dialog', async (dialog) => {
    prompts++;
    if (accept) await dialog.accept();
    else await dialog.dismiss();
  });
  const signOut = page.getByRole('button', { name: 'יציאה', exact: true });
  await signOut.click();
  await expect(signOut).toBeEnabled();
  expect(prompts).toBe(1);
  expect(state.writes.filter((write) => write.path === '/api/auth/logout')).toHaveLength(0);
  await expect(page).toHaveURL(/\/activities\/draft$/);
  await expect(page.locator('#document-title')).toHaveValue('שינוי שלא נשמר');
  await page.locator('#save-activity').click();
  await expect(page.getByText('נשמר', { exact: true })).toBeVisible();

  await page.locator('#document-title').fill('שינוי נוסף');
  accept = true;
  await signOut.click();
  await expect(page).toHaveURL(/\/login$/);
  expect(prompts).toBe(2);
  expect(state.writes.filter((write) => write.path === '/api/auth/logout')).toHaveLength(1);
});

test('failed sign-out keeps edits and asks again before a later attempt', async ({ page }) => {
  const state = await isolate(page);
  await page.route('**/api/auth/logout', (route) =>
    route.fulfill({ status: 503, json: { title: 'לא ניתן לצאת כעת' } }),
  );
  await page.goto('/activities/draft');
  await page.locator('#document-title').fill('שינוי שלא נשמר');
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
  await expect(page.locator('#document-title')).toHaveValue('שינוי שלא נשמר');
  await page.unroute('**/api/auth/logout');
  await signOut.click();
  await expect(signOut).toBeEnabled();
  expect(prompts).toBe(2);
  expect(state.writes.filter((write) => write.path === '/api/auth/logout')).toHaveLength(0);
});

test('sign-out still works after Back returns a signed-in parent to the login page', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/login');
  await page.getByLabel('כתובת דוא״ל', { exact: true }).fill('parent@example.test');
  await page.getByLabel('סיסמה', { exact: true }).fill('TestOnly!Parent12345');
  await page.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(page.getByRole('heading', { name: 'פעילות חדשה', exact: true })).toBeVisible();
  await page.goBack();
  await expect(page).toHaveURL(/\/login$/);
  const signOut = page.getByRole('button', { name: 'יציאה', exact: true });
  await signOut.click();
  await expect(signOut).toHaveCount(0);
  expect(state.writes.filter((write) => write.path === '/api/auth/logout')).toHaveLength(1);
});

test('removing a scoped generation target keeps progress and cancellation accessible', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/templates/example/create');
  await page.locator('#generate-activity').click();
  await expect(page.locator('#cancel-generation')).toBeVisible();
  state.complete();
  await expect(page.locator('#question-0-answer')).toHaveValue('2');
  await page.locator('#question-0-improve').click();
  await page.locator('#question-0-improve-submit').click();
  await expect(page.locator('#cancel-generation')).toBeVisible();
  await page.getByText('אפשרויות נוספות לשאלה 1', { exact: true }).click();
  await page.getByRole('button', { name: 'הסרת שאלה 1', exact: true }).click();
  await expect(page.locator('#cancel-generation')).toBeVisible();
  await expect(page.locator('app-generation-status')).toHaveCount(1);
  await expect(page.locator('#generate-activity')).toHaveAttribute('aria-disabled', 'true');
  await page.locator('#cancel-generation').click();
  await expect(page.locator('#cancel-generation')).toHaveCount(0);
  expect(state.writes.filter((write) => write.path.endsWith('/cancel'))).toHaveLength(1);
  await expect(page.locator('#question-0-prompt')).toHaveValue('כמה הם 2+1?');
});

test('saves before generation, edits manually, reviews the current revision and opens an immutable copy', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/templates/example/create');
  await page.locator('#generate-activity').click();
  await expect(page.getByText('יוצרים את הפעילות…', { exact: true })).toBeVisible();
  expect(state.writes.map((w) => w.path)).toEqual([
    '/api/activity-drafts',
    '/api/activity-drafts/draft/operations',
  ]);
  await expect(page).toHaveURL(/\/activities\/draft\?operation=op$/);
  state.complete();
  await expect(page.locator('#document-title')).toHaveValue('תרגול חדש');
  await page.locator('#question-0-answer').fill('9');
  await page.locator('#save-activity').click();
  await expect(page.getByText('נשמר', { exact: true })).toBeVisible();
  expect(state.writes.filter((w) => w.path.endsWith('/operations'))).toHaveLength(1);
  await page.locator('#release-activity').click();
  await page.getByRole('link', { name: 'צפייה בפעילות המוכנה' }).click();
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
  await expect(page.getByLabel('סוג התשובה', { exact: true })).toBeHidden();
  await page.getByText('אפשרויות נוספות לשאלה 1').focus();
  await page.keyboard.press('Enter');
  await page.getByLabel('סוג התשובה', { exact: true }).selectOption('single-choice');
  await page.getByRole('button', { name: 'הוספת אפשרות', exact: true }).click();
  await page.getByLabel('אפשרות 1', { exact: true }).fill('שלום');
  await page.getByLabel('תשובה נכונה').selectOption('שלום');
  await expect(page.getByLabel('תשובה נכונה')).toHaveValue('שלום');
  await page.getByLabel('אפשרות 1', { exact: true }).fill('שלום רב');
  // Editing an option never re-points the answer; the mismatch stays visible for the parent.
  await expect(page.getByLabel('תשובה נכונה')).toHaveValue('שלום');
  await expect(page.getByLabel('תשובה נכונה')).toHaveAttribute('aria-invalid', 'true');
  await page.locator('#question-0-points').fill('1.5');
  await expect(page.locator('#question-0-points-error')).toContainText('מספר שלם');
  // The summary flags the problem only while the field that explains it is hidden.
  const more = page.locator('details:has(#question-0-points) > summary');
  await expect(more.getByText('יש לתקן את הניקוד')).toBeHidden();
  await more.click();
  await expect(more.getByText('יש לתקן את הניקוד')).toBeVisible();
  await more.click();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
  await page.screenshot({
    path: test.info().outputPath('content-mobile.png'),
    fullPage: true,
  });
  // Moving a question keeps focus on its move button; removing one hands it to a neighbour.
  await page.locator('#add-question').focus();
  await page.keyboard.press('Enter');
  await page.getByText('אפשרויות נוספות לשאלה 2').click();
  // Opening content renders a frame later; focus waits until it is visible.
  const moveUp = page.locator('#question-1-move-up');
  await expect(moveUp).toBeVisible();
  await moveUp.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#question-0-move-up')).toBeFocused();
  await page.getByRole('button', { name: 'הסרת שאלה 1', exact: true }).focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#question-0-more')).toBeFocused();
});

test('unknown outcomes retain local work and do not automatically start another operation', async ({
  page,
}) => {
  const state = await isolate(page);
  await page.goto('/templates/example/create');
  await page.locator('#generate-activity').click();
  await expect(page.locator('#cancel-generation')).toBeVisible();
  await page.locator('#document-title').fill('העבודה שלי נשמרת מקומית');
  state.unknown();
  await expect(
    page.getByText('לא הפעלנו ניסיון נוסף אוטומטית כדי למנוע חיוב כפול.', { exact: true }),
  ).toBeVisible();
  await expect(page.locator('#check-saved')).toBeVisible();
  await expect(page.locator('#cancel-generation')).toBeHidden();
  await expect(page.locator('#document-title')).toHaveValue('העבודה שלי נשמרת מקומית');
  expect(state.writes.filter((write) => write.path.endsWith('/operations'))).toHaveLength(1);
});
