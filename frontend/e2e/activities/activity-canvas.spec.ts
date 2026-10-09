import { expect, test, type Page } from '@playwright/test';
import {
  numericPlan,
  suppliedPlan,
  sourceText,
} from '../../src/app/features/activities/learning-plan.fixture';
import limits from '../../src/app/core/api/limits.fixture.json';
import { textSize } from '../text-size';

async function isolate(page: Page, configured = true) {
  const writes: { url: string; body: Record<string, unknown> }[] = [];
  let draft = {
    id: 'draft',
    revision: 1,
    plan: numericPlan,
    document: { title: 'תרגול', instructions: null, materials: [], questions: [] },
    diagnostics: {},
    measurements: [],
    chat: [],
    canUndo: false,
    activeOperationId: null,
    releasedSnapshotId: null,
    releasedSourceRevision: null,
    createdAtUtc: '2026-10-01T00:00:00Z',
    updatedAtUtc: '2026-10-01T00:00:00Z',
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname;
    if (['POST', 'PUT'].includes(request.method()))
      writes.push({ url: path, body: request.postDataJSON() });
    if (path === '/api/auth/me')
      return route.fulfill({ json: { email: 'parent@example.test', familyId: 'isolated' } });
    if (path === '/api/auth/csrf') return route.fulfill({ json: { token: 'isolated' } });
    if (path === '/api/library/changes') return route.fulfill({ status: 204 });
    if (path === '/api/limits') return route.fulfill({ json: limits });
    if (path === '/api/ai/status')
      return route.fulfill({ json: { configured, schemaVersion: numericPlan.schemaVersion } });
    if (path === '/api/activity-drafts' && request.method() === 'POST') {
      draft = { ...draft, ...request.postDataJSON() };
      return route.fulfill({ status: 201, json: draft });
    }
    if (path === '/api/activity-drafts/draft') {
      if (request.method() === 'PUT')
        draft = { ...draft, ...request.postDataJSON(), revision: draft.revision + 1 };
      return route.fulfill({ json: draft });
    }
    throw new Error(`Unexpected isolated request: ${request.method()} ${path}`);
  });
  return writes;
}

function reply(body: { requestId: string; baseRevision: number }, proposal = suppliedPlan) {
  return {
    requestId: body.requestId,
    baseRevision: body.baseRevision,
    proposal,
    clarification: null,
    changes: [{ kind: 'added', path: 'plan' }],
    assumptions: ['כיתה ג׳'],
    generationMetadata: {
      provider: 'isolated',
      model: 'fake',
      promptVersion: 'test',
      generatedAtUtc: '2026-10-01T00:00:00Z',
    },
  };
}

test('clarifies, confirms exact source text and saves the draft with its conversation without starting AI', async ({
  page,
}) => {
  const writes = await isolate(page);
  const authoring: Record<string, unknown>[] = [];
  await page.route('**/api/ai/activity-plans', async (route) => {
    const body = route.request().postDataJSON();
    authoring.push(body);
    await route.fulfill({
      json:
        authoring.length === 1
          ? {
              ...reply(body),
              proposal: null,
              clarification: 'לאיזה גיל?',
              changes: [],
              assumptions: [],
            }
          : reply(body),
    });
  });
  await page.goto('/activities/new');
  await expect(page.locator('#save-activity, #create-activity')).toHaveCount(0);
  await page.getByRole('textbox', { name: 'מה תרצו להכין?' }).fill('תרגול לפי מקור דו לשוני');
  await page.locator('#chat-send').click();
  await expect(page.locator('#chat-clarification')).toHaveText('לאיזה גיל?');
  await page.getByLabel('התשובה שלכם').fill('כיתה ג');
  await page.locator('#chat-send').click();
  await expect(page.locator('#plan-title')).toHaveText('הגדרות הפעילות');
  expect(authoring[1]['context']).toEqual([
    { role: 'parent', text: 'תרגול לפי מקור דו לשוני' },
    { role: 'assistant', text: 'לאיזה גיל?' },
  ]);
  await expect(page.getByLabel('הטקסט שלכם')).toHaveValue(sourceText);
  await page.locator('#save-activity').click();
  await expect(page.getByRole('alert')).toContainText('אשרו שהטקסט שלכם הועתק נכון');
  expect(writes).toEqual([]);
  await page.getByRole('button', { name: 'הטקסט הועתק נכון' }).click();
  await expect(page.getByRole('log')).toContainText('לאיזה גיל?');
  await page.locator('#save-activity').click();
  await expect(page.getByText('נשמר', { exact: true })).toBeVisible();
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({
    url: '/api/activity-drafts',
    body: { plan: { materials: [{ text: sourceText }] } },
  });
  expect(writes[0].body['chat']).toHaveLength(4);
  expect(writes[0].body['chat']).toMatchObject([{}, {}, {}, { assumptions: ['כיתה ג׳'] }]);
  expect(JSON.stringify(writes[0].body)).not.toContain('confirmed');
});

test('editing a source cancels a pending proposal and a failed checkpoint preserves local work', async ({
  page,
}) => {
  await isolate(page);
  let release!: () => void;
  const barrier = new Promise<void>((resolve) => (release = resolve));
  let calls = 0;
  await page.route('**/api/ai/activity-plans', async (route) => {
    const body = route.request().postDataJSON();
    if (++calls > 1) await barrier;
    await route.fulfill({ json: reply(body) }).catch(() => undefined);
  });
  await page.route('**/api/activity-drafts', (route) =>
    route.fulfill({ status: 409, json: { title: 'לא ניתן לשמור כעת' } }),
  );
  await page.goto('/activities/new');
  await page.locator('#chat-message').fill('מקור דו לשוני');
  await page.locator('#chat-send').click();
  await expect(page.getByLabel('הטקסט שלכם')).toBeVisible();
  await page.locator('#chat-message').fill('שינוי');
  await page.locator('#chat-send').click();
  await expect(page.locator('#chat-cancel')).toBeVisible();
  await page.getByLabel('הטקסט שלכם').fill('עריכה מקומית — Hello');
  await expect(page.locator('#chat-cancel')).toBeHidden();
  release();
  await page.getByRole('button', { name: 'הטקסט הועתק נכון' }).click();
  await page.locator('#save-activity').click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByLabel('הטקסט שלכם')).toHaveValue('עריכה מקומית — Hello');
});

test('a saved draft can be edited without AI at 360px and 200% text using the keyboard', async ({
  page,
}) => {
  const writes = await isolate(page, false);
  await page.setViewportSize({ width: 360, height: 800 });
  await page.goto('/activities/draft');
  await textSize(page, 32);
  await expect(page.getByText('העוזר לא זמין כרגע.', { exact: false })).toBeVisible();
  await page.locator('#edit-activity').focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#document-title')).toBeFocused();
  await page.locator('#document-title').fill('תרגול ידני — Hello');
  await page.locator('#save-activity').focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('נשמר', { exact: true })).toBeVisible();
  await expect(page.getByText('תרגול ידני — Hello', { exact: true })).toBeVisible();
  expect(writes).toHaveLength(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('canvas-mobile.png'), fullPage: true });
});
