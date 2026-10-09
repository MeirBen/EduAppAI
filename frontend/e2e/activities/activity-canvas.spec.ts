import { expect, test, type Page } from '@playwright/test';
import {
  numericPlan,
  suppliedPlan,
  sourceText,
} from '../../src/app/features/activities/learning-plan.fixture';
import limits from '../../src/app/core/api/limits.fixture.json';
import { ActivityDetail } from '../../src/app/core/api/models';
import { textSize } from '../text-size';

async function isolate(page: Page, configured = true, initial: Partial<ActivityDetail> = {}) {
  const writes: { url: string; body: Record<string, unknown> }[] = [];
  let draft: ActivityDetail = {
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
    ...initial,
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
    if (path === '/api/ai/status') return route.fulfill({ json: { configured } });
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
  await page.locator('#finish-editing').focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#edit-activity')).toBeFocused();
  await expect(page.getByRole('heading', { name: 'תרגול ידני — Hello', level: 3 })).toBeVisible();
  expect(writes).toHaveLength(0);
  await page.locator('#save-activity').focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('נשמר', { exact: true })).toBeVisible();
  await expect(page.getByText('תרגול ידני — Hello', { exact: true })).toBeVisible();
  expect(writes).toHaveLength(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('canvas-mobile.png'), fullPage: true });
});

test('desktop chat stays reachable beside lower questions and returns to page flow for large text and source forms', async ({
  page,
}) => {
  await isolate(page, true, {
    document: {
      title: 'תרגול ארוך',
      instructions: null,
      materials: [],
      questions: Array.from({ length: 12 }, (_, index) => ({
        id: `q${index}`,
        prompt: `שאלה ${index + 1}: ${'תוכן לקריאה ולבדיקה. '.repeat(12)}`,
        interaction: { type: 'numeric-input', options: null },
        answer: { value: '1' },
        points: 1,
        origin: { kind: 'manual' },
        acceptance: null,
      })),
    },
    chat: Array.from({ length: 12 }, (_, index) => ({
      role: index % 2 ? 'assistant' : 'parent',
      text: `הודעה ${index + 1}: ${'תוכן השיחה. '.repeat(12)}`,
      atUtc: '2026-10-01T00:00:00Z',
      target: null,
      operationId: null,
      assumptions: null,
      outcome: null,
    })),
  });
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/activities/draft');
  const chat = page.locator('.chat-panel');
  await page.locator('#ask-question-q10').scrollIntoViewIfNeeded();
  const before = await page.evaluate(() => scrollY);
  await page.locator('#ask-question-q10').click();
  await expect(page.locator('#chat-message')).toBeFocused();
  await expect(page.locator('#chat-message')).toHaveValue('לגבי שאלה 11: ');
  expect(Math.abs((await page.evaluate(() => scrollY)) - before)).toBeLessThan(10);
  const composer = await page.locator('.composer').boundingBox();
  expect(composer!.y).toBeGreaterThan(0);
  expect(composer!.y + composer!.height).toBeLessThan(800);
  const history = page.getByRole('log');
  expect(await history.evaluate((element) => element.scrollHeight > element.clientHeight)).toBe(
    true,
  );
  expect((await history.boundingBox())!.height).toBeGreaterThan(150);
  await expect(history.getByText('הודעה 12:', { exact: false })).toBeInViewport();
  await page.locator('#chat-message').fill('לגבי שאלה 11: נסחו בפשטות');
  await page.locator('#ask-question-q11').click();
  await expect(page.locator('#chat-message')).toHaveValue('לגבי שאלה 12: נסחו בפשטות');
  await expect
    .poll(() =>
      history.evaluate(
        (element) => element.scrollHeight - element.clientHeight - element.scrollTop,
      ),
    )
    .toBeLessThan(1);
  await page.screenshot({ path: test.info().outputPath('chat-desktop.png') });
  await chat.locator('.chat-attachments > summary').click();
  await expect(chat).toHaveCSS('position', 'static');
  await chat.locator('.chat-attachments > summary').click();
  for (const [width, height] of [
    [1280, 800],
    [1920, 1080],
  ]) {
    await page.setViewportSize({ width, height });
    await page.locator('#ask-question-q10').scrollIntoViewIfNeeded();
    const position = await page.evaluate(() => scrollY);
    await page.locator('#ask-question-q10').click();
    await expect(chat).toHaveCSS('position', 'sticky');
    expect(Math.abs((await page.evaluate(() => scrollY)) - position)).toBeLessThan(10);
    const bounds = await page.locator('.composer').boundingBox();
    expect(bounds!.y).toBeGreaterThan(0);
    expect(bounds!.y + bounds!.height).toBeLessThan(height - 100);
  }
  for (const [width, font] of [
    [1440, 32],
    [360, 16],
    [360, 32],
    [320, 32],
  ]) {
    await page.setViewportSize({ width, height: 900 });
    await textSize(page, font);
    await expect(chat).toHaveCSS('position', 'static');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    );
    await page.locator('#ask-question-q11').click();
    await expect(page.locator('#chat-message')).toBeFocused();
    await expect(page.locator('#chat-message')).toBeInViewport();
  }
  await page.screenshot({ path: test.info().outputPath('chat-enlarged.png') });
});
