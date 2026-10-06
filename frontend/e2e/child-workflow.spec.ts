import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { suppliedPlan } from '../src/app/features/activities/learning-plan.fixture';
import { textSize } from './text-size';

const story = 'פסקה ראשונה בעברית.\n\nSecond paragraph — keep 2 + 3 in order.';
const poem = 'שורה ראשונה\nשורה שנייה\nשורה שלישית';
const exactAnswer = '  תשובה מְנֻקֶּדֶת — exact text\n  ';

test.use({ actionTimeout: 10_000 });

/** Three real interaction types with immutable, manually reviewed test content; no generation calls. */
async function createSnapshot(parent: APIRequestContext, headers: Record<string, string>) {
  const { schemaVersion } = await (await parent.get('/api/ai/status')).json();
  const plan = {
    ...suppliedPlan,
    schemaVersion,
    defaults: { ...suppliedPlan.defaults, questionCount: 3 },
    materials: [
      { ...suppliedPlan.materials[0], text: story },
      {
        ...suppliedPlan.materials[0],
        id: '22222222222222222222222222222222',
        label: 'שיר',
        text: poem,
      },
    ],
    questions: {
      ...suppliedPlan.questions,
      formats: ['numeric-input', 'text-input', 'single-choice'],
      choiceCount: { value: 2, adjustable: false },
    },
  };
  const created = await parent.post('/api/activity-drafts', {
    headers,
    data: { plan, input: { settings: plan.defaults } },
  });
  expect(created.status()).toBe(201);
  const draft = await created.json();
  const path = `/api/activity-drafts/${draft.id}`;
  const saved = await parent.put(path, {
    headers,
    data: {
      expectedRevision: draft.revision,
      plan,
      input: draft.input,
      document: {
        title: 'מסע הקריאה שלי',
        instructions: 'קראו וענו בקצב שלכם.',
        materials: [
          { id: plan.materials[0].id, title: null, body: story },
          { id: plan.materials[1].id, title: null, body: poem },
        ],
        questions: [
          {
            id: null,
            prompt: '2 + 3 = ?',
            interaction: { type: 'numeric-input' },
            answer: { value: '5' },
            points: 2,
          },
          {
            id: null,
            prompt: 'מה למדתם מהטקסט?',
            interaction: { type: 'text-input' },
            answer: { value: 'private-child-workflow-key' },
            points: 3,
          },
          {
            id: null,
            prompt: 'איזו אפשרות בחרתם?',
            interaction: { type: 'single-choice', options: ['אדום', 'blue'] },
            answer: { value: 'blue' },
            points: 1,
          },
        ],
      },
    },
  });
  expect(saved.status(), await saved.text()).toBe(200);
  const released = await parent.post(path + '/release', {
    headers,
    data: { expectedRevision: (await saved.json()).revision },
  });
  expect(released.status()).toBe(201);
  return released.json();
}

async function narrow(page: Page, name: string) {
  await page.setViewportSize({ width: 360, height: 800 });
  await textSize(page, 32);
  await expect
    .poll(() => page.evaluate(() => getComputedStyle(document.documentElement).fontSize))
    .toBe('32px');
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    .toBe(true);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ animations: 'disabled', path: test.info().outputPath(name + '.png') });
  if (await page.locator('#answer-form').count()) {
    await page.locator('#answer-form input').scrollIntoViewIfNeeded();
    await page.screenshot({
      animations: 'disabled',
      path: test.info().outputPath(name + '-numeric.png'),
    });
    await page.locator('#answer-form select').scrollIntoViewIfNeeded();
    await page.screenshot({
      animations: 'disabled',
      path: test.info().outputPath(name + '-choice.png'),
    });
  }
  await page.locator('footer').scrollIntoViewIfNeeded();
  await expect(page.getByRole('banner')).not.toBeInViewport();
  await page.screenshot({
    animations: 'disabled',
    path: test.info().outputPath(name + '-end.png'),
  });
  await textSize(page, 16);
  await page.setViewportSize({ width: 1280, height: 900 });
}

