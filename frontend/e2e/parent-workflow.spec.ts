import { expect, test, type Page } from '@playwright/test';
import { numericPlan } from '../src/app/features/activities/learning-plan.fixture';
import { textSize } from './text-size';

async function login(page: Page, email = 'browser@example.test') {
  await page.goto('/');
  await page.getByLabel('כתובת דוא״ל', { exact: true }).fill(email);
  await page.getByLabel('סיסמה', { exact: true }).fill('TestOnly!Parent12345');
  await page.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(page.getByRole('heading', { name: 'פעילות חדשה' })).toBeVisible();
}

async function propose(page: Page, prompt: string) {
  await page.goto('/activities/new');
  await page.getByRole('textbox', { name: 'מה תרצו להכין?' }).fill(prompt);
  await page.locator('#chat-send').click();
  await expect(page.locator('#plan-name')).not.toHaveValue('');
  await expect(page.locator('#chat-cancel')).toBeHidden();
}

async function start(page: Page) {
  const previous = new URL(page.url()).searchParams.get('operation');
  await page.locator('#generate-activity').click();
  await expect(page).toHaveURL(
    (url) =>
      /^\/activities\/[a-f0-9-]+$/.test(url.pathname) &&
      !!url.searchParams.get('operation') &&
      url.searchParams.get('operation') !== previous,
  );
  const url = new URL(page.url());
  return {
    draftPath: '/api' + url.pathname.replace('/activities/', '/activity-drafts/'),
    operationId: url.searchParams.get('operation')!,
  };
}

async function operation(page: Page, state: { draftPath: string; operationId: string }) {
  return (await page.request.get(state.draftPath + '/operations/' + state.operationId)).json();
}

async function finish(
  page: Page,
  state: { draftPath: string; operationId: string },
  status = 'completed',
) {
  await expect
    .poll(async () => (await operation(page, state)).status, { timeout: 15_000 })
    .toBe(status);
  await expect(page.locator('#cancel-generation')).toBeHidden();
  return (await page.request.get(state.draftPath)).json();
}

async function narrow(page: Page, name: string) {
  await page.setViewportSize({ width: 360, height: 800 });
  await textSize(page, 32);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({
    path: test.info().outputPath(`${name}-mobile.png`),
    fullPage: true,
  });
  await page.locator('footer').scrollIntoViewIfNeeded();
  await expect(page.getByRole('banner')).not.toBeInViewport();
  await textSize(page, 16);
  await page.setViewportSize({ width: 1440, height: 1000 });
}

test('the parent keeps their place: pages open at the top, focus follows the action and the caret starts on the page side', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 600 });
  await login(page, 'races@example.test');
  await page.locator('footer').scrollIntoViewIfNeeded();
  expect(await page.evaluate(() => scrollY)).toBeGreaterThan(0);
  // A DOM click follows the link from where the parent is, without the test runner scrolling first.
  await page
    .getByRole('navigation', { name: 'ניווט ראשי' })
    .getByRole('link', { name: 'המרחב שלנו' })
    .evaluate((link: HTMLElement) => link.click());
  await expect(page.getByRole('heading', { name: 'המרחב שלנו', level: 1 })).toBeVisible();
  await expect.poll(() => page.evaluate(() => scrollY)).toBe(0);

  // A request keeps focus on its own button; content that replaces it takes focus at its heading.
  await propose(page, 'קריאה עם סגנון לבחירה והמתנה');
  // An empty field follows the page direction; typed text sets its own.
  const composer = page.locator('#chat-message');
  const direction = () =>
    composer.evaluate((field) => (field.matches(':dir(rtl)') ? 'rtl' : 'ltr'));
  expect(await direction()).toBe('rtl');
  await composer.fill('Hello');
  await expect.poll(direction).toBe('ltr');
  await composer.fill('');
  await expect.poll(direction).toBe('rtl');
  await page.locator('#save-activity').focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('נשמר', { exact: true })).toBeVisible();
  await expect(page.locator('#save-activity')).toBeFocused();
  await page.locator('#generate-activity').focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#cancel-generation')).toBeVisible();
  // Starting generation keeps focus on its own, now unavailable, button and shows the progress.
  await expect(page.locator('#generate-activity')).toBeFocused();
  await expect(page.locator('app-generation-status')).toBeInViewport();
  await expect(page.locator('#document-heading')).toBeFocused({ timeout: 15_000 });
});

