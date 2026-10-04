import { expect, test, Page } from '@playwright/test';
import {
  numericPlan,
  suppliedPlan,
  sourceText,
} from '../../src/app/features/activities/learning-plan.fixture';
import limits from '../../src/app/core/api/limits.fixture.json';

async function isolate(page: Page, configured = true) {
  const writes: { url: string; body: Record<string, unknown> }[] = [];
  await page.route('**/api/**', async (route) => {
    const request = route.request(),
      path = new URL(request.url()).pathname;
    if (request.method() === 'POST') writes.push({ url: path, body: request.postDataJSON() });
    if (path === '/api/auth/me')
      return route.fulfill({ json: { email: 'parent@example.test', familyId: 'isolated' } });
    if (path === '/api/auth/csrf') return route.fulfill({ json: { token: 'isolated' } });
    if (path === '/api/limits') return route.fulfill({ json: limits });
    if (path === '/api/ai/status')
      return route.fulfill({ json: { configured, schemaVersion: numericPlan.schemaVersion } });
    if (path === '/api/templates/example')
      return route.fulfill({
        json: { id: 'example', versionId: 'v3', currentVersion: 3, definition: numericPlan },
      });
    if (path === '/api/templates' && request.method() === 'POST')
      return route.fulfill({
        json: {
          id: 'saved',
          versionId: 'v1',
          currentVersion: 1,
          definition: request.postDataJSON(),
        },
      });
    throw new Error(`Unexpected isolated request: ${request.method()} ${path}`);
  });
  return writes;
}

test('clarifies, confirms exact source text and saves only the reusable plan', async ({ page }) => {
  const writes = await isolate(page);
  const authoring: Record<string, unknown>[] = [];
  await page.route('**/api/ai/template-drafts', async (route) => {
    const body = route.request().postDataJSON();
    authoring.push(body);
    await route.fulfill({
      json: {
        requestId: body.requestId,
        baseRevision: body.baseRevision,
        proposal: authoring.length === 1 ? null : suppliedPlan,
        clarification: authoring.length === 1 ? 'לאיזה גיל?' : null,
        changes: authoring.length === 1 ? [] : [{ kind: 'added', path: 'plan' }],
        assumptions: [],
        generationMetadata: {
          provider: 'isolated',
          model: 'fake',
          promptVersion: 'test',
          generatedAtUtc: '2026-10-01T00:00:00Z',
        },
      },
    });
  });
  await page.goto('/activities/new');
  await expect(page.locator('#save-template')).toHaveCount(0);
  await expect(page.locator('#generate-activity')).toHaveCount(0);
  await page.getByRole('textbox', { name: 'מה תרצו להכין?' }).fill('תרגול לפי מקור דו לשוני');
  await page.getByRole('button', { name: 'שליחה', exact: true }).click();
  await expect(page.locator('#chat-clarification')).toHaveText('לאיזה גיל?');
  expect(authoring).toHaveLength(1);
  await page.getByLabel('התשובה שלכם').fill('כיתה ג');
  await page.locator('#chat-send').click();
  await expect(page.getByRole('heading', { name: 'הגדרות הפעילות' })).toBeVisible();
  await expect(page.locator('#plan-name')).toHaveValue('מספרים');
  await expect(page.locator('#plan-name')).toBeHidden();
  expect(authoring[1]['context']).toEqual([
    { role: 'parent', text: 'תרגול לפי מקור דו לשוני' },
    { role: 'assistant', text: 'לאיזה גיל?' },
  ]);
  await expect(page.getByLabel('הטקסט שלכם')).toHaveValue(sourceText);
  await expect(page.getByText('בדקו שהטקסט הועתק נכון לפני שממשיכים.')).toBeVisible();
  await page.getByText('שמירה כתבנית לשימוש חוזר', { exact: true }).click();
  await page.getByRole('button', { name: 'שמירה כתבנית', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('ואשרו את הטקסט שלכם');
  expect(writes).toEqual([]);
  await page.getByRole('button', { name: 'הטקסט הועתק נכון' }).click();
  await expect(page.getByText('בדקו שהטקסט הועתק נכון לפני שממשיכים.')).toBeHidden();
  await page.locator('#save-template').click();
  await expect(page.getByText('התבנית נשמרה בספרייה. הפעילות לא השתנתה.')).toBeVisible();
  expect(writes).toHaveLength(1);
  expect(writes[0].url).toBe('/api/templates');
  expect(writes[0].body).toMatchObject({ materials: [{ text: sourceText }] });
  expect(JSON.stringify(writes[0].body)).not.toContain('confirmed');
});

test('local typing wins over a pending author request and publication conflicts preserve it', async ({
  page,
}) => {
  await isolate(page);
  let release!: () => void;
  const barrier = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/api/ai/template-drafts', async (route) => {
    const body = route.request().postDataJSON();
    await barrier;
    await route
      .fulfill({
        json: {
          requestId: body.requestId,
          baseRevision: body.baseRevision,
          proposal: { ...numericPlan, name: 'הצעה ישנה' },
          clarification: null,
          changes: [{ kind: 'changed', path: 'name' }],
          assumptions: [],
          generationMetadata: {
            provider: 'isolated',
            model: 'fake',
            promptVersion: 'test',
            generatedAtUtc: '2026-10-01T00:00:00Z',
          },
        },
      })
      .catch(() => undefined);
  });
  await page.route('**/api/templates/example/versions', async (route) => {
    expect(route.request().postDataJSON().expectedVersion).toBe(3);
    await route.fulfill({ status: 409, json: { title: 'התבנית השתנתה' } });
  });
  await page.goto('/templates/example/edit');
  await page.locator('#chat-message').fill('שינוי');
  await page.locator('#chat-send').click();
  await expect(page.locator('#chat-cancel')).toBeVisible();
  await page.getByLabel('שם התבנית').fill('עריכה מקומית');
  await expect(page.locator('#chat-cancel')).toBeHidden();
  release();
  await page.locator('#save-template').click();
  await expect(page.getByRole('alert')).toContainText('השינויים שלכם נשארים כאן');
  await expect(page.getByLabel('שם התבנית')).toHaveValue('עריכה מקומית');
});

test('direct editing works without AI at 360px and 200% text with keyboard-accessible controls', async ({
  page,
}) => {
  await isolate(page, false);
  await page.setViewportSize({ width: 360, height: 800 });
  await page.goto('/templates/new');
  await page.addStyleTag({ content: 'html { font-size: 200%; }' });
  await expect(page.getByText('יצירה בעזרת AI אינה זמינה כרגע.', { exact: false })).toBeVisible();
  await page.getByLabel('שם התבנית').fill('תרגול ידני');
  await page.getByLabel('מה רוצים ללמוד או לתרגל?').fill('תרגול מספרים');
  await page.locator('#activity-topic').fill('חשבון');
  await page.locator('#activity-audience').fill('כיתה ג');
  await page.locator('#activity-questionCount').fill('3');
  await page.locator('#save-template').focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('התבנית נשמרה בספרייה.', { exact: true })).toBeVisible();
  await expect(page.locator('[id$="-length-mode"]')).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
  await page.screenshot({
    path: test.info().outputPath('plan-mobile.png'),
    fullPage: true,
  });
});