test('child activates, resumes all answer types, recovers a committed submission and receives parent grades', async ({
  page: parent,
  browser,
  request,
}) => {
  test.setTimeout(120_000);
  const callsBefore = await (await request.get('http://127.0.0.1:5203/__stats')).json();
  await parent.goto('/login');
  await parent.getByLabel('כתובת דוא״ל', { exact: true }).fill('source@example.test');
  await parent.getByLabel('סיסמה', { exact: true }).fill('TestOnly!Parent12345');
  await parent.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(parent).toHaveURL('/activities/new');
  const headers = {
    'X-XSRF-TOKEN': (await (await parent.request.get('/api/auth/csrf')).json()).token,
  };
  const created = await parent.request.post('/api/children', { headers, data: { name: 'נועה' } });
  expect(created.status()).toBe(201);
  const profile = await created.json();
  const issued = await parent.request.post(`/api/children/${profile.id}/activation`, {
    headers,
    data: { deviceLabel: 'מכשיר הילדה' },
  });
  expect(issued.status()).toBe(200);
  const { code } = await issued.json();
  const snapshot = await createSnapshot(parent.request, headers);
  const assigned = await parent.request.post('/api/assignments', {
    headers,
    data: { childId: profile.id, snapshotId: snapshot.id },
  });
  expect(assigned.status()).toBe(201);
  const assignment = await assigned.json();
  const baseURL = new URL(parent.url()).origin;
  const context = await browser.newContext({ baseURL, serviceWorkers: 'block' });
  const child = await context.newPage();
  const childRequests: string[] = [];
  let submissions = 0;
  child.on('request', (req) => {
    if (new URL(req.url()).pathname.startsWith('/api/'))
      childRequests.push(new URL(req.url()).pathname);
    if (req.method() === 'POST' && req.url().endsWith('/session/submit')) submissions++;
  });
  try {
    await child.goto('/child');
    await expect(child).toHaveURL('/child/activate');
    await expect(child.getByText('הדפדפן הזה ישמור גישה', { exact: false })).toBeVisible();
    await narrow(child, 'activation-mobile');
    await child.getByLabel('קוד הפעלה', { exact: true }).fill(code);
    await child.getByRole('button', { name: 'פתיחת הפעילויות שלי' }).click();
    await expect(child).toHaveURL('/child');
    await expect(
      child.getByRole('link', { name: snapshot.document.title, exact: true }),
    ).toBeVisible();
    await expect(child.getByRole('navigation', { name: 'ניהול המשפחה' })).toHaveCount(0);
    expect(
      await child.evaluate(() => JSON.stringify(localStorage) + JSON.stringify(sessionStorage)),
    ).not.toContain(code);
    await child.reload();
    await expect(child).toHaveURL('/child');
    await narrow(child, 'inbox-mobile');
    const detailResponse = child.waitForResponse((r) =>
      r.url().endsWith(`/api/child/assignments/${assignment.id}`),
    );
    await child.getByRole('link', { name: snapshot.document.title, exact: true }).click();
    const detail = await (await detailResponse).json();
    expect(Object.keys(detail).sort()).toEqual([
      'createdAtUtc',
      'document',
      'id',
      'revision',
      'status',
    ]);
    expect(Object.keys(detail.document).sort()).toEqual([
      'instructions',
      'materials',
      'questions',
      'title',
    ]);
    for (const material of detail.document.materials)
      expect(Object.keys(material).sort()).toEqual(['body', 'id', 'title']);
    for (const question of detail.document.questions) {
      expect(Object.keys(question).sort()).toEqual(['id', 'interaction', 'points', 'prompt']);
      expect(Object.keys(question.interaction).sort()).toEqual(['options', 'type']);
    }
    expect(JSON.stringify(detail)).not.toContain('private-child-workflow-key');
    await expect(child.locator('[data-material]').first()).toHaveText(story);
    expect(await child.locator('[data-material]').first().textContent()).toBe(story);
    expect(await child.locator('[data-material]').last().textContent()).toBe(poem);
    const numeric = child.getByRole('textbox', { name: '2 + 3 = ?', exact: true });
    const text = child.getByRole('textbox', { name: 'מה למדתם מהטקסט?', exact: true });
    const choice = child.getByRole('combobox', { name: 'איזו אפשרות בחרתם?', exact: true });
    await numeric.fill('-');
    await text.fill(exactAnswer);
    await choice.selectOption('blue');
    child.once('dialog', (dialog) => dialog.dismiss());
    await child.getByRole('link', { name: 'לפעילויות שלי', exact: true }).click();
    await expect(child).toHaveURL(`/child/assignments/${assignment.id}`);
    await child.getByRole('button', { name: 'שמירת התשובות', exact: true }).click();
    await expect(child.getByText('התשובות נשמרו.', { exact: true })).toBeVisible();
    await child.reload();
    await expect(numeric).toHaveValue('-');
    await expect(text).toHaveValue(exactAnswer);
    await expect(choice).toHaveValue('blue');
    await child.getByRole('button', { name: 'הגשת העבודה', exact: true }).click();
    await expect(numeric).toBeFocused();
    expect(submissions).toBe(0);
    await numeric.fill('+05.00');
    await narrow(child, 'player-mobile-light');
    await child
      .getByRole('group', { name: 'מצב תצוגה' })
      .locator('label', { hasText: 'כהה' })
      .click();
    await narrow(child, 'player-mobile-dark');
    const sessionPath = `/api/child/assignments/${assignment.id}/session`;
    await child.route(
      '**' + sessionPath + '/submit',
      async (route) => {
        const response = await route.fetch();
        expect(response.status()).toBe(200);
        await route.abort('failed');
      },
      { times: 1 },
    );
    await child.getByRole('button', { name: 'הגשת העבודה', exact: true }).click();
    await expect(child.getByRole('alert')).toContainText('ייתכן שהבקשה נשמרה');
    await expect(text).toHaveValue(exactAnswer);
    await child.getByRole('button', { name: 'בדיקת העבודה השמורה', exact: true }).click();
    await expect(child.getByRole('heading', { name: 'העבודה הוגשה', exact: true })).toBeVisible();
    await expect(text).toBeDisabled();
    await expect(child.getByText('ציון סופי:', { exact: false })).toHaveCount(0);
    expect(submissions).toBe(1);
    child.once('dialog', (dialog) => dialog.accept());
    await child.getByRole('button', { name: 'טעינת העבודה השמורה', exact: true }).click();
    expect(
      await child
        .locator(`[data-saved-answer="${snapshot.document.questions[1].id}"]`)
        .textContent(),
    ).toBe(exactAnswer);
    const receipt = await (await context.request.get(sessionPath)).json();
    expect(Object.keys(receipt).sort()).toEqual([
      'answers',
      'assignmentId',
      'finalTotal',
      'possibleTotal',
      'reviewedAtUtc',
      'revision',
      'savedAtUtc',
      'startedAtUtc',
      'status',
      'submittedAtUtc',
    ]);
    for (const answer of receipt.answers)
      expect(Object.keys(answer).sort()).toEqual(['questionId', 'value']);
    expect(receipt.finalTotal).toBeNull();
    await narrow(child, 'receipt-mobile');
    await parent.goto(`/assignments/${assignment.id}`);
    expect(
      await parent
        .locator(`[data-child-answer="${snapshot.document.questions[1].id}"]`)
        .textContent(),
    ).toBe(exactAnswer);
    await parent.getByLabel('נקודות לשאלה 2 — בין 0 ל־3', { exact: true }).fill('2');
    parent.once('dialog', (dialog) => dialog.accept());
    await parent.getByRole('button', { name: 'שמירת ציונים וסיום הבדיקה', exact: true }).click();
    await expect(parent.getByText('ציון סופי:', { exact: false })).toBeVisible();
    await child.getByRole('button', { name: 'בדיקת העבודה השמורה', exact: true }).click();
    await expect(child.getByText('ציון סופי:', { exact: false })).toContainText('5 מתוך 6');
    await child.getByRole('button', { name: 'טעינת העבודה השמורה', exact: true }).click();
    await child.getByRole('link', { name: 'לפעילויות שלי', exact: true }).click();
    await child
      .getByRole('combobox', { name: 'איזה פעילויות להציג?', exact: true })
      .selectOption('submitted');
    await expect(
      child.getByRole('link', { name: snapshot.document.title, exact: true }),
    ).toBeVisible();
    await expect(child.getByText('הבדיקה הושלמה.', { exact: true })).toHaveCount(0);
    expect(
      await child.evaluate(() => JSON.stringify(localStorage) + JSON.stringify(sessionStorage)),
    ).not.toContain(exactAnswer);
    expect(childRequests.filter((path) => !path.startsWith('/api/child/'))).toEqual([]);
    child.once('dialog', (dialog) => dialog.accept());
    await child.getByRole('button', { name: 'ניתוק המכשיר', exact: true }).click();
    await expect(child).toHaveURL('/child/activate');
    expect((await context.request.get('/api/child/auth/me')).status()).toBe(401);
    const devices = await (await parent.request.get(`/api/children/${profile.id}/devices`)).json();
    expect(devices.items[0].revokedAtUtc).not.toBeNull();
    expect(await (await request.get('http://127.0.0.1:5203/__stats')).json()).toEqual(callsBefore);
  } finally {
    await context.close();
  }
});