test('prompt to editable activity, independent template, scoped repair and frozen parent preview', async ({
  page,
}) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('lang', 'he');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'דילוג לתוכן הראשי' })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('main')).toBeFocused();
  await login(page);
  const templatesBefore = (await (await page.request.get('/api/templates')).json()).length;
  await propose(page, 'קריאה עם סגנון לבחירה והמתנה');
  await page.getByRole('combobox', { name: 'סגנון', exact: true }).selectOption('סיפורי');
  const state = await start(page);
  await expect(page.locator('#cancel-generation')).toBeVisible();
  await page.reload();
  await expect(page.locator('#cancel-generation')).toBeVisible();
  await expect.poll(async () => (await operation(page, state)).stage).toBe('materials');
  const planned = await operation(page, state);
  expect(planned.steps.map((step: { stage: string }) => step.stage)).toEqual([
    'material-ideas',
    'materials',
  ]);
  expect(planned.expectedRevision).toBe(planned.originalRevision);
  expect((await (await page.request.get(state.draftPath)).json()).revision).toBe(
    planned.originalRevision,
  );
  let draft = await finish(page, state);
  expect((await operation(page, state)).steps.map((step: { stage: string }) => step.stage)).toEqual(
    ['material-ideas', 'materials', 'questions'],
  );
  await expect(page.locator('#question-0-prompt')).toHaveValue('על מה לומדים בקטע? 1');
  expect((await (await page.request.get('/api/templates')).json()).length).toBe(templatesBefore);
  expect(draft.templateVersionId).toBeNull();
  expect(Object.values(draft.input.controlValues)).toEqual(['סיפורי']);
  const secondQuestion = draft.document.questions[1];
  await page.locator('#question-0-improve').click();
  await page.locator('#question-0-improve-submit').click();
  await expect(page.locator('#question-0-prompt')).toHaveValue('שאלה חלופית: מה לומדים?');
  await page.locator('#question-0-prompt').fill('מה למדתם על הדינוזאורים?');
  await page.locator('#save-activity').click();
  await expect(page.getByText('נשמר', { exact: true })).toBeVisible();
  draft = await (await page.request.get(state.draftPath)).json();
  expect(draft.document.questions[1]).toEqual(secondQuestion);
  await narrow(page, 'workspace');
  await page.getByText('שמירה כתבנית לשימוש חוזר', { exact: true }).click();
  await page.locator('#save-template').click();
  await expect(page.getByText('התבנית נשמרה בספרייה. הפעילות לא השתנתה.')).toBeVisible();
  const templates = await (await page.request.get('/api/templates')).json();
  expect(templates).toHaveLength(templatesBefore + 1);
  const template = templates.find((value: { name: string }) => value.name === 'חוקרים וקוראים');
  expect((await (await page.request.get(state.draftPath)).json()).document).toEqual(draft.document);
  await page.locator('#release-activity').click();
  await page.getByRole('link', { name: 'צפייה בפעילות המוכנה' }).click();
  await expect(page.getByRole('heading', { name: 'פעילות מוכנה — תצוגה להורים' })).toBeVisible();
  const frozenUrl = page.url();
  const frozenPath = '/api' + new URL(frozenUrl).pathname;
  const frozen = await (await page.request.get(frozenPath)).json();
  await expect(page.locator('textarea')).toHaveCount(0);
  await expect(page.getByRole('button', { name: /שליחה לילד|הקצאה לילד/ })).toHaveCount(0);
  await page.locator('summary').first().focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('details').first()).toHaveAttribute('open', '');
  await narrow(page, 'snapshot');
  await page.goto('/templates/' + template.id + '/edit');
  await page.getByLabel('שם התבנית', { exact: true }).fill('תבנית ששונתה');
  await page.locator('#save-template').click();
  await expect(page.getByText('התבנית נשמרה בספרייה.', { exact: true })).toBeVisible();
  await page.goto(frozenUrl);
  expect(await (await page.request.get(frozenPath)).json()).toEqual(frozen);
  expect(errors).toEqual([]);
});

test('exact bilingual source bypasses material generation and missing answers block release', async ({
  page,
}) => {
  await login(page, 'source@example.test');
  await propose(page, 'תרגול לפי מקור דו לשוני');
  const source = '"שָׁלוֹם" — Hello!\nDon\'t change בעלי־חיים.\n';
  await expect(page.getByLabel('הטקסט שלכם')).toHaveValue(source);
  await page.locator('#generate-activity').click();
  await expect(page.getByRole('alert')).toContainText('אשרו שהטקסט שלכם הועתק נכון');
  await page.getByRole('button', { name: 'הטקסט הועתק נכון' }).click();
  const state = await start(page);
  const draft = await finish(page, state);
  expect(draft.document.materials[0].body).toBe(source);
  expect((await operation(page, state)).steps.map((step: { stage: string }) => step.stage)).toEqual(
    ['questions'],
  );
  await page.locator('#question-0-answer').fill('');
  await page.locator('#save-activity').click();
  await expect(page.getByText('נשמר', { exact: true })).toBeVisible();
  await expect(page.locator('#question-0-answer-errors')).toHaveText('חסרה תשובה נכונה.');
  await expect(page.getByText('יש לתקן את המסומן בשאלה 1.')).toBeVisible();
  await page.locator('#release-activity').click();
  await expect(page.getByRole('alert')).toBeVisible();
  expect(await (await page.request.get('/api/instances')).json()).toEqual([]);
  await page.locator('#question-0-answer').fill('דינוזאורים');
  await page.locator('#release-activity').click();
  await expect(page.getByRole('link', { name: 'צפייה בפעילות המוכנה' })).toBeVisible();
});

test('strict material rejection stops questions, while a question failure retains accepted material', async ({
  page,
}) => {
  await login(page, 'blockers@example.test');
  await propose(page, 'תרגול קריאה באורך קשיח');
  let state = await start(page);
  let draft = await finish(page, state, 'failed');
  await expect(page.getByText('הטקסט שנוצר לא עמד בדרישת האורך.')).toBeVisible();
  await expect(page.getByText('נדרש: 100–120 מילים', { exact: true })).toBeVisible();
  await expect(page.getByText('הפעילות נוצרה.')).toHaveCount(0);
  expect(draft.document.materials).toEqual([]);
  expect(draft.document.questions).toEqual([]);
  expect((await operation(page, state)).steps.map((step: { stage: string }) => step.stage)).toEqual(
    ['material-ideas', 'materials'],
  );
  await propose(page, 'תרגול קריאה עם כשל בשאלות');
  state = await start(page);
  draft = await finish(page, state, 'failed');
  expect(draft.document.materials[0].body).toContain('מאובנים');
  expect(draft.document.questions).toEqual([]);
  await expect(page.getByText('הטקסט נשמר, אבל יצירת השאלות נכשלה.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'יצירת השאלות שוב' })).toBeVisible();
  await page.reload();
  await expect(page.locator('#material-0-body')).toHaveValue(/מאובנים/);
  expect((await operation(page, state)).steps.map((step: { stage: string }) => step.stage)).toEqual(
    ['material-ideas', 'materials', 'questions'],
  );
  const acceptedMaterials = draft.document.materials;
  await page.getByRole('button', { name: 'יצירת השאלות שוב' }).click();
  await expect(page).not.toHaveURL(new RegExp('operation=' + state.operationId));
  const retried = {
    draftPath: state.draftPath,
    operationId: new URL(page.url()).searchParams.get('operation')!,
  };
  draft = await finish(page, retried, 'failed');
  expect(draft.document.materials).toEqual(acceptedMaterials);
  expect(
    (await operation(page, retried)).steps.map((step: { stage: string }) => step.stage),
  ).toEqual(['questions']);
});

test('question-only generation preserves typing and Undo across late output and stale save', async ({
  page,
}) => {
  await login(page, 'races@example.test');
  await propose(page, 'תרגול חשבון ללא קטע עם המתנה');
  await expect(page.locator('[id$="-length-mode"]')).toHaveCount(0);
  const state = await start(page);
  await page.locator('#document-title').fill('עריכה מקומית');
  await finish(page, state);
  await expect(page.getByRole('heading', { name: 'נוצרה תוצאה בזמן שהמשכתם לערוך' })).toBeVisible();
  await expect(page.locator('#document-title')).toHaveValue('עריכה מקומית');
  await page.locator('#plan-undo').click();
  await expect(page.locator('#document-title')).not.toHaveValue('לומדים על חשבון');
  await page.locator('#document-title').fill('עריכה שחשוב לשמור');
  await page.locator('#save-activity').click();
  await expect(page.getByRole('alert')).toContainText('הטיוטה השתנתה בשרת');
  await expect(page.locator('#document-title')).toHaveValue('עריכה שחשוב לשמור');
  page.once('dialog', (dialog) => dialog.accept());
  await page.locator('#reload-activity').click();
  await expect(page.locator('#document-title')).toHaveValue('לומדים על חשבון');
  expect((await operation(page, state)).steps.map((step: { stage: string }) => step.stage)).toEqual(
    ['questions'],
  );
  await page.getByText('פעולות נוספות', { exact: true }).click();
  await page.locator('#generate-questions').click();
  await expect(page.locator('#cancel-generation')).toBeVisible();
  await page.locator('#document-title').fill('עריכה בזמן ביטול');
  await page.locator('#cancel-generation').click();
  await expect(page.locator('#cancel-generation')).toBeHidden();
  await expect(page.locator('#document-title')).toHaveValue('עריכה בזמן ביטול');
});

test('authoring failures expose safe errors and retain the parent request', async ({ page }) => {
  await login(page, 'failures@example.test');
  for (const [prompt, message] of [
    ['בדיקת כשל', ''],
    ['בדיקת מגבלת פלט', 'מגבלת הפלט'],
    ['בדיקת מכסה', 'מגבלת הבקשות'],
    ['בדיקת מכסה בגוף התשובה', 'מגבלת הבקשות'],
  ]) {
    await page.getByRole('textbox', { name: 'מה תרצו להכין?' }).fill(prompt);
    await page.locator('#chat-send').click();
    await expect(page.getByRole('alert')).toBeVisible();
    if (message) await expect(page.getByRole('alert')).toContainText(message);
    await expect(page.getByRole('alert')).not.toContainText('private provider');
    await expect(page.getByRole('textbox', { name: 'מה תרצו להכין?' })).toHaveValue(prompt);
  }
});

test('a lost start response replays the original key and preserves later local typing', async ({
  page,
}) => {
  await login(page, 'recovery@example.test');
  await propose(page, 'תרגול חשבון ללא קטע עם המתנה');
  const starts: Record<string, unknown>[] = [];
  let acceptedId = '';
  await page.route('**/api/activity-drafts/*/operations', async (route) => {
    starts.push(route.request().postDataJSON());
    const response = await route.fetch();
    const operation = await response.json();
    if (starts.length === 1) {
      expect(response.status()).toBe(202);
      acceptedId = operation.id;
      await route.abort('failed');
    } else {
      expect(operation.id).toBe(acceptedId);
      await route.fulfill({ response });
    }
  });
  await page.locator('#generate-activity').click();
  await expect(page.locator('#recover-start')).toBeVisible();
  await page.locator('#document-title').fill('עריכה אחרי אובדן תשובה');
  await expect(page.locator('#generate-activity')).toBeDisabled();
  await page.locator('#recover-start').click();
  await expect(page.locator('#recover-start')).toBeHidden();
  await expect(page).toHaveURL(new RegExp('operation=' + acceptedId));
  await finish(page, {
    draftPath: '/api' + new URL(page.url()).pathname.replace('/activities/', '/activity-drafts/'),
    operationId: acceptedId,
  });
  expect(starts).toHaveLength(2);
  expect(starts[1]).toEqual(starts[0]);
  await expect(page.locator('#document-title')).toHaveValue('עריכה אחרי אובדן תשובה');
});

test('library deletion confirms intent, preserves independent items, recovers from failure and follows other devices', async ({
  page,
}) => {
  await login(page, 'cleanup@example.test');
  await propose(page, 'תרגול חשבון ללא קטע');
  const state = await start(page);
  await finish(page, state);
  await page.getByText('שמירה כתבנית לשימוש חוזר', { exact: true }).click();
  await page.locator('#save-template').click();
  await expect(page.getByText('התבנית נשמרה בספרייה. הפעילות לא השתנתה.')).toBeVisible();
  await page.locator('#release-activity').click();
  await expect(page.getByRole('link', { name: 'צפייה בפעילות המוכנה' })).toBeVisible();
  await page.goto('/templates');
  const templates = page.locator('section[aria-labelledby="templates-title"]');
  const snapshots = page.locator('section[aria-labelledby="ready-title"]');
  page.once('dialog', (dialog) => dialog.dismiss());
  await templates.getByRole('button', { name: /^מחיקת התבנית: / }).click();
  await expect(templates.locator('article')).toHaveCount(1);
  page.once('dialog', (dialog) => dialog.accept());
  await templates.getByRole('button', { name: /^מחיקת התבנית: / }).click();
  await expect(templates.locator('article')).toHaveCount(0);
  await expect(snapshots.locator('article')).toHaveCount(1);
  expect((await page.request.get(state.draftPath)).status()).toBe(200);
  await page.route('**/api/templates', async (route) => {
    if (route.request().method() === 'DELETE')
      await route.fulfill({ status: 500, json: { title: 'המחיקה נכשלה' } });
    else await route.continue();
  });
  page.once('dialog', (dialog) => dialog.accept());
  await page.getByRole('button', { name: 'איפוס נתוני הלמידה' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(snapshots.locator('article')).toHaveCount(1);
  await page.unroute('**/api/templates');
  page.once('dialog', (dialog) => dialog.accept());
  await page.getByRole('button', { name: 'איפוס נתוני הלמידה' }).click();
  await expect(page.getByText('נתוני הלמידה נמחקו.', { exact: true })).toBeVisible();
  await expect(snapshots.locator('article')).toHaveCount(0);
  expect((await page.request.get(state.draftPath)).status()).toBe(404);
  expect((await page.request.get('/api/auth/me')).status()).toBe(200);

  // Writes this page did not make reach it like another device's; one sign-in keeps the suite
  // within the server's login rate limit.
  const headers = {
    'X-XSRF-TOKEN': (await (await page.request.get('/api/auth/csrf')).json()).token,
  };
  const { schemaVersion } = await (await page.request.get('/api/ai/status')).json();
  const plan = { ...numericPlan, schemaVersion };
  const created = await page.request.post('/api/activity-drafts', {
    headers,
    data: { plan, input: { settings: plan.defaults } },
  });
  const draft = await created.json();
  const drafts = page.locator('section[aria-labelledby="drafts-title"]');
  await drafts.getByRole('link', { name: plan.name }).click();
  await expect(page.locator('#document-title')).toBeVisible();
  const saved = await page.request.put(`/api/activity-drafts/${draft.id}`, {
    headers,
    data: {
      expectedRevision: draft.revision,
      plan,
      input: draft.input,
      document: { title: 'מהטלפון', instructions: null, materials: [], questions: [] },
    },
  });
  expect(saved.status()).toBe(200);
  await expect(page.getByText('הפעילות עודכנה במכשיר אחר')).toBeVisible();
  await page.locator('#reload-activity').click();
  await expect(page.locator('#document-title')).toHaveValue('מהטלפון');
  await expect(page.getByText('הפעילות עודכנה במכשיר אחר')).toBeHidden();
  await page.request.delete(`/api/activity-drafts/${draft.id}`, { headers });
  await expect(page.getByText('הפעילות נמחקה במכשיר אחר')).toBeVisible();
  await expect(page.locator('#reload-activity')).toHaveCount(0);
});

test('two pages follow generation, cancellation and release while preserving edits and recovering a closed stream', async ({
  page,
  context,
}) => {
  test.setTimeout(60_000);
  await login(page, 'cleanup@example.test');
  const headers = {
    'X-XSRF-TOKEN': (await (await page.request.get('/api/auth/csrf')).json()).token,
  };
  const { schemaVersion } = await (await page.request.get('/api/ai/status')).json();
  const plan = { ...numericPlan, schemaVersion, goal: 'תרגול חשבון עם המתנה' };
  const created = await page.request.post('/api/activity-drafts', {
    headers,
    data: { plan, input: { settings: plan.defaults } },
  });
  expect(created.status()).toBe(201);
  const draft = await created.json();
  const path = `/api/activity-drafts/${draft.id}`;
  const editorUrl = `/activities/${draft.id}`;
  const actor = await context.newPage();
  await actor.goto(editorUrl);
  await page.route('**/api/library/changes?*', (route) => route.fulfill({ status: 503 }));
  await page.goto(editorUrl);
  await expect(page.locator('#reconnect-draft')).toBeVisible();
  await page.locator('#document-title').fill('עריכה מקומית שנשמרת כאן');

  const running = await start(actor);
  expect((await (await actor.request.get(path)).json()).revision).toBe(draft.revision);
  await page.unroute('**/api/library/changes?*');
  await page.locator('#reconnect-draft').click();
  await expect(page.locator('#generate-activity')).toHaveAttribute('aria-disabled', 'true');
  await actor.locator('#cancel-generation').click();
  await expect(page.getByText('היצירה בוטלה.', { exact: true })).toBeVisible();
  expect((await operation(actor, running)).status).toBe('cancelled');
  await expect(page.locator('#document-title')).toHaveValue('עריכה מקומית שנשמרת כאן');

  const next = await start(actor);
  await finish(actor, next);
  await expect(page.locator('#cancel-generation')).toBeHidden();
  await expect(page.locator('#available-title')).toBeVisible();
  await expect(page.locator('#document-title')).toHaveValue('עריכה מקומית שנשמרת כאן');
  await actor.locator('#release-activity').click();
  await expect(actor.getByRole('link', { name: 'צפייה בפעילות המוכנה' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'צפייה בפעילות המוכנה' })).toBeVisible();
  await expect(page.locator('#document-title')).toBeDisabled();
  await expect(page.locator('#document-title')).toHaveValue('עריכה מקומית שנשמרת כאן');

  const another = await actor.request.post('/api/activity-drafts', {
    headers,
    data: { plan, input: { settings: plan.defaults } },
  });
  const current = await another.json();
  page.once('dialog', (dialog) => dialog.accept());
  await page.goto(`/activities/${current.id}`);
  await page.locator('#document-title').fill('עריכה בזמן שהעמוד מוסתר');
  // Drive the browser visibility event deterministically; the real EventSource disconnects.
  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  const saved = await actor.request.put(`/api/activity-drafts/${current.id}`, {
    headers,
    data: {
      expectedRevision: current.revision,
      plan,
      input: current.input,
      document: { ...current.document, title: 'שינוי בזמן ההסתרה' },
    },
  });
  expect(saved.status()).toBe(200);
  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await expect(page.getByText('הפעילות עודכנה במכשיר אחר', { exact: true })).toBeVisible();
  await expect(page.locator('#document-title')).toHaveValue('עריכה בזמן שהעמוד מוסתר');
  await actor.request.delete(`/api/activity-drafts/${current.id}`, { headers });
  await expect(page.getByText('הפעילות נמחקה במכשיר אחר', { exact: true })).toBeVisible();
  await expect(page.locator('#reload-activity')).toHaveCount(0);
  await actor.close();
});
